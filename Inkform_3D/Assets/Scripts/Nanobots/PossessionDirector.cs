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
        [Tooltip("蔓延+立起的推进速度（小=慢、更 cinematic）。")]
        public float PathSpeed = 0.35f;
        public float WrapFormationSpeed = 1.2f;
        [Tooltip("【已弃用】原 F 处蓄势停顿。现接触即包裹，不再使用。")]
        public float FootHoldTime = 0f;

        [Header("附身/脱离")]
        [Tooltip("附身后蜂群贴附物体表面用的采样点数。")]
        public int WrapSampleCount = 64;
        [Tooltip("脱离时聚合地面点距物体的水平半径。")]
        public float DetachGroundRadius = 2.5f;
        [Tooltip("地面传送时控制体抬离地面高度（≈胶囊半高，防嵌地）。")]
        public float PlayerGroundOffset = 1.1f;

        [Header("蔓延轨迹（A→F 贴地 + 分形）")]
        [Tooltip("A→F 水平细分段数：越多越贴合地形起伏。")]
        public int GroundSamples = 10;
        [Tooltip("贴地离地高度，避免 bot 嵌进地面。")]
        public float GroundClearance = 0.15f;
        [Tooltip("支流数：行进时一股分成几股(1=不分叉)。")]
        public int BranchCount = 4;
        [Tooltip("支流最大横向张开间距（最外两股相距约 2 倍此值）。")]
        public float BranchSpread = 2.5f;
        [Tooltip("支流内分形细抖动幅度。")]
        public float FractalAmp = 0.35f;
        [Tooltip("分形噪声频率。")]
        public float FractalScale = 1.5f;
        [Tooltip("分形沿时间漂移速度，让虫群活起来。")]
        public float FlowSpeed = 0.4f;
        [Tooltip("队伍在路上拉散程度。")]
        public float Trail = 0.35f;
        [Tooltip("管中心线每米采样点数(越大管越平滑)。")]
        public float CenterlineSamplesPerUnit = 4f;

        [Header("束缚绑定（接触后）")]
        [Tooltip("管心离物体表面的高度。")]
        public float SurfaceOffset = 0.12f;
        [Tooltip("贴表面爬行/束缚带每米采样点数。")]
        public float SurfaceSamplesPerUnit = 6f;
        [Tooltip("束缚带条数（绕物体缠绕的环）。")]
        public int StrapCount = 4;
        [Tooltip("束缚带绕物体的圈数（>1 为螺旋）。")]
        public float StrapTurns = 1f;
        [Tooltip("子触手数量。")]
        public int SubTentacleCount = 30;
        [Tooltip("子触手长度（米）。")]
        public float SubTentacleLength = 0.5f;
        [Tooltip("子触手相对主管的粗细倍数。")]
        public float SubTentacleRadius = 0.35f;
        [Tooltip("爬行段推进速度。")]
        public float CrawlSpeed = 1.2f;
        [Tooltip("束缚段推进速度。")]
        public float BindSpeed = 1.2f;

        public State Current { get; private set; } = State.Wander;

        readonly List<Possessable> _candidates = new();
        int _selected;
        Coroutine _co;
        Possessable _current;       // 当前附身物体（游荡时 null）
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

            // 附身→附身：先释放旧物体（留原地、还原碰撞与外观）。
            if (_current != null && _current != target) Release(_current);
            _current = null;

            // ① 蔓延+立起：A→F→P 的 L 折线。蔓延段贴地起伏 + 分形涌动；
            //    F 处硬转向是特意的（蔓延→立起的相变）。AB 不走。
            Vector3 a = Swarm.Centroid;
            var path = BuildCrawlPath(a, foot, contact);
            Swarm.SetFormation(new PathFlowFormation(path, Trail, BranchCount, BranchSpread,
                FractalAmp, FractalScale, FlowSpeed), PathSpeed);

            // 方管：用与支流相同的几何生成中心线，随形态进度向前生长。
            if (Tubes != null) Tubes.SetBranches(BuildBranchCenterlines(path));

            while (!Swarm.FormationComplete)
            {
                if (Tubes != null) Tubes.Growth = Swarm.Progress;
                yield return null;
            }
            if (Tubes != null) Tubes.Growth = 1f; // 管头到达 P

            // ② 接触表面：选表面目标点,管头沿表面爬行过去（mesh 为主的束缚开始）。
            Vector3 wrapTarget = PickWrapTarget(target, contact);
            var crawlSurface = BuildSurfaceWalk(target, contact, wrapTarget);

            // 已长好的 A→F→P 主管(恒满,不重长) + 新的贴表面爬行段(从 P 生长到 wrapTarget)。
            var mainAFP = GetMainPathPoints(path);
            var moveBranches = new List<Vector3[]> { mainAFP, crawlSurface };
            var moveRadii = new List<float> { 1f, 1f };
            var moveGrows = new List<bool> { false, true };
            if (Tubes != null) yield return GrowTubes(moveBranches, moveRadii, moveGrows, CrawlSpeed);

            // ③ 束缚带 + 子触手生长（mesh 束缚绑定 + 周围微小子触手）。
            //    主管(A→F→P + 爬行段)标记为恒满,只让束缚带与子触手随 Growth 长出。
            var bindBranches = new List<Vector3[]> { mainAFP, crawlSurface };
            var bindRadii = new List<float> { 1f, 1f };
            var bindGrows = new List<bool> { false, false };
            var straps = BuildBindingStraps(target, wrapTarget);
            bindBranches.AddRange(straps);
            for (int i = 0; i < straps.Count; i++) { bindRadii.Add(1f); bindGrows.Add(true); }
            var subs = BuildSubTentacles(target, crawlSurface);
            bindBranches.AddRange(subs);
            for (int i = 0; i < subs.Count; i++) { bindRadii.Add(SubTentacleRadius); bindGrows.Add(true); }

            // shader 辅助：表面从 wrapTarget 起微发光收紧（不再是主包裹）。
            target.BeginWrapShader(wrapTarget);
            if (Tubes != null) yield return GrowTubes(bindBranches, bindRadii, bindGrows, BindSpeed);
            else yield return new WaitForSeconds(target.WrapDuration);

            // ④ 控制转移：玩家贴合到物体并驾驶它；蜂群转为贴附其(移动中的)表面。
            target.OnPossessed();
            AttachControlTo(target);
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

            // 蜂群从物体表面聚合回地面点成团；管网清掉。
            if (Tubes != null) Tubes.Clear();
            Swarm.SetFormation(new BlobFormation(() => ground), BlobSpeed);

            // 给聚合一点过渡时间（BlobFormation 常驻不会 Complete，靠 WrapDuration 兜一拍）。
            yield return new WaitForSeconds(obj.WrapDuration);

            // 控制体落到地面点，回游荡（不可跳）。
            MovePlayerTo(ground + Vector3.up * PlayerGroundOffset);
            _co = null;
            EnterWander();
        }

        // 把玩家控制贴合到物体：玩家传送到物体、物体 parent 到玩家(随驾驶移动)、关物体碰撞、
        // 蜂群转 WrapFollow（用目标表面采样点反变换到物体局部，随物体移动跟随）。
        void AttachControlTo(Possessable target)
        {
            MovePlayerTo(target.transform.position);
            target.transform.SetParent(Player, true);
            target.SetCollidersEnabled(false);
            if (PlayerVisual != null) PlayerVisual.gameObject.SetActive(false);

            var worldSamples = target.GetSurfaceSamples(Mathf.Max(1, WrapSampleCount));
            var local = new Vector3[worldSamples.Length];
            for (int i = 0; i < worldSamples.Length; i++)
                local[i] = target.transform.InverseTransformPoint(worldSamples[i]);
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

        /// <summary>把若干中心线喂给管渲染器,Growth 0→1 推进(speed=每秒进度)。全部生长。</summary>
        IEnumerator GrowTubes(List<Vector3[]> branches, List<float> radii, float speed)
            => GrowTubes(branches, radii, null, speed);

        /// <summary>同上,grows[i]=false 的条恒满(不随 Growth 重新长)。</summary>
        IEnumerator GrowTubes(List<Vector3[]> branches, List<float> radii, List<bool> grows, float speed)
        {
            Tubes.SetBranches(branches, radii, grows);
            float g = 0f;
            while (g < 1f)
            {
                g = Mathf.Clamp01(g + Mathf.Max(0.01f, speed) * Time.deltaTime);
                Tubes.Growth = g;
                yield return null;
            }
            Tubes.Growth = 1f;
        }

        /// <summary>
        /// 构造蔓延+立起路径：A → (贴地折线) → F → P。
        /// A→F 段水平细分，每个中间点朝下 raycast 贴到地面（+离地高度），
        /// 让路径顺着地形起伏；起点 A 用质心（保留从团里淌出的感觉），不强行贴地。
        /// F→P 竖直立起段保留（硬转向特意为之）。
        /// 接口预留：以后把这里的水平插值换成 NavMesh.CalculatePath 的 corner 序列即可，
        /// PathFlowFormation 一行不用改。
        /// </summary>
        PolylinePath BuildCrawlPath(Vector3 a, Vector3 foot, Vector3 contact)
        {
            int n = Mathf.Max(1, GroundSamples);
            var pts = new System.Collections.Generic.List<Vector3>(n + 2);
            pts.Add(a);

            // 在 A 与 F 之间按水平方向插点，每点贴地。k 从 1 起（A 已加）。
            for (int k = 1; k <= n; k++)
            {
                float f = k / (float)n;                 // (0,1]
                Vector3 lerp = Vector3.Lerp(a, foot, f); // 含线性 Y 作退化兜底
                // 从足够高处朝下打，避免起点落在地形之下漏检。
                Vector3 origin = new Vector3(lerp.x, Mathf.Max(a.y, foot.y) + 20f, lerp.z);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f,
                        GroundMask, QueryTriggerInteraction.Ignore))
                    pts.Add(hit.point + Vector3.up * GroundClearance);
                else
                    pts.Add(lerp); // 没命中地面：退化用线性插值点，不中断
            }

            // 末点确保是 F（贴地循环最后一点 f=1 已≈F，但显式收口更稳），再接 P。
            pts[pts.Count - 1] = foot;
            pts.Add(contact);
            return new PolylinePath(pts.ToArray());
        }

        /// <summary>
        /// 用与 PathFlowFormation 一致的支流数学，把主路径展开成 BranchCount 条中心线点序列，
        /// 供方管渲染。横向偏移 = side * lane * BranchSpread * sin(π·s)：A 与 P 处收束、中段张开。
        /// </summary>
        List<Vector3[]> BuildBranchCenterlines(PolylinePath path)
        {
            int samples = Mathf.Max(2, Mathf.CeilToInt(path.TotalLength * CenterlineSamplesPerUnit) + 1);
            int branches = Mathf.Max(1, BranchCount);
            var result = new List<Vector3[]>(branches);

            for (int b = 0; b < branches; b++)
            {
                float lane = branches > 1 ? (b / (float)(branches - 1) - 0.5f) * 2f : 0f; // [-1,1]
                var line = new Vector3[samples];
                for (int k = 0; k < samples; k++)
                {
                    float s = k / (float)(samples - 1);
                    Vector3 basePos = path.PointAt01(s);
                    if (branches > 1 && BranchSpread > 0f)
                    {
                        Vector3 tangent = path.TangentAt01(s);
                        Vector3 side = Vector3.Cross(tangent, Vector3.up);
                        if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                        side.Normalize();
                        float env = Mathf.Sin(Mathf.PI * s);
                        basePos += side * (lane * BranchSpread * env);
                    }
                    line[k] = basePos;
                }
                result.Add(line);
            }
            return result;
        }

        // ───────────────────────── 束缚绑定（接触后） ─────────────────────────

        /// <summary>主路径(A→F→P)的原始折线点。</summary>
        static Vector3[] GetMainPathPoints(PolylinePath path)
        {
            var pts = path.Points;
            var arr = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) arr[i] = pts[i];
            return arr;
        }

        /// <summary>把世界点朝物体中心 raycast 贴到目标表面,返回表面点+法线*SurfaceOffset。失败返回原点。</summary>
        bool SurfaceSnap(Possessable target, Vector3 worldPoint, out Vector3 onSurface)
        {
            Vector3 center = target.Bounds.center;
            Vector3 toCenter = center - worldPoint;
            float dist = toCenter.magnitude;
            onSurface = worldPoint;
            if (dist < 1e-4f) return false;
            // 从物体外侧朝中心打：起点放在该点沿"远离中心"方向稍外。
            Vector3 dir = toCenter / dist;
            Vector3 origin = worldPoint - dir * 0.5f; // 略微退到表面外
            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist + 1f,
                    PossessableMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Possessable>() == target)
            {
                onSurface = hit.point + hit.normal * SurfaceOffset;
                return true;
            }
            return false;
        }

        /// <summary>表面上确定性选一个"包裹目标点":取离接触点较远的一个表面采样点,凸显爬行距离。</summary>
        Vector3 PickWrapTarget(Possessable target, Vector3 contact)
        {
            var samples = target.GetSurfaceSamples(64);
            Vector3 best = contact;
            float bestD = -1f;
            foreach (var s in samples)
            {
                float d = (s - contact).sqrMagnitude;
                if (d > bestD) { bestD = d; best = s; }
            }
            // 贴表面 + 离面一点。
            return SurfaceSnap(target, best, out var onSurf) ? onSurf : best;
        }

        /// <summary>
        /// 贴表面爬行路径 from→to:沿直线分段,每个中间点朝物体中心 raycast 贴到表面。
        /// 失败的点退化为线性插值,不中断。
        /// </summary>
        Vector3[] BuildSurfaceWalk(Possessable target, Vector3 from, Vector3 to)
        {
            float len = Vector3.Distance(from, to);
            int n = Mathf.Max(2, Mathf.CeilToInt(len * SurfaceSamplesPerUnit) + 1);
            var line = new Vector3[n];
            line[0] = from;
            for (int k = 1; k < n; k++)
            {
                float f = k / (float)(n - 1);
                Vector3 lerp = Vector3.Lerp(from, to, f);
                line[k] = SurfaceSnap(target, lerp, out var onSurf) ? onSurf : lerp;
            }
            return line;
        }

        /// <summary>
        /// 束缚带:绕物体生成 StrapCount 条环形/螺旋中心线,每点贴表面 → 多条交叉缠绕,绳索捆绑感。
        /// 不同带绕不同轴 + 相位错开。
        /// </summary>
        List<Vector3[]> BuildBindingStraps(Possessable target, Vector3 around)
        {
            var result = new List<Vector3[]>();
            Bounds b = target.Bounds;
            Vector3 c = b.center;
            float radius = b.extents.magnitude; // 椭球退化半径
            int straps = Mathf.Max(1, StrapCount);
            int ptsPerTurn = Mathf.Max(8, Mathf.CeilToInt(radius * 2f * Mathf.PI * SurfaceSamplesPerUnit));
            int n = Mathf.Max(8, Mathf.CeilToInt(ptsPerTurn * Mathf.Max(1f, StrapTurns)));

            for (int sI = 0; sI < straps; sI++)
            {
                // 每条带的绕轴:在世界轴间插值 + 确定性扰动,带与带交叉。
                float aF = sI / (float)straps;
                Vector3 axis = Vector3.Slerp(Vector3.up, Vector3.right, aF);
                axis = Quaternion.AngleAxis(Hash.Unit(sI, 30) * 180f, Vector3.forward) * axis;
                axis.Normalize();
                Vector3 u = Vector3.Cross(axis, Vector3.up);
                if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(axis, Vector3.right);
                u.Normalize();
                Vector3 v = Vector3.Cross(axis, u);
                float phase = Hash.Unit(sI, 31) * Mathf.PI * 2f;

                var line = new Vector3[n];
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)(n - 1);
                    float ang = phase + t * Mathf.PI * 2f * Mathf.Max(1f, StrapTurns);
                    // 环上点(椭球面附近),再贴表面。
                    Vector3 ringPt = c + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * radius;
                    line[k] = SurfaceSnap(target, ringPt, out var onSurf) ? onSurf : ringPt;
                }
                result.Add(line);
            }
            return result;
        }

        /// <summary>
        /// 子触手:沿爬行/束缚路径确定性选点,每根生成一条短中心线(起点表面、向外+fBm 抖动)。
        /// </summary>
        List<Vector3[]> BuildSubTentacles(Possessable target, Vector3[] alongPath)
        {
            var result = new List<Vector3[]>();
            if (alongPath == null || alongPath.Length < 2) return result;
            Vector3 c = target.Bounds.center;
            int count = Mathf.Max(0, SubTentacleCount);

            for (int i = 0; i < count; i++)
            {
                // 沿爬行路径确定性取一个根点。
                float f = Hash.Unit(i, 40);
                int idx = Mathf.Clamp(Mathf.FloorToInt(f * (alongPath.Length - 1)), 0, alongPath.Length - 1);
                Vector3 root = alongPath[idx];

                // 方向:表面外法向(root 远离中心)+ fBm 抖动 → 向外摇曳。
                Vector3 outward = (root - c);
                if (outward.sqrMagnitude < 1e-4f) outward = Vector3.up;
                outward.Normalize();
                Vector3 wob = Hash.Fbm3(root * 2f + Vector3.one * i, 2);
                Vector3 dir = (outward + wob * 0.6f).normalized;

                float len = SubTentacleLength * (0.5f + Hash.Unit(i, 41));
                var line = new Vector3[3];
                line[0] = root;
                line[1] = root + dir * (len * 0.5f) + wob * (len * 0.15f);
                line[2] = root + dir * len + wob * (len * 0.25f);
                result.Add(line);
            }
            return result;
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
            if (Player == null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireSphere(Player.position, ScanRadius);
        }
    }
}
