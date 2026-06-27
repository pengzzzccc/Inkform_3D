using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inkform.Gameplay;
using Inkform.Data;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 编排层：nanobot(metacube) 生命周期状态机（按 Docs/nanobot-metacube.md）。
    /// 三态：聚合 Idle ↔ 蔓延 Spreading ↔ 附身 Possessed。
    ///   · 聚合：metacube 成贴地半球团跟随玩家（不可跳）；雷达持续检测可附身物、检测到闪一下。
    ///           子态 聚合/离散 由 AggregateForm.Dispersion 驱动（有选中→聚合收紧；无→离散松散）。
    ///   · 蔓延：预解算梳状触手（贴地主干 + 侧分支平行 + 爬面到随机表面点），cube 沿之生长。
    ///   · 附身：物体成化身随 WASD 驾驶（可跳）；cube 全覆盖贴附其表面跟随。
    /// 输入：1/2 选上/下一个候选；E 附身选中目标；Attack 脱离。
    ///
    /// 几何即规则：ResolveFootAndContact 任一 raycast 失败 = 不可附身。
    /// </summary>
    public class PossessionDirector : MonoBehaviour
    {
        public enum State { Idle, Spreading, Possessed }

        [Header("引用")]
        public MetacubeSystem Cubes;
        public Transform Player;
        [Tooltip("可选：监听 1/2 选择、E 附身/脱离确认、Attack 脱离。留空则只能脚本调用。")]
        public InputReader Input;

        [Header("雷达")]
        [Tooltip("以玩家为中心的检测半径。")]
        public float ScanRadius = 8f;
        [Tooltip("检测节流间隔（秒）。")]
        public float ScanInterval = 0.25f;
        public LayerMask PossessableMask;
        public LayerMask GroundMask;

        [Header("聚合半球")]
        public float BlobRadius = 1.1f;
        public float BlobSpeed = 1f;
        public float BlobFlow = 0.5f;
        [Tooltip("半球表面低频起伏幅度（呼吸感）。")]
        public float BlobWobble = 0.18f;
        [Tooltip("无选中目标时的离散程度（0..1）。有选中时收紧到 0。")]
        public float IdleDispersion = 0.55f;
        [Tooltip("无目标时离散度的低频呼吸幅度（叠加在 IdleDispersion 上）。")]
        public float IdleBreathe = 0.12f;
        [Tooltip("聚合/离散 平滑切换速率（每秒）。")]
        public float DispersionLerpRate = 2.5f;

        [Header("蔓延触手（梳状侧分支，全程贴面）")]
        public int LeafCount = 64;
        public int SurfaceSubdiv = 2;
        public float GroundSamplesPerUnit = 2f;
        public float GroundClearance = 0.12f;
        public float DepartMin = 0.2f;
        public float DepartMax = 0.8f;
        public float BranchThickness = 0.25f;
        public float Trail = 1.5f;
        public float SpreadSpeed = 0.5f;
        public int SourcePointCount = 4;

        [Header("附身/脱离")]
        public float DetachGroundRadius = 2.5f;
        public float PlayerGroundOffset = 1.1f;
        [Tooltip("全覆盖分布噪点缩放：大=斑块细碎、小=大片疏密。")]
        public float CoverNoiseScale = 0.6f;
        [Tooltip("全覆盖疏密对比：大=疏密反差强。")]
        public float CoverNoiseContrast = 1.5f;

        public State Current { get; private set; } = State.Idle;

        readonly List<Possessable> _candidates = new();
        readonly HashSet<Possessable> _detected = new(); // 已检测集合（只对新进的闪）
        Possessable _selectedTarget;                     // 当前选中（引用稳定，跨重排）
        Possessable _current;                            // 当前附身物体（常态 null）
        AggregateForm _aggregate;                        // 常态形态（用于实时调 Dispersion）
        SpreadPathSolver _solver;                        // 蔓延路径（供 Gizmos）
        Coroutine _co;                                   // 蔓延/脱离协程
        PlayerMotor _motor;
        Rigidbody _playerRb;
        Camera _cam;                                     // 脱离落点朝向用
        float _scanTimer;

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
            EnterIdle();
        }

        void OnEnable()
        {
            if (Input != null)
            {
                Input.SelectPrevPressed += OnSelectPrev;
                Input.SelectNextPressed += OnSelectNext;
                Input.InteractPressed += OnConfirm;
                Input.UsePressed += OnDetachKey;
            }
        }

        void OnDisable()
        {
            if (Input != null)
            {
                Input.SelectPrevPressed -= OnSelectPrev;
                Input.SelectNextPressed -= OnSelectNext;
                Input.InteractPressed -= OnConfirm;
                Input.UsePressed -= OnDetachKey;
            }
        }

        void Update()
        {
            // 蔓延中不扫描；常态/附身持续雷达检测。
            if (Current != State.Spreading)
            {
                _scanTimer -= Time.deltaTime;
                if (_scanTimer <= 0f) { _scanTimer = Mathf.Max(0.05f, ScanInterval); ScanTick(); }
            }

            // 子态 聚合/离散：有选中→收紧聚合(0)，无→离散(IdleDispersion + 低频呼吸)。
            if (_aggregate != null && Current == State.Idle)
            {
                float target = _selectedTarget != null
                    ? 0f
                    : Mathf.Clamp01(IdleDispersion + Mathf.Sin(Time.time * 0.6f) * IdleBreathe);
                _aggregate.Dispersion = Mathf.MoveTowards(_aggregate.Dispersion, target,
                    DispersionLerpRate * Time.deltaTime);
            }
        }

        // ── 雷达 ──
        // 检测范围内可附身物（排除当前附身物）；新进的闪一下；维护候选列表与选中项。
        void ScanTick()
        {
            if (Player == null) return;
            _candidates.Clear();

            var hits = Physics.OverlapSphere(Player.position, ScanRadius, PossessableMask,
                QueryTriggerInteraction.Ignore);
            var seen = new HashSet<Possessable>();
            foreach (var c in hits)
            {
                var p = c.GetComponentInParent<Possessable>();
                if (p == null || p == _current || !seen.Add(p)) continue;
                _candidates.Add(p);
                if (!_detected.Contains(p)) p.Flash(); // 新检测到 → 闪一下
            }
            // 更新已检测集合（移除离开范围的，便于再次进入时重新闪）。
            _detected.Clear();
            foreach (var p in _candidates) _detected.Add(p);

            _candidates.Sort((a, b) =>
                (a.transform.position - Player.position).sqrMagnitude
                .CompareTo((b.transform.position - Player.position).sqrMagnitude));

            // 选中项：常态默认选最近；附身态不自动选（默认无选中→E 即脱离）。
            if (_selectedTarget != null && !_candidates.Contains(_selectedTarget))
                _selectedTarget = null;
            if (_selectedTarget == null && Current == State.Idle && _candidates.Count > 0)
                _selectedTarget = _candidates[0];

            RefreshHighlight();
        }

        void RefreshHighlight()
        {
            foreach (var p in _candidates) if (p != null) p.Highlighted = (p == _selectedTarget);
        }

        // ── 选择（1/2）──
        void OnSelectPrev() => Cycle(-1);
        void OnSelectNext() => Cycle(+1);

        void Cycle(int dir)
        {
            if (Current == State.Spreading || _candidates.Count == 0) return;
            int idx = _selectedTarget != null ? _candidates.IndexOf(_selectedTarget) : -1;
            idx = (idx + dir + _candidates.Count) % _candidates.Count;
            _selectedTarget = _candidates[idx];
            RefreshHighlight();
        }

        // ── 确认（E）/ 脱离（Attack）──
        void OnConfirm()
        {
            if (Current == State.Spreading) return;
            if (_selectedTarget != null) Possess(_selectedTarget);
            else if (_current != null) Detach();
        }

        void OnDetachKey()
        {
            if (Current == State.Possessed) Detach();
        }

        // ── 状态 ──
        void EnterIdle()
        {
            _selectedTarget = null;
            _current = null;
            _candidates.Clear();
            _detected.Clear();
            Current = State.Idle;
            SetControlFrozen(false);
            _motor?.ApplyProfile(WanderProfile);
            if (Cubes != null && Player != null)
            {
                _aggregate = new AggregateForm(GroundUnderPlayer, BlobRadius, BlobWobble, BlobFlow)
                {
                    Dispersion = Mathf.Clamp01(IdleDispersion)
                };
                Cubes.SetForm(_aggregate, BlobSpeed);
            }
        }

        /// <summary>对选中目标启动附身（常态→case A / 附身→case B）。几何够不到则忽略。</summary>
        public void Possess(Possessable target)
        {
            if (target == null || target == _current) return;
            if (!ResolveFootAndContact(target, out _, out _))
            {
                Debug.Log($"[PossessionDirector] {target.name} 不可附身（悬空/底下没地/被遮挡）。");
                return;
            }
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(SpreadRoutine(target));
        }

        /// <summary>脱离：触手贴面收回物体旁一侧地面点、聚合成半球、回常态。</summary>
        public void Detach()
        {
            if (_current == null) return;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(DetachRoutine(_current));
        }

        IEnumerator SpreadRoutine(Possessable target)
        {
            foreach (var p in _candidates) if (p != null) p.Highlighted = false;
            _selectedTarget = null;
            target.Highlighted = true;

            Current = State.Spreading;
            SetControlFrozen(true);

            ResolveFootAndContact(target, out Vector3 foot, out _);
            Vector3[] leaves = target.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);

            var settings = new SpreadPathSolver.Settings
            {
                GroundSamplesPerUnit = GroundSamplesPerUnit,
                SurfaceSubdiv = SurfaceSubdiv,
                DepartMin = DepartMin,
                DepartMax = DepartMax,
                GroundSnap = GroundSnap,
                SurfaceProject = target.GetSurfaceProjector(),
                SourceProject = null,
            };

            Vector3[] sources;
            if (_current != null)
            {
                // case B：从当前物体表面取几个源点，贴源表面降到地面再贴地铺向新目标。
                sources = _current.GetSurfaceSamples(Mathf.Max(1, SourcePointCount));
                settings.SourceProject = _current.GetSurfaceProjector();
                Release(_current);
                _current = null;
            }
            else
            {
                // case A：源 = metacube 团中心（半球团），贴地伸出主干。
                sources = new[] { Cubes.Centroid };
            }

            _solver = SpreadPathSolver.Solve(sources, foot, target.Bounds.center, leaves, settings);
            Cubes.SetForm(new SpreadForm(_solver, Trail, BranchThickness), SpreadSpeed);

            while (!Cubes.FormComplete) yield return null;

            // 附身生效：控制转移 + cube 转贴面跟随。
            target.OnPossessed();
            AttachControlTo(target, _solver.Leaves);
            Current = State.Possessed;
            _current = target;
            _co = null;
        }

        IEnumerator DetachRoutine(Possessable obj)
        {
            foreach (var p in _candidates) if (p != null) p.Highlighted = false;
            _selectedTarget = null;

            Current = State.Spreading;
            SetControlFrozen(true);

            Vector3 ground = ComputeGroundPoint(obj);
            ResolveFootAndContact(obj, out Vector3 foot, out _);
            Vector3[] leaves = obj.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);
            var projector = obj.GetSurfaceProjector();

            Release(obj);
            _current = null;

            // 收束触手：source=地面点 G、leaves=物体表面点 → 正向是 G 爬上物体；反向播放=从表面收回 G。
            var settings = new SpreadPathSolver.Settings
            {
                GroundSamplesPerUnit = GroundSamplesPerUnit,
                SurfaceSubdiv = SurfaceSubdiv,
                DepartMin = DepartMin,
                DepartMax = DepartMax,
                GroundSnap = GroundSnap,
                SurfaceProject = projector,
                SourceProject = null,
            };
            _solver = SpreadPathSolver.Solve(new[] { ground }, foot, obj.Bounds.center, leaves, settings);
            Cubes.SetForm(new SpreadForm(_solver, Trail, BranchThickness, reverse: true), SpreadSpeed);

            while (!Cubes.FormComplete) yield return null;

            MovePlayerTo(ground + Vector3.up * PlayerGroundOffset);
            _co = null;
            EnterIdle();
        }

        // 控制转移：玩家胶囊先落位到物体处，再 parent 物体随驾驶移动、关其碰撞、cube 转贴面跟随。
        void AttachControlTo(Possessable target, Vector3[] worldLeaves)
        {
            // 世界包围盒：在任何重定位/挂父之前取，保证世界坐标准确。
            Bounds b = target.Bounds;

            // ① 先把控制胶囊移到物体处——此刻物体尚未挂到玩家下，不会被一起拖走（否则物体瞬移）。
            //    底对齐（b.min.y）而非顶：胶囊底≈物体底，驾驶时物体随胶囊立于地面、不上浮/下陷。
            //    PlayerGroundOffset(1.1) 略大于胶囊半高(1.0)，留 ~0.1 离地间隙防去穿插弹飞。
            MovePlayerTo(new Vector3(b.center.x, b.min.y + PlayerGroundOffset, b.center.z));

            // ② 再挂父（worldPositionStays=true）：物体保持原世界位，从此随玩家移动。
            target.transform.SetParent(Player, true);
            target.SetCollidersEnabled(false);

            // 贴附点：每 cube 一个噪点密度分布的独立表面点（分散、不叠团）。物体仍在原位，无缝衔接。
            int coverCount = Cubes != null ? Mathf.Max(1, Cubes.BotCount) : Mathf.Max(1, LeafCount);
            Vector3[] samples = target.GetNoiseSurfaceSamples(coverCount, CoverNoiseScale, CoverNoiseContrast);
            // 兜底：噪点采样异常时退回蔓延末态叶子 / 表面采样 / 中心。
            if (samples == null || samples.Length == 0 || !IsFinite(samples[0]))
                samples = (worldLeaves != null && worldLeaves.Length > 0 && IsFinite(worldLeaves[0]))
                    ? worldLeaves
                    : target.GetSurfaceSamples(Mathf.Max(1, LeafCount));
            if (samples == null || samples.Length == 0) samples = new[] { b.center };

            var local = new Vector3[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                local[i] = target.transform.InverseTransformPoint(samples[i]);
            Cubes.SetForm(new PossessForm(target.transform, local), 1f);

            SetControlFrozen(false);
            if (_playerRb != null && !_playerRb.isKinematic)
            {
                _playerRb.linearVelocity = Vector3.zero;
                _playerRb.angularVelocity = Vector3.zero;
            }
            _motor?.ApplyProfile(PossessProfile);
        }

        // 释放物体：解父、恢复碰撞、取消高亮。
        void Release(Possessable p)
        {
            if (p == null) return;
            if (p.transform.parent == Player) p.transform.SetParent(null, true);
            p.SetCollidersEnabled(true);
            p.Highlighted = false;
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

        // 脱离聚合地面点：物体**面向摄像机一侧**水平偏移朝下命中地面；兜底正下方/原地。
        Vector3 ComputeGroundPoint(Possessable obj)
        {
            Bounds b = obj.Bounds;
            Vector3 c = b.center;

            // 面向摄像机的水平方向（无摄像机则退回物体正下方，dir=0）。
            if (_cam == null) _cam = Camera.main;
            Vector3 dir = Vector3.zero;
            if (_cam != null)
            {
                dir = _cam.transform.position - c;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
            }
            Vector3 probe = c + dir * DetachGroundRadius;

            Vector3 origin = new Vector3(probe.x, b.max.y + 5f, probe.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            origin = new Vector3(c.x, b.max.y + 5f, c.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit2, 200f, GroundMask, QueryTriggerInteraction.Ignore))
                return hit2.point;
            return new Vector3(c.x, 0f, c.z);
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
              || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        void SetControlFrozen(bool frozen)
        {
            if (_motor != null) _motor.enabled = !frozen;
            if (_playerRb != null)
            {
                if (frozen) _playerRb.linearVelocity = Vector3.zero;
                _playerRb.isKinematic = frozen;
            }
        }

        // 玩家脚下地面点（半球底面落地用）。
        Vector3 GroundUnderPlayer()
        {
            if (Player == null) return Vector3.zero;
            Vector3 o = Player.position + Vector3.up * 0.5f;
            if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, 50f, GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return Player.position - Vector3.up;
        }

        // 任意点 → 贴地点（朝下 raycast + 离地高度）。miss 返回原点。供 SpreadPathSolver。
        Vector3 GroundSnap(Vector3 world)
        {
            Vector3 origin = new Vector3(world.x, world.y + 20f, world.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f, GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * GroundClearance;
            return world;
        }

        /// <summary>求落点 F 与接触点 P 作为可附身闸门。任一 raycast 失败 = 不可附身。</summary>
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
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.2f);
                Gizmos.DrawWireSphere(Player.position, ScanRadius);
            }
            if (_solver != null && _solver.LeafCount > 0)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.7f);
                foreach (var e in _solver.Edges) Gizmos.DrawLine(e.a, e.b);
                Gizmos.color = new Color(1f, 0.7f, 0.2f, 1f);
                foreach (var leaf in _solver.Leaves) Gizmos.DrawSphere(leaf, 0.05f);
            }
        }
    }
}
