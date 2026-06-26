using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 渲染层(管网版)：把各支流中心线挤成方截面金属管，按生长进度 0→1 向前延伸，
    /// 管头发光做生长前沿 —— 「方管分形延伸」。替代点粒渲染。
    /// Director 喂中心线 + Growth；本类每帧只重建已生长的前段 mesh(预分配复用,无 GC)。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NanobotTubeRenderer : MonoBehaviour
    {
        [Header("外观")]
        public Material TubeMaterial;
        [Tooltip("方管截面边长(米)。")]
        public float TubeSize = 0.25f;
        [Tooltip("中心线每米采样环数(越大越平滑/越密)。分节需要足够密。")]
        public float RingsPerUnit = 12f;
        [Tooltip("管头(生长前沿)相对根部的粗细比。")]
        [Range(0.1f, 1f)] public float TipThickness = 0.5f;
        [Tooltip("中心线轻微分形扭动幅度(0=干净直管)。")]
        public float WobbleAmp = 0.05f;
        public float WobbleScale = 1.5f;

        [Header("硬表面分节")]
        [Tooltip("每米机械节数(台阶/节缝密度)。")]
        public float SegmentsPerUnit = 3f;
        [Tooltip("节缝处粗细跳变幅度(相对截面边长的比例)。")]
        [Range(0f, 0.6f)] public float SegmentRidge = 0.25f;
        [Tooltip("相邻节绕切线的拧接旋转角(度)。")]
        public float SegmentTwist = 22f;

        MeshFilter _mf;
        Mesh _mesh;
        readonly List<Vector3[]> _branches = new();
        readonly List<float> _branchRadius = new(); // 每条中心线的半径倍数(子触手更细)
        readonly List<bool> _branchGrows = new();   // true=随 Growth 生长; false=恒满(已长好的主管)
        readonly List<float> _branchStartArc = new(); // 该条根弧长偏移(离总根 A)；仅前沿模式用
        readonly List<float> _branchLen = new();      // 该条自身总弧长(缓存)
        bool _frontMode;        // true=深度推进生长前沿(树); false=每条按自身比例生长(旧)
        float _maxTotalArc;     // 前沿满量程 = max(startArc + len)

        // 预分配缓冲(随容量增长复用)。
        readonly List<Vector3> _verts = new();
        readonly List<Vector3> _normals = new();
        readonly List<Color> _colors = new();
        readonly List<Vector2> _uvs = new();
        readonly List<Vector2> _uv2 = new();  // x=节内归一相位 segPhase, y=节索引奇偶
        readonly List<int> _tris = new();

        float _growth;
        public float Growth
        {
            get => _growth;
            set => _growth = Mathf.Clamp01(value);
        }

        float _drain;
        /// <summary>
        /// 从根排空进度（仅前沿模式有意义）：0=整条满；1=整条空。
        /// drainFront = Drain·maxTotalArc，每条只画 [drainFront, startArc+len] 这段 →
        /// 离根近的 trunk 段先被吃掉、surface 段最后 → 「流体从根往梢流走」。
        /// </summary>
        public float Drain
        {
            get => _drain;
            set => _drain = Mathf.Clamp01(value);
        }

        void Awake()
        {
            // mesh 顶点是世界空间 → 强制本物体单位变换，避免父级位置二次偏移。
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            _mf = GetComponent<MeshFilter>();
            _mesh = new Mesh { name = "NanobotTubes" };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
            if (TubeMaterial != null) GetComponent<MeshRenderer>().sharedMaterial = TubeMaterial;
        }

        /// <summary>设置当前管网各支流中心线(世界点序列)。全部用统一管粗、随 Growth 生长。</summary>
        public void SetBranches(IReadOnlyList<Vector3[]> branchCenterlines)
            => SetBranches(branchCenterlines, null, (IReadOnlyList<bool>)null);

        /// <summary>设置中心线 + 每条半径倍数(子触手传更小,如 0.35)。全部随 Growth 生长。</summary>
        public void SetBranches(IReadOnlyList<Vector3[]> branchCenterlines, IReadOnlyList<float> radiusMul)
            => SetBranches(branchCenterlines, radiusMul, (IReadOnlyList<bool>)null);

        /// <summary>
        /// 设置中心线 + 半径倍数 + 是否随 Growth 生长。
        /// grows[i]=false 的条恒按满长渲染(用于已长好的主管,新条生长时它不重新长)。
        /// radiusMul/grows 为 null 或长度不足时分别用 1.0 / true。
        /// </summary>
        public void SetBranches(IReadOnlyList<Vector3[]> branchCenterlines,
            IReadOnlyList<float> radiusMul, IReadOnlyList<bool> grows)
        {
            _branches.Clear();
            _branchRadius.Clear();
            _branchGrows.Clear();
            _branchStartArc.Clear();
            _branchLen.Clear();
            _frontMode = false;
            if (branchCenterlines != null)
                for (int i = 0; i < branchCenterlines.Count; i++)
                {
                    var b = branchCenterlines[i];
                    if (b == null || b.Length < 2) continue;
                    _branches.Add(b);
                    _branchRadius.Add(radiusMul != null && i < radiusMul.Count ? radiusMul[i] : 1f);
                    _branchGrows.Add(grows == null || i >= grows.Count || grows[i]);
                }
        }

        /// <summary>
        /// 树状分支专用：每条带**根弧长偏移** startArc（离总根 A 的累计弧长）。
        /// Growth 此时是**全局生长前沿**：front = Growth·maxTotalArc，每条只画
        /// front-startArc 这一段（front≤startArc 不长、超过则截到自身长度）→
        /// 主干先长、子枝按深度依次冒头，像一棵真的树在生长。
        /// </summary>
        public void SetBranches(IReadOnlyList<Vector3[]> branchCenterlines,
            IReadOnlyList<float> radiusMul, IReadOnlyList<float> startArcs)
        {
            _branches.Clear();
            _branchRadius.Clear();
            _branchGrows.Clear();
            _branchStartArc.Clear();
            _branchLen.Clear();
            _frontMode = true;
            _maxTotalArc = 1e-4f;
            if (branchCenterlines != null)
                for (int i = 0; i < branchCenterlines.Count; i++)
                {
                    var b = branchCenterlines[i];
                    if (b == null || b.Length < 2) continue;
                    float len = 0f;
                    for (int k = 1; k < b.Length; k++) len += Vector3.Distance(b[k - 1], b[k]);
                    float sArc = startArcs != null && i < startArcs.Count ? startArcs[i] : 0f;
                    _branches.Add(b);
                    _branchRadius.Add(radiusMul != null && i < radiusMul.Count ? radiusMul[i] : 1f);
                    _branchGrows.Add(true);
                    _branchStartArc.Add(sArc);
                    _branchLen.Add(len);
                    _maxTotalArc = Mathf.Max(_maxTotalArc, sArc + len);
                }
        }

        /// <summary>清空管网(idle/结束)。</summary>
        public void Clear()
        {
            _branches.Clear();
            _branchRadius.Clear();
            _branchGrows.Clear();
            _branchStartArc.Clear();
            _branchLen.Clear();
            _frontMode = false;
            _growth = 0f;
            _drain = 0f;
            if (_mesh != null) _mesh.Clear();
        }

        void LateUpdate()
        {
            if (_branches.Count == 0) { if (_mesh.vertexCount > 0) _mesh.Clear(); return; }

            _verts.Clear(); _normals.Clear(); _colors.Clear(); _uvs.Clear(); _uv2.Clear(); _tris.Clear();

            // 前沿模式(树)：全局前沿弧长，每条画 [fromFrac, toFrac] 这段(自身长度归一)。
            //   生长前沿 toFrac = (front-startArc)/len；根排空 fromFrac = (drainFront-startArc)/len。
            float front = _frontMode ? _growth * _maxTotalArc : 0f;
            float drainFront = _frontMode ? _drain * _maxTotalArc : 0f;

            for (int i = 0; i < _branches.Count; i++)
            {
                float fromFrac, toFrac;
                if (_frontMode)
                {
                    float len = _branchLen[i];
                    if (len <= 1e-4f) continue;
                    float sArc = _branchStartArc[i];
                    toFrac = Mathf.Clamp01((front - sArc) / len);
                    fromFrac = Mathf.Clamp01((drainFront - sArc) / len);
                }
                else
                {
                    fromFrac = 0f;
                    toFrac = _branchGrows[i] ? _growth : 1f;
                }
                if (toFrac - fromFrac <= 1e-4f) continue;
                AppendTube(_branches[i], fromFrac, toFrac, _branchRadius[i]);
            }

            _mesh.Clear();
            if (_verts.Count == 0) return;
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _uv2);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds(); // 动态 mesh：否则会被视锥剔除
        }

        /// <summary>
        /// 沿中心线挤一根方管，渲染弧长区间 [fromFrac, toFrac]·总长（自身长度归一）。
        /// fromFrac>0 = 从根排空(只画后段)；toFrac<1 = 未长满(只画前段)。
        /// 平行传输坐标系防扭转；管头顶点色 a=1 做生长前沿发光。
        /// </summary>
        void AppendTube(Vector3[] center, float fromFrac, float toFrac, float radiusMul)
        {
            // 累计弧长 + 总长。
            int cn = center.Length;
            float total = 0f;
            for (int k = 1; k < cn; k++) total += Vector3.Distance(center[k - 1], center[k]);
            if (total <= 1e-4f) return;

            float fromArc = total * Mathf.Clamp01(fromFrac);
            float toArc = total * Mathf.Clamp01(toFrac);
            float span = toArc - fromArc;
            if (span <= 1e-4f) return;

            // 沿弧长按 RingsPerUnit 采样环；环覆盖 [fromArc, toArc]。
            int ringCount = Mathf.Max(2, Mathf.CeilToInt(span * RingsPerUnit) + 1);
            int baseVert = _verts.Count;

            // 平行传输：初始 up 取与起点段切线尽量正交的轴。
            Vector3 prevTangent = SampleTangent(center, total, fromArc);
            Vector3 up = Mathf.Abs(Vector3.Dot(prevTangent, Vector3.up)) > 0.95f
                ? Vector3.right : Vector3.up;
            up = Vector3.Normalize(up - prevTangent * Vector3.Dot(up, prevTangent));

            for (int r = 0; r < ringCount; r++)
            {
                float arc = fromArc + span * (r / (float)(ringCount - 1));
                float s01total = arc / total;                 // 在整条中心线上的弧长归一
                Vector3 pos = SamplePoint(center, total, arc);
                Vector3 tangent = SampleTangent(center, total, arc);

                // 平行传输：把上一环 up 投影到当前法平面,避免截面突变扭转。
                up = up - tangent * Vector3.Dot(up, tangent);
                if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
                up.Normalize();
                Vector3 side = Vector3.Normalize(Vector3.Cross(tangent, up));

                // 分形扭动(沿法平面小幅偏移)。
                if (WobbleAmp > 0f)
                {
                    Vector3 w = Hash.Fbm3(pos * WobbleScale, 2);
                    pos += (side * w.x + up * w.y) * WobbleAmp;
                }

                // ── 硬表面分节 ──
                // ⚠ 旋转/缩放只作用于本环截面的局部基(rside/rup)，
                //   不改平行传输的 up/side，否则破坏后续环的连续性。
                float segF = arc * SegmentsPerUnit;
                int segIdx = Mathf.FloorToInt(segF);
                float segPhase = segF - segIdx;                   // 节内 [0,1)
                Vector3 rside = side, rup = up;
                // 每节绕切线拧接旋转(确定性,交替方向更"机械")。
                if (Mathf.Abs(SegmentTwist) > 0.01f)
                {
                    Quaternion roll = Quaternion.AngleAxis(
                        SegmentTwist * ((segIdx & 1) == 0 ? 1f : -1f), tangent);
                    rside = roll * rside;
                    rup = roll * rup;
                }
                // 节缝棱廓：节首/尾略粗,中间略细 → 一节节台阶。
                float ridge = 1f + SegmentRidge * (Mathf.Abs(segPhase - 0.5f) * 2f - 0.5f);

                // 管头变细：r 接近末端时收。
                float tipT = r / (float)(ringCount - 1);          // 0 根 → 1 头
                float thick = Mathf.Lerp(1f, TipThickness, tipT) * (TubeSize * 0.5f) * ridge * radiusMul;
                float tipFlag = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1f, tipT)); // 末端发光

                // 方截面 4 顶点(rside/rup 张成的正方形角)。
                Vector3 c0 = pos + (-rside - rup) * thick;
                Vector3 c1 = pos + (rside - rup) * thick;
                Vector3 c2 = pos + (rside + rup) * thick;
                Vector3 c3 = pos + (-rside + rup) * thick;
                Vector3[] ring = { c0, c1, c2, c3 };
                Vector3[] nrm = { (-rside - rup).normalized, (rside - rup).normalized,
                                  (rside + rup).normalized, (-rside + rup).normalized };

                float segParity = (segIdx & 1) == 0 ? 0f : 1f;
                var col = new Color(0, 0, 0, tipFlag);
                for (int q = 0; q < 4; q++)
                {
                    _verts.Add(ring[q]);
                    _normals.Add(nrm[q]);
                    _colors.Add(col);
                    _uv2.Add(new Vector2(segPhase, segParity));
                    _uvs.Add(new Vector2(q / 4f, s01total)); // UV.y = 沿管弧长(头尾渐变用)
                }
            }

            // 连接相邻环成方管侧壁(4 个面 × 2 三角形)。
            for (int r = 0; r < ringCount - 1; r++)
            {
                int a = baseVert + r * 4;
                int b = baseVert + (r + 1) * 4;
                for (int q = 0; q < 4; q++)
                {
                    int q2 = (q + 1) % 4;
                    // 四边形 (a+q, a+q2, b+q2, b+q)
                    _tris.Add(a + q); _tris.Add(b + q); _tris.Add(a + q2);
                    _tris.Add(a + q2); _tris.Add(b + q); _tris.Add(b + q2);
                }
            }
        }

        // ── 按弧长在折线上采样位置/切线 ──

        static Vector3 SamplePoint(Vector3[] pts, float total, float arc)
        {
            arc = Mathf.Clamp(arc, 0f, total);
            float acc = 0f;
            for (int k = 1; k < pts.Length; k++)
            {
                float seg = Vector3.Distance(pts[k - 1], pts[k]);
                if (arc <= acc + seg || k == pts.Length - 1)
                {
                    float f = seg > 1e-5f ? (arc - acc) / seg : 0f;
                    return Vector3.Lerp(pts[k - 1], pts[k], Mathf.Clamp01(f));
                }
                acc += seg;
            }
            return pts[pts.Length - 1];
        }

        static Vector3 SampleTangent(Vector3[] pts, float total, float arc)
        {
            float eps = Mathf.Max(0.01f, total * 0.01f);
            Vector3 ahead = SamplePoint(pts, total, Mathf.Min(total, arc + eps));
            Vector3 behind = SamplePoint(pts, total, Mathf.Max(0f, arc - eps));
            Vector3 d = ahead - behind;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }
    }
}
