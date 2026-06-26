using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inkform.Gameplay;
using Inkform.Data;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 编排层：纳米机器人生命周期状态机 + 玩家主控角色。
    /// 三状态：游荡 Wander ↔ 延伸 Extend ↔ 附身 Possess。所有切换都经过延伸。
    ///   · 游荡：蜂群成团跟随控制体(Player)，不可跳。
    ///   · 延伸：蔓延管线（贴地分叉立起 + 方管 + 束缚带 + 子触手）把身体送到目标并包裹，玩家冻结。
    ///   · 附身：控制体贴合到物体，物体随 WASD 驾驶移动、相机跟随、可跳；蜂群贴附其表面。
    /// 输入：Shift=扫描/切候选；E=附身选中目标(有高亮) 或 脱离(附身中无高亮)。
    ///
    /// 身体层(NanobotSwarm)和目标层(Possessable)互不知道对方，全靠这里编排。
    /// 几何即谜题规则：ResolveFootAndContact 的任一 raycast 失败 = 不可附身
    /// （悬空/底下没地/隔玻璃自动被排除，不另写可附身判定）。
    /// 跳跃由 MovementProfile.CanJump 把关：游荡 false、附身 true。
    /// </summary>
    public class PossessionDirector : MonoBehaviour
    {
        public enum State { Wander, Extend, Possess }

        [Header("引用")]
        public NanobotSwarm Swarm;
        public Transform Player;
        [Tooltip("玩家视觉体（游荡时显示=蜂群的核；附身时隐藏，物体即化身）。")]
        public Transform PlayerVisual;
        [Tooltip("可选：监听 Shift=扫描 / E=附身·脱离。留空则只能脚本调用。")]
        public InputReader Input;
        [Tooltip("可选：方管渲染器。留空则不画管。")]
        public NanobotTubeRenderer Tubes;

        [Header("扫描")]
        [Tooltip("以玩家为中心的扫描半径。")]
        public float ScanRadius = 7f;
        [Tooltip("可附身物体所在层。")]
        public LayerMask PossessableMask;
        [Tooltip("地面层（求目标正下方落点 F / 脱离聚合的地面点用）。")]
        public LayerMask GroundMask;

        [Header("流动手感")]
        public float BlobSpeed = 1f;
        [Tooltip("树状生长的推进速度（小=慢、更 cinematic）。")]
        public float PathSpeed = 0.35f;

        [Header("附身/脱离")]
        [Tooltip("【兜底】无蔓延树时，附身后蜂群贴附物体表面用的采样点数。")]
        public int WrapSampleCount = 64;
        [Tooltip("脱离时聚合地面点距物体的水平半径。")]
        public float DetachGroundRadius = 2.5f;
        [Tooltip("地面传送时控制体抬离地面高度（≈胶囊半高，防嵌地）。")]
        public float PlayerGroundOffset = 1.1f;
        [Tooltip("附身后空中主干方管从根流空的时长（秒）。")]
        public float TrunkDrainTime = 0.4f;

        [Header("蔓延树（预解算：贴地爬到接触点 P → 触面爆叉 → 表面均匀覆盖）")]
        [Tooltip("叶子末梢数：表面均匀终点个数（覆盖分辨率）。bot 按 i%LeafCount 分摊。")]
        public int LeafCount = 64;
        [Tooltip("表面段细分段数：>0 时把贴面段投影回最近表面点，避免切入凹陷(穿模)。")]
        public int SurfaceSubdiv = 2;
        [Tooltip("贴地主干每米细分点数：越大越贴合地形起伏。")]
        public float GroundSamplesPerUnit = 2f;
        [Tooltip("贴地主干离地高度，避免 bot 嵌进地面。")]
        public float GroundClearance = 0.12f;
        [Tooltip("分支粗细：同叶多 bot 的横向簇宽。")]
        public float BranchThickness = 0.3f;
        [Tooltip("分形抖动幅度（本树纯贴面，抖动基本不生效，保留备用）。")]
        public float JitterAmp = 0.3f;
        [Tooltip("分形抖动噪声频率。")]
        public float JitterScale = 0.6f;
        [Tooltip("抖动沿时间漂移速度。")]
        public float FlowSpeed = 0.4f;
        [Tooltip("队伍在路上拉散程度（弧长单位）。")]
        public float Trail = 2f;

        public State Current { get; private set; } = State.Wander;

        readonly List<Possessable> _candidates = new();
        int _selected;
        Coroutine _co;
        Possessable _current;       // 当前附身物体（游荡时 null）
        NanobotTree _tree;          // 当前预解算的蔓延树（供 Gizmos 可视化）
        PlayerMotor _motor;
        Rigidbody _playerRb;

        // 两套移动 profile：唯一差别是 CanJump（游荡不可跳、附身可跳）。
        static readonly MovementProfile WanderProfile = new()
        { MoveSpeedMul = 1f, MassMul = 1f, JumpHeightMul = 1f, Buoyancy = 0f, Drag = 0f, CanJump = false };
        static readonly MovementProfile PossessProfile = new()
        { MoveSpeedMul = 1f, MassMul = 1f, JumpHeightMul = 1f, Buoyancy = 0f, Drag = 0f, CanJump = true };

        void Start()
        {
            if (Player != null)
            {
                _motor = Player.GetComponent<PlayerMotor>();
                _playerRb = Player.GetComponent<Rigidbody>();
            }
            EnterWander();
        }

        void OnEnable()
        {
            if (Input != null)
            {
                Input.ScanPressed += OnScan;
                Input.InteractPressed += OnConfirm;
            }
        }

        void OnDisable()
        {
            if (Input != null)
            {
                Input.ScanPressed -= OnScan;
                Input.InteractPressed -= OnConfirm;
            }
        }

        // ── 输入 ──
        // Shift：未扫描则开扫并高亮最近，已扫描则循环切候选。延伸过程中忽略。
        void OnScan()
        {
            if (Current == State.Extend) return;
            if (_candidates.Count == 0) BeginScan();
            else CycleCandidate();
        }

        // E：有高亮候选 → 附身它（游荡→附身 / 附身→附身）；否则附身中 → 脱离。
        void OnConfirm()
        {
            if (Current == State.Extend) return;
            if (_candidates.Count > 0) Possess(_candidates[_selected]);
            else if (_current != null) Detach();
        }

        // ── 状态 ──

        void EnterWander()
        {
            ClearHighlights();
            _candidates.Clear();
            _current = null;
            Current = State.Wander;
            SetControlFrozen(false);
            _motor?.ApplyProfile(WanderProfile);   // 不可跳
            if (PlayerVisual != null) PlayerVisual.gameObject.SetActive(true);
            if (Tubes != null) Tubes.Clear();
            if (Swarm != null && Player != null)
                Swarm.SetFormation(new BlobFormation(() => Player.position), BlobSpeed);
        }

        /// <summary>开扫：以玩家为中心找范围内可附身物体并高亮（排除当前附身物）。</summary>
        public void BeginScan()
        {
            if (Swarm == null || Player == null) return;
            _candidates.Clear();
            ClearHighlights();

            var hits = Physics.OverlapSphere(Player.position, ScanRadius, PossessableMask,
                QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
            {
                var p = c.GetComponentInParent<Possessable>();
                if (p != null && p != _current && !_candidates.Contains(p)) _candidates.Add(p);
            }

            if (_candidates.Count == 0)
            {
                Debug.Log("[PossessionDirector] 扫描范围内无可附身物体。");
                return;
            }

            // 默认选最近的
            _candidates.Sort((a, b) =>
                (a.transform.position - Player.position).sqrMagnitude
                .CompareTo((b.transform.position - Player.position).sqrMagnitude));
            _selected = 0;
            RefreshHighlight();
            Debug.Log($"[PossessionDirector] 扫描到 {_candidates.Count} 个候选。Shift 切换，E 确认。");
        }

        void CycleCandidate()
        {
            if (_candidates.Count == 0) return;
            _selected = (_selected + 1) % _candidates.Count;
            RefreshHighlight();
        }

        void RefreshHighlight()
        {
            for (int i = 0; i < _candidates.Count; i++)
                _candidates[i].Highlighted = (i == _selected);
        }

        void ClearHighlights()
        {
            foreach (var p in _candidates) if (p != null) p.Highlighted = false;
        }

        /// <summary>对选定目标启动附身（游荡→附身 或 附身→附身）。几何够不到则忽略。</summary>
        public void Possess(Possessable target)
        {
            if (target == null || target == _current) return;
            if (!ResolveFootAndContact(target, out Vector3 foot, out Vector3 contact))
            {
                Debug.Log($"[PossessionDirector] {target.name} 不可附身（悬空/底下没地/被遮挡）。");
                return;
            }
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ExtendToPossessRoutine(target, foot, contact));
        }

        /// <summary>脱离附身：蜂群从物体表面聚合回地面点成团 → 回游荡。</summary>
        public void Detach()
        {
            if (_current == null) return;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(DetachRoutine(_current));
        }

        IEnumerator ExtendToPossessRoutine(Possessable target, Vector3 foot, Vector3 contact)
        {
            ClearHighlights();
            _candidates.Clear();
            target.Highlighted = true; // 选中目标保持高亮直到包裹

            Current = State.Extend;
            SetControlFrozen(true);
            _ = foot; // 落点 F 已在 Possess 闸门用过；树根在接触点 P，这里不再需要 F。

            // 附身→附身：先释放旧物体（留原地、还原碰撞与外观）。
            if (_current != null && _current != target) Release(_current);
            _current = null;

            // ① 触手伸出前**一次性预解算整棵蔓延树**（全程贴面、不离地/不离面）：
            //    蜂群 → **贴地爬行**到接触点 P(物体正下方贴地点) → 沿物体表面递归二分爆叉
            //    → 表面均匀叶子。airBranchDepth=0/outwardLift=0 → 整棵树贴面、空中不分叉；
            //    surfaceSubdiv+projector → 表面段投影回最近表面点，分支不穿模；
            //    最远点采样叶子 → 覆盖整表面均匀。树只发散不汇聚（分离后不回聚）。
            Vector3[] trunkPath = BuildGroundCrawl(Swarm.Centroid, foot, contact);
            Vector3[] leaves = target.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);
            var projector = SurfaceSubdiv > 0 ? target.GetSurfaceProjector() : null;
            var tree = NanobotTree.Build(start: trunkPath[0], leafPoints: leaves,
                surfaceCenter: target.Bounds.center,
                airBranchDepth: 0, outwardLift: 0f,
                surfaceSubdiv: SurfaceSubdiv, projectToSurface: projector,
                trunkPath: trunkPath);
            _tree = tree; // 供 Gizmos 验收

            // ② 驱动蜂群沿树生长（统一生长前沿，bot 按 i%LeafCount 分摊到各叶路径）。
            Swarm.SetFormation(new TreeBranchFormation(tree, Trail, BranchThickness,
                JitterAmp, JitterScale, FlowSpeed), PathSpeed);

            // ③ 驱动方管：抽取**不重叠**的分支中心线 + 根弧长偏移，按全局前沿深度推进生长。
            if (Tubes != null)
            {
                float maxArc = tree.GetBranchPolylines(out var branches, out var startArcs);
                var radii = new List<float>(branches.Count);
                for (int i = 0; i < branches.Count; i++) radii.Add(1f);
                Tubes.SetBranches(branches, radii, startArcs);
                _ = maxArc;
            }

            // ④ 表面上色辅助：从接触点 P 起微发光收紧（不再是主包裹，纯辅助）。
            target.BeginWrapShader(contact);

            // 单一前沿推进：蜂群 Progress 同时驱动方管 Growth，主干先到、子枝后冒、铺满表面。
            while (!Swarm.FormationComplete)
            {
                if (Tubes != null) Tubes.Growth = Swarm.Progress;
                yield return null;
            }
            if (Tubes != null) Tubes.Growth = 1f;

            // ⑤ 控制转移：玩家贴合到物体并驾驶它；蜂群转为贴附其(移动中的)表面。
            target.OnPossessed();
            AttachControlTo(target);
            // ⑥ 空中主干流空：bot 已转 WrapFollow 在身上，主干方管从根排空后清掉。
            if (Tubes != null) StartCoroutine(DrainTrunkRoutine(TrunkDrainTime));
            Current = State.Possess;
            _current = target;
            _co = null;
        }

        IEnumerator DetachRoutine(Possessable obj)
        {
            ClearHighlights();
            _candidates.Clear();

            Current = State.Extend;
            SetControlFrozen(true);

            // 物体旁地面汇聚点 G（落点也用它）。
            Vector3 ground = ComputeGroundPoint(obj);

            // 释放物体（解父、还原碰撞与外观），它停在原地。
            Release(obj);
            _current = null;

            // ① 预解算「收束树」：贴地主干 G→(贴地爬)→物体接触点 P，再沿物体表面铺到叶 →
            //    **反向**播放即「从表面分股贴面收回、贴地爬回 G」，全程不离面。
            Vector3[] leaves = obj.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);
            var projector = SurfaceSubdiv > 0 ? obj.GetSurfaceProjector() : null;
            // 物体接触点 P（贴面收束树的 root 邻接段终点）；求不到则退化用 G 直连。
            Vector3[] trunkPath = ResolveFootAndContact(obj, out Vector3 dFoot, out Vector3 dContact)
                ? BuildGroundCrawl(ground, dFoot, dContact)
                : new[] { ground };
            var tree = NanobotTree.Build(start: trunkPath[0], leafPoints: leaves,
                surfaceCenter: obj.Bounds.center,
                airBranchDepth: 0, outwardLift: 0f,
                surfaceSubdiv: SurfaceSubdiv, projectToSurface: projector,
                trunkPath: trunkPath);
            _tree = tree; // 供 Gizmos

            // ② 蜂群反向收束：bot 从各自表面叶子点沿树路径反向收缩 → 分股贴面蔓延汇到 G。
            Swarm.SetFormation(new TreeBranchFormation(tree, Trail, BranchThickness,
                JitterAmp, JitterScale, FlowSpeed, reverse: true), PathSpeed);

            // ③ 方管同步：前沿 Growth 从 1→0（末梢=表面段先收、G-trunk 最后）跟随收束。
            if (Tubes != null)
            {
                tree.GetBranchPolylines(out var branches, out var startArcs);
                var radii = new List<float>(branches.Count);
                for (int i = 0; i < branches.Count; i++) radii.Add(1f);
                Tubes.SetBranches(branches, radii, startArcs);
                Tubes.Drain = 0f;
                Tubes.Growth = 1f;
            }

            // reverse 下 Progress=1 即全员回到 G。方管 Growth = 1-Progress 同步退潮。
            while (!Swarm.FormationComplete)
            {
                if (Tubes != null) Tubes.Growth = 1f - Swarm.Progress;
                yield return null;
            }
            if (Tubes != null) Tubes.Clear();

            // ④ 落地成团：bot 已在 G，转常驻团；控制体落到 G 旁地面，回游荡（不可跳）。
            Swarm.SetFormation(new BlobFormation(() => ground), BlobSpeed);
            MovePlayerTo(ground + Vector3.up * PlayerGroundOffset);
            _co = null;
            EnterWander();
        }

        // 把玩家控制贴合到物体：物体 parent 到玩家(随驾驶移动)、关物体碰撞、玩家安全落位、
        // 蜂群转 WrapFollow。贴附点用**蔓延末态的树叶子**(bot 蔓延时正落在这些点) → 附身瞬间
        // 零跳变、流体无缝衔接到身上；反变换到物体局部 → 随驾驶移动整体跟随。
        void AttachControlTo(Possessable target)
        {
            // ① 先接管层级与碰撞（顺序确定，避免坐标系歧义）。
            target.transform.SetParent(Player, true);
            target.SetCollidersEnabled(false);
            if (PlayerVisual != null) PlayerVisual.gameObject.SetActive(false);

            // ② 玩家安全落位：放到物体包围盒中心，但确保玩家胶囊底不嵌入地面/残留几何，
            //    避免解冻刚体时被物理去穿插弹飞（带飞相机 → bot「闪掉」的根因）。
            Bounds b = target.Bounds;
            Vector3 safePos = new Vector3(b.center.x, b.max.y + PlayerGroundOffset, b.center.z);
            MovePlayerTo(safePos);

            // ③ 贴附点：优先蔓延末态的树叶子(无缝)；空/非法(NaN)兜底新采样；再兜底物体中心。
            Vector3[] worldSamples = (_tree != null && _tree.LeafCount > 0 && IsFinite(_tree.Leaves[0]))
                ? _tree.Leaves
                : target.GetSurfaceSamples(Mathf.Max(1, WrapSampleCount));
            if (worldSamples == null || worldSamples.Length == 0)
                worldSamples = new[] { b.center };

            var local = new Vector3[worldSamples.Length];
            for (int i = 0; i < worldSamples.Length; i++)
                local[i] = target.transform.InverseTransformPoint(worldSamples[i]);
            Swarm.SetFormation(new WrapFollowFormation(target.transform, local), 1f);

            // ④ 解冻并切可跳 profile；清零速度防解冻瞬间残余冲量弹飞。
            SetControlFrozen(false);
            if (_playerRb != null && !_playerRb.isKinematic)
            {
                _playerRb.linearVelocity = Vector3.zero;
                _playerRb.angularVelocity = Vector3.zero;
            }
            _motor?.ApplyProfile(PossessProfile); // 可跳
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
              || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // 附身后主干从根流空：方管 Drain 0→1(离根近的 trunk 先被吃掉)，收净后清管。
        // bot 流体此时已转 WrapFollow 贴在物体上 → 空中主干自然流走、只剩身上流体。
        IEnumerator DrainTrunkRoutine(float duration)
        {
            if (Tubes == null) yield break;
            float d = 0f;
            float inv = 1f / Mathf.Max(0.01f, duration);
            while (d < 1f)
            {
                d = Mathf.Clamp01(d + inv * Time.deltaTime);
                Tubes.Drain = d;
                yield return null;
            }
            Tubes.Clear();
        }

        // 释放物体：解除 parent、恢复碰撞、还原包裹外观、取消高亮。
        void Release(Possessable p)
        {
            if (p == null) return;
            if (p.transform.parent == Player) p.transform.SetParent(null, true);
            p.SetCollidersEnabled(true);
            p.ResetWrap();
            p.Highlighted = false;
        }

        // 延伸过程冻结控制体：停 PlayerMotor 并设 kinematic（不受重力/输入），过渡结束再恢复。
        void SetControlFrozen(bool frozen)
        {
            if (_motor != null) _motor.enabled = !frozen;
            if (_playerRb != null)
            {
                if (frozen) _playerRb.linearVelocity = Vector3.zero;
                _playerRb.isKinematic = frozen;
            }
        }

        void MovePlayerTo(Vector3 pos)
        {
            if (Player != null) Player.position = pos;
            if (_playerRb != null)
            {
                _playerRb.position = pos;
                if (!_playerRb.isKinematic) _playerRb.linearVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 贴地主干折线：从蜂群质心 a 到 foot(物体正下方贴地点)按 GroundSamplesPerUnit 细分，
        /// 每个中间点朝下 raycast 贴 GroundMask（+离地 GroundClearance），失败退化线性插值；
        /// 末点接 contact(P=物体最低表面点)。→ bot 沿地面爬到 P，全程不离地。
        /// </summary>
        Vector3[] BuildGroundCrawl(Vector3 a, Vector3 foot, Vector3 contact)
        {
            // 起点也贴地（蜂群可能略悬空）。
            Vector3 start = SnapToGround(a, out Vector3 startOnGround) ? startOnGround : a;

            float horiz = Vector3.Distance(new Vector3(start.x, 0f, start.z),
                                           new Vector3(foot.x, 0f, foot.z));
            int n = Mathf.Max(1, Mathf.CeilToInt(horiz * Mathf.Max(0.1f, GroundSamplesPerUnit)));

            var pts = new List<Vector3>(n + 2) { start };
            for (int k = 1; k <= n; k++)
            {
                float f = k / (float)n;
                Vector3 lerp = Vector3.Lerp(start, foot, f);
                pts.Add(SnapToGround(lerp, out Vector3 onGround) ? onGround : lerp);
            }
            // 显式收口到 foot(贴地)，再接 P(物体表面接触点)。
            pts[pts.Count - 1] = SnapToGround(foot, out Vector3 footG) ? footG : foot;
            pts.Add(contact);
            return pts.ToArray();
        }

        // 把世界点朝下 raycast 贴到 GroundMask（+离地高度）。命中返回 true。
        bool SnapToGround(Vector3 world, out Vector3 onGround)
        {
            Vector3 origin = new Vector3(world.x, world.y + 20f, world.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f,
                    GroundMask, QueryTriggerInteraction.Ignore))
            {
                onGround = hit.point + Vector3.up * GroundClearance;
                return true;
            }
            onGround = world;
            return false;
        }

        // 脱离聚合的地面点：物体周围随机水平偏移朝下命中地面；兜底物体正下方/原地。
        Vector3 ComputeGroundPoint(Possessable obj)
        {
            Bounds b = obj.Bounds;
            Vector3 c = b.center;
            float ang = Random.value * Mathf.PI * 2f;
            Vector3 probe = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * DetachGroundRadius;

            Vector3 origin = new Vector3(probe.x, b.max.y + 5f, probe.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f,
                    GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point;

            origin = new Vector3(c.x, b.max.y + 5f, c.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit2, 200f,
                    GroundMask, QueryTriggerInteraction.Ignore))
                return hit2.point;

            return new Vector3(c.x, 0f, c.z);
        }

        /// <summary>
        /// 求落点 F 与接触点 P（= 包裹入射点）。
        /// F = 目标包围盒中心朝下命中地面；P = 从 F 朝上命中目标表面（最低点）。
        /// 任一失败返回 false = 不可附身。
        /// </summary>
        bool ResolveFootAndContact(Possessable target, out Vector3 foot, out Vector3 contact)
        {
            foot = contact = Vector3.zero;
            Bounds b = target.Bounds;
            Vector3 center = b.center;

            // F：从中心上方一点朝下打到地面。
            Vector3 downOrigin = new Vector3(center.x, b.max.y + 0.5f, center.z);
            float downDist = (b.max.y + 0.5f) - (center.y - b.extents.y - 50f);
            if (!Physics.Raycast(downOrigin, Vector3.down, out RaycastHit groundHit,
                    downDist + 100f, GroundMask, QueryTriggerInteraction.Ignore))
                return false;
            foot = groundHit.point;

            // P：从 F 略低处朝上打到目标自身表面（最低可见表面点）。
            Vector3 upOrigin = foot + Vector3.down * 0.05f;
            if (!Physics.Raycast(upOrigin, Vector3.up, out RaycastHit surfHit,
                    b.size.y + 1f, PossessableMask, QueryTriggerInteraction.Ignore))
                return false;
            // 命中的必须是目标本身（不是别的可附身物挡在前面）。
            if (surfHit.collider.GetComponentInParent<Possessable>() != target)
                return false;
            contact = surfHit.point;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            if (Player != null)
            {
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.25f);
                Gizmos.DrawWireSphere(Player.position, ScanRadius);
            }

            // 预解算蔓延树：分叉线段 + 叶子末梢点（Play 模式选中本物体可见，便于验收
            // 均匀覆盖与不穿插）。
            if (_tree != null && _tree.LeafCount > 0)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.8f);
                foreach (var e in _tree.Edges) Gizmos.DrawLine(e.a, e.b);
                Gizmos.color = new Color(1f, 0.7f, 0.2f, 1f);
                foreach (var leaf in _tree.Leaves) Gizmos.DrawSphere(leaf, 0.06f);
            }
        }
    }
}
