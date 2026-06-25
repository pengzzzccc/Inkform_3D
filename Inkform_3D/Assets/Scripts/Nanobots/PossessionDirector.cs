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
        [Tooltip("蔓延+立起的推进速度（小=慢、更 cinematic）。")]
        public float PathSpeed = 0.35f;
        public float WrapFormationSpeed = 1.2f;
        [Tooltip("到达 F（目标正下方）后的积聚停顿，蓄势用。")]
        public float FootHoldTime = 0.25f;

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

        public State Current { get; private set; } = State.Idle;

        readonly List<Possessable> _candidates = new();
        int _selected;
        Coroutine _possessCo;

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
            if (!ResolveFootAndContact(target, out Vector3 foot, out Vector3 contact))
            {
                Debug.Log($"[PossessionDirector] {target.name} 不可附身（悬空/底下没地/被遮挡）。");
                EnterIdle();
                return;
            }
            if (_possessCo != null) StopCoroutine(_possessCo);
            _possessCo = StartCoroutine(PossessRoutine(target, foot, contact));
        }

        IEnumerator PossessRoutine(Possessable target, Vector3 foot, Vector3 contact)
        {
            ClearHighlights();
            target.Highlighted = true; // 选中目标保持高亮直到包裹

            // ① 蔓延+立起：A→F→P 的 L 折线。蔓延段贴地起伏 + 分形涌动；
            //    F 处硬转向是特意的（蔓延→立起的相变）。AB 不走。
            Current = State.Moving;
            Vector3 a = Swarm.Centroid;
            var path = BuildCrawlPath(a, foot, contact);
            Swarm.SetFormation(new PathFlowFormation(path, Trail, BranchCount, BranchSpread,
                FractalAmp, FractalScale, FlowSpeed), PathSpeed);

            while (!Swarm.FormationComplete) yield return null;

            // F 处积聚停顿做蓄势（cinematic）。形态已 complete，bot 停在 P。
            if (FootHoldTime > 0f) yield return new WaitForSeconds(FootHoldTime);

            // ② 接触 P 即开始包裹：shader 距离场 + bot 散布到表面。
            Current = State.Wrapping;
            target.BeginWrapShader(contact);
            Swarm.SetFormation(new SurfaceWrapFormation(target.GetSurfaceSamples(Swarm.BotCount)),
                WrapFormationSpeed);

            while (!Swarm.FormationComplete) yield return null;
            yield return new WaitForSeconds(target.WrapDuration); // 等 shader 扫满

            // ③ 附身生效。
            Current = State.Possessed;
            target.OnPossessed();
            _possessCo = null;
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
