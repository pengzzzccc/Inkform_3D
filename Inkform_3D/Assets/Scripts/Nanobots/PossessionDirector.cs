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
    ///   · 延伸：触手按预解算树生长（正向=奔向目标 / 反向=收敛回地面成团），玩家冻结。
    ///   · 附身：控制体贴合到物体，物体随 WASD 驾驶移动、相机跟随、可跳；蜂群贴附其表面。
    /// 输入：Shift=扫描/切候选；E=附身选中目标(有高亮) 或 脱离(附身中无高亮)。
    ///
    /// 几何即谜题规则：ResolveFootAndContact 的任一 raycast 失败 = 不可附身。
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

        [Header("扫描")]
        [Tooltip("以玩家为中心的扫描半径。")]
        public float ScanRadius = 7f;
        [Tooltip("可附身物体所在层。")]
        public LayerMask PossessableMask;
        [Tooltip("地面层（求目标正下方落点 / 脱离聚合的地面点用）。")]
        public LayerMask GroundMask;

        [Header("流动手感")]
        public float BlobSpeed = 1f;
        [Tooltip("树状生长的推进速度（小=慢、更 cinematic）。")]
        public float PathSpeed = 0.35f;
        [Tooltip("生长前沿到达此进度时开始包裹上色（约等于空中段走完、首次触面）。")]
        [Range(0f, 1f)] public float WrapTriggerProgress = 0.45f;
        [Tooltip("脱离时聚合地面点距物体的水平半径。")]
        public float DetachGroundRadius = 2.5f;
        [Tooltip("地面传送时控制体抬离地面高度（≈胶囊半高，防嵌地）。")]
        public float PlayerGroundOffset = 1.1f;

        [Header("树状分支（空中伸展 + 表面蔓延）")]
        [Tooltip("叶子末梢数：表面均匀终点个数（覆盖分辨率）。bot 按 i%LeafCount 分摊。")]
        public int LeafCount = 64;
        [Tooltip("前几级分叉在空中（>此深度的节点贴面蔓延）。")]
        public int AirBranchDepth = 3;
        [Tooltip("空中节点最大向外抬出高度（depth=0 最大，随深度衰减到 0）。")]
        public float OutwardLift = 2.5f;
        [Tooltip("表面段细分段数：>0 时把贴面段投影回最近表面点，避免切入凹陷(穿模)。")]
        public int SurfaceSubdiv = 2;
        [Tooltip("分支粗细：同叶多 bot 的横向簇宽。")]
        public float BranchThickness = 0.3f;
        [Tooltip("空中段分形抖动幅度（表面段自动衰减到几乎不抖）。")]
        public float JitterAmp = 0.3f;
        [Tooltip("分形抖动噪声频率。")]
        public float JitterScale = 0.6f;
        [Tooltip("抖动沿时间漂移速度，让虫群活起来。")]
        public float FlowSpeed = 0.4f;
        [Tooltip("队伍在路上拉散程度（弧长单位）。")]
        public float Trail = 2f;

        public State Current { get; private set; } = State.Wander;

        readonly List<Possessable> _candidates = new();
        int _selected;
        Coroutine _co;
        NanobotTree _tree;          // 当前预解算的生长树（供 Gizmos 可视化）
        Possessable _current;       // 当前附身物体（游荡时 null）
        PlayerMotor _motor;
        Rigidbody _playerRb;

        // 三套移动 profile：唯一差别是 CanJump（游荡不可跳、附身可跳）。
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
            if (!ResolveFootAndContact(target, out _, out Vector3 contact))
            {
                Debug.Log($"[PossessionDirector] {target.name} 不可附身（悬空/底下没地/被遮挡）。");
                return;
            }
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ExtendToPossessRoutine(target, contact));
        }

        /// <summary>脱离附身：分支向地面点聚合成团 → 回游荡。</summary>
        public void Detach()
        {
            if (_current == null) return;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(DetachRoutine(_current));
        }

        IEnumerator ExtendToPossessRoutine(Possessable target, Vector3 contact)
        {
            ClearHighlights();
            _candidates.Clear();
            target.Highlighted = true;

            Current = State.Extend;
            SetControlFrozen(true);

            // 附身→附身：先释放旧物体（留原地、还原碰撞与外观）。
            if (_current != null && _current != target) Release(_current);
            _current = null;

            // ① 伸出前一次性预解算整棵生长树：起点(蜂群质心) → 空中树状分叉 → 目标表面均匀末梢。
            Vector3 a = Swarm.Centroid;
            Vector3[] leaves = target.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);
            var projector = SurfaceSubdiv > 0 ? target.GetSurfaceProjector() : null;
            _tree = NanobotTree.Build(a, leaves, target.Bounds.center,
                AirBranchDepth, OutwardLift, SurfaceSubdiv, projector);

            Swarm.SetFormation(new TreeBranchFormation(_tree, Trail, BranchThickness,
                JitterAmp, JitterScale, FlowSpeed), PathSpeed);

            // ② 生长前沿首次触面时开始包裹上色。
            bool wrapStarted = false;
            while (!Swarm.FormationComplete)
            {
                if (!wrapStarted && Swarm.Progress >= WrapTriggerProgress)
                {
                    target.BeginWrapShader(contact);
                    wrapStarted = true;
                }
                yield return null;
            }
            if (!wrapStarted) target.BeginWrapShader(contact);
            yield return new WaitForSeconds(target.WrapDuration);

            // ③ 控制转移：玩家贴合到物体并驾驶它；蜂群转为贴附其(移动中的)表面。
            target.OnPossessed();
            AttachControlTo(target, leaves);
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

            Vector3 ground = ComputeGroundPoint(obj);

            // 释放物体（解父、还原碰撞与外观），它停在原地。
            Release(obj);
            _current = null;

            // 反向生长树：root=地面点 G，leaves=物体当前世界表面点 → 分支从表面收敛到 G 成团。
            Vector3[] leaves = obj.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);
            var projector = SurfaceSubdiv > 0 ? obj.GetSurfaceProjector() : null;
            _tree = NanobotTree.Build(ground, leaves, obj.Bounds.center,
                AirBranchDepth, OutwardLift, SurfaceSubdiv, projector);

            Swarm.SetFormation(new TreeBranchFormation(_tree, Trail, BranchThickness,
                JitterAmp, JitterScale, FlowSpeed, reverse: true), PathSpeed);

            while (!Swarm.FormationComplete) yield return null;

            // 控制体落到地面点，回游荡（不可跳）。
            MovePlayerTo(ground + Vector3.up * PlayerGroundOffset);
            _co = null;
            EnterWander();
        }

        // 把玩家控制贴合到物体：玩家传送到物体、物体 parent 到玩家(随驾驶移动)、关物体碰撞、
        // 蜂群转 WrapFollow（用延伸的同一批 leaves 反变换到物体局部，无缝衔接）。
        void AttachControlTo(Possessable target, Vector3[] worldLeaves)
        {
            MovePlayerTo(target.transform.position);
            target.transform.SetParent(Player, true);
            target.SetCollidersEnabled(false);
            if (PlayerVisual != null) PlayerVisual.gameObject.SetActive(false);

            var local = new Vector3[worldLeaves.Length];
            for (int i = 0; i < worldLeaves.Length; i++)
                local[i] = target.transform.InverseTransformPoint(worldLeaves[i]);
            Swarm.SetFormation(new WrapFollowFormation(target.transform, local), 1f);

            SetControlFrozen(false);
            _motor?.ApplyProfile(PossessProfile); // 可跳
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
        /// 求落点 F 与接触点 P（= 包裹入射点），作为可附身性闸门。
        /// F = 目标包围盒中心朝下命中地面；P = 从 F 朝上命中目标表面（最低点）。
        /// 任一失败返回 false = 不可附身。
        /// </summary>
        bool ResolveFootAndContact(Possessable target, out Vector3 foot, out Vector3 contact)
        {
            foot = contact = Vector3.zero;
            Bounds b = target.Bounds;
            Vector3 center = b.center;

            Vector3 downOrigin = new Vector3(center.x, b.max.y + 0.5f, center.z);
            float downDist = (b.max.y + 0.5f) - (center.y - b.extents.y - 50f);
            if (!Physics.Raycast(downOrigin, Vector3.down, out RaycastHit groundHit,
                    downDist + 100f, GroundMask, QueryTriggerInteraction.Ignore))
                return false;
            foot = groundHit.point;

            Vector3 upOrigin = foot + Vector3.down * 0.05f;
            if (!Physics.Raycast(upOrigin, Vector3.up, out RaycastHit surfHit,
                    b.size.y + 1f, PossessableMask, QueryTriggerInteraction.Ignore))
                return false;
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

            // 预解算生长树：分叉线段 + 叶子末梢点（Play 模式下选中本物体可见，便于验收）。
            if (_tree != null && _tree.LeafCount > 0)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.8f);
                foreach (var (ea, eb) in _tree.Edges) Gizmos.DrawLine(ea, eb);
                Gizmos.color = new Color(1f, 0.7f, 0.2f, 1f);
                foreach (var leaf in _tree.Leaves) Gizmos.DrawSphere(leaf, 0.06f);
            }
        }
    }
}
