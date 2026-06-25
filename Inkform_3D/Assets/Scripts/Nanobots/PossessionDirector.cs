using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Inkform.Gameplay;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 编排层：把扫描→选定→蔓延立起(L-path)→包裹→附身串成一条流水线。
    /// 身体层(NanobotSwarm)和目标层(Possessable)互不知道对方，全靠这里编排。
    ///
    /// 几何即谜题规则：ResolveFootAndContact 的任一 raycast 失败 = 不可附身
    /// （悬空/底下没地/隔玻璃自动被排除，不另写可附身判定）。
    /// </summary>
    public class PossessionDirector : MonoBehaviour
    {
        public enum State { Idle, Scanning, Moving, Wrapping, Possessed }

        [Header("引用")]
        public NanobotSwarm Swarm;
        public Transform Player;
        [Tooltip("可选：监听 E=扫描 / 鼠标左键=确认。留空则只能脚本调用。")]
        public InputReader Input;

        [Header("扫描")]
        [Tooltip("以玩家为中心的扫描半径。")]
        public float ScanRadius = 7f;
        [Tooltip("可附身物体所在层。")]
        public LayerMask PossessableMask;
        [Tooltip("地面层（求目标正下方落点 F 用）。")]
        public LayerMask GroundMask;

        [Header("流动手感")]
        public float BlobSpeed = 1f;
        [Tooltip("树状生长的推进速度（小=慢、更 cinematic）。")]
        public float PathSpeed = 0.35f;
        [Tooltip("生长前沿到达此进度时开始包裹上色（约等于空中段走完、首次触面）。")]
        [Range(0f, 1f)] public float WrapTriggerProgress = 0.45f;

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

        public State Current { get; private set; } = State.Idle;

        readonly List<Possessable> _candidates = new();
        int _selected;
        Coroutine _possessCo;
        NanobotTree _tree; // 当前预解算的生长树（供 Gizmos 可视化）

        void Start()
        {
            EnterIdle();
        }

        void OnEnable()
        {
            if (Input != null)
            {
                Input.InteractPressed += OnScanPressed;
                Input.UsePressed += OnConfirmPressed;
            }
        }

        void OnDisable()
        {
            if (Input != null)
            {
                Input.InteractPressed -= OnScanPressed;
                Input.UsePressed -= OnConfirmPressed;
            }
        }

        // ── 输入 ──
        // E：未扫描时开扫；已扫描时循环切候选。鼠标左键：确认附身选中目标。
        void OnScanPressed()
        {
            if (Current == State.Idle) BeginScan();
            else if (Current == State.Scanning) CycleCandidate();
        }

        void OnConfirmPressed()
        {
            if (Current == State.Scanning && _candidates.Count > 0)
                Possess(_candidates[_selected]);
        }

        // ── 状态 ──

        void EnterIdle()
        {
            ClearHighlights();
            Current = State.Idle;
            if (Swarm != null && Player != null)
                Swarm.SetFormation(new BlobFormation(() => Player.position), BlobSpeed);
        }

        /// <summary>开扫：以玩家为中心找范围内可附身物体并高亮。</summary>
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
                if (p != null && !_candidates.Contains(p)) _candidates.Add(p);
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
            Current = State.Scanning;
            RefreshHighlight();
            Debug.Log($"[PossessionDirector] 扫描到 {_candidates.Count} 个候选。E 切换，左键确认。");
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

        /// <summary>对选定目标启动附身流程。几何够不到则中止回 idle。</summary>
        public void Possess(Possessable target)
        {
            if (target == null) { EnterIdle(); return; }
            if (!ResolveFootAndContact(target, out _, out Vector3 contact))
            {
                Debug.Log($"[PossessionDirector] {target.name} 不可附身（悬空/底下没地/被遮挡）。");
                EnterIdle();
                return;
            }
            if (_possessCo != null) StopCoroutine(_possessCo);
            _possessCo = StartCoroutine(PossessRoutine(target, contact));
        }

        IEnumerator PossessRoutine(Possessable target, Vector3 contact)
        {
            ClearHighlights();
            target.Highlighted = true; // 选中目标保持高亮直到包裹

            // ① 触手伸出前一次性预解算整棵生长树：A → 空中树状分叉 → 表面均匀叶子末梢。
            //    每条分支各奔一个不同的表面点；树只发散不汇聚（分离后不回聚）。
            Current = State.Moving;
            Vector3 a = Swarm.Centroid;
            Vector3[] leaves = target.GetEvenSurfaceSamples(Mathf.Max(1, LeafCount), out _);
            var projector = SurfaceSubdiv > 0 ? target.GetSurfaceProjector() : null;
            _tree = NanobotTree.Build(a, leaves, target.Bounds.center,
                AirBranchDepth, OutwardLift, SurfaceSubdiv, projector);

            Swarm.SetFormation(new TreeBranchFormation(_tree, Trail, BranchThickness,
                JitterAmp, JitterScale, FlowSpeed), PathSpeed);

            // ② 生长前沿越过空中段、首次触面时开始包裹上色（shader 距离场扫满表面）。
            bool wrapStarted = false;
            while (!Swarm.FormationComplete)
            {
                if (!wrapStarted && Swarm.Progress >= WrapTriggerProgress)
                {
                    Current = State.Wrapping;
                    target.BeginWrapShader(contact);
                    wrapStarted = true;
                }
                yield return null;
            }
            if (!wrapStarted) { Current = State.Wrapping; target.BeginWrapShader(contact); }
            yield return new WaitForSeconds(target.WrapDuration); // 等 shader 扫满

            // ③ 附身生效。
            Current = State.Possessed;
            target.OnPossessed();
            _possessCo = null;
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
