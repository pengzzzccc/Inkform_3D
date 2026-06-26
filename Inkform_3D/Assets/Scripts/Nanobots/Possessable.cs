using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 目标层：一个可被附身的物体。向编排层提供几何/表面采样/包裹控制。
    /// 配套 NanobotWrap shader：被附身时从入射点 P 起把表面距离场 _Grow 0→1 扫满。
    /// 物体应放在 Possessable 层，且带 MeshFilter + Collider（求几何点用）。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class Possessable : MonoBehaviour
    {
        [Header("包裹")]
        [Tooltip("_Grow 从 0→1 的时长（秒）。")]
        public float WrapDuration = 1.1f;

        [Header("高亮（扫描命中时）")]
        public Color HighlightEmission = new Color(0.3f, 0.8f, 1f) * 1.5f;

        Renderer _rend;
        MeshFilter _meshFilter;
        Material _wrapMat;       // 实例化材质（避免 MaterialPropertyBlock 退出 SRP Batcher）
        Color _baseEmission;
        bool _hasEmission;
        Coroutine _wrapCo;

        public Bounds Bounds => _rend != null ? _rend.bounds : new Bounds(transform.position, Vector3.one);

        static readonly int IdEntry = Shader.PropertyToID("_EntryPoint");
        static readonly int IdMaxDist = Shader.PropertyToID("_MaxDist");
        static readonly int IdGrow = Shader.PropertyToID("_Grow");
        static readonly int IdEmission = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            _rend = GetComponent<Renderer>();
            _meshFilter = GetComponent<MeshFilter>();
            // 实例化材质，独占编辑（material 访问会自动实例化）。
            _wrapMat = _rend.material;
            if (_wrapMat.HasProperty(IdGrow)) _wrapMat.SetFloat(IdGrow, 0f);
            _hasEmission = _wrapMat.HasProperty(IdEmission);
            if (_hasEmission) _baseEmission = _wrapMat.GetColor(IdEmission);
        }

        /// <summary>扫描高亮开关。</summary>
        public bool Highlighted
        {
            set
            {
                if (!_hasEmission) return;
                _wrapMat.EnableKeyword("_EMISSION");
                _wrapMat.SetColor(IdEmission, value ? HighlightEmission : _baseEmission);
            }
        }

        /// <summary>
        /// 表面采样点（世界空间），抽稀到 ~count。优先用 mesh 顶点；
        /// 无 mesh 时退化到 bounds 表面随机点。供 SurfaceWrapFormation 用。
        /// </summary>
        public Vector3[] GetSurfaceSamples(int count)
        {
            var mesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
            if (mesh == null) return FallbackSamples(count);

            var verts = mesh.vertices;
            var tris = mesh.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0 || verts.Length == 0) return FallbackSamples(count);

            var l2w = transform.localToWorldMatrix;

            // 三角形世界顶点 + 面积前缀和（面积加权 → 大三角形分到更多采样点）。
            var v0 = new Vector3[triCount];
            var e1 = new Vector3[triCount]; // v1 - v0
            var e2 = new Vector3[triCount]; // v2 - v0
            var cumArea = new float[triCount];
            float total = 0f;
            for (int tIdx = 0; tIdx < triCount; tIdx++)
            {
                Vector3 a = l2w.MultiplyPoint3x4(verts[tris[tIdx * 3 + 0]]);
                Vector3 b = l2w.MultiplyPoint3x4(verts[tris[tIdx * 3 + 1]]);
                Vector3 c = l2w.MultiplyPoint3x4(verts[tris[tIdx * 3 + 2]]);
                v0[tIdx] = a; e1[tIdx] = b - a; e2[tIdx] = c - a;
                total += 0.5f * Vector3.Cross(e1[tIdx], e2[tIdx]).magnitude;
                cumArea[tIdx] = total;
            }
            if (total <= 1e-6f) return FallbackSamples(count);

            // 每个采样点：确定性挑三角形(面积加权) + 重心坐标取点。
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float pick = Hash.Unit(i, 10) * total;
                int t = LowerBound(cumArea, pick);

                float u = Hash.Unit(i, 11);
                float v = Hash.Unit(i, 12);
                if (u + v > 1f) { u = 1f - u; v = 1f - v; } // 折回三角形内
                result[i] = v0[t] + e1[t] * u + e2[t] * v;
            }
            return result;
        }

        /// <summary>在升序前缀和数组里二分定位 value 落入的区间索引。</summary>
        static int LowerBound(float[] cum, float value)
        {
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        Vector3[] FallbackSamples(int count)
        {
            var b = Bounds;
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Hash.Direction(i);
                result[i] = b.center + Vector3.Scale(dir, b.extents);
            }
            return result;
        }

        /// <summary>
        /// 均匀表面采样：先取面积加权稠密候选池，再用最远点采样(farthest-point)下采样到 count，
        /// 得到类 Poisson 的均匀分布。同时输出每点所在三角形的世界法线（树状分支用来抬出/贴面）。
        /// 这些点作为 NanobotTree 的叶子终点 → 保证最终覆盖整表面且均匀。
        /// 无 mesh 时退化到 bounds 表面点（法线取 center→point 方向）。
        /// </summary>
        public Vector3[] GetEvenSurfaceSamples(int count, out Vector3[] normals)
        {
            if (count <= 0) { normals = System.Array.Empty<Vector3>(); return System.Array.Empty<Vector3>(); }

            // 稠密候选池（带法线）。候选越多，最远点下采样越均匀。
            int poolCount = Mathf.Max(count, count * 8);
            Vector3[] pool = SampleWithNormals(poolCount, out Vector3[] poolNormals);

            if (count >= pool.Length) { normals = poolNormals; return pool; }

            // 最远点采样：确定性地从 0 号起，每次选离已选集合最远的候选点。
            int[] picked = FarthestPointDownsample(pool, count);
            var pts = new Vector3[count];
            normals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                pts[i] = pool[picked[i]];
                normals[i] = poolNormals[picked[i]];
            }
            return pts;
        }

        /// <summary>面积加权表面采样，附带每点所在三角形的世界法线。无 mesh 时退化到 bounds。</summary>
        Vector3[] SampleWithNormals(int count, out Vector3[] normals)
        {
            var mesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
            if (mesh == null) return FallbackWithNormals(count, out normals);

            var verts = mesh.vertices;
            var tris = mesh.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0 || verts.Length == 0) return FallbackWithNormals(count, out normals);

            var l2w = transform.localToWorldMatrix;

            var v0 = new Vector3[triCount];
            var e1 = new Vector3[triCount];
            var e2 = new Vector3[triCount];
            var nrm = new Vector3[triCount];
            var cumArea = new float[triCount];
            float total = 0f;
            for (int tIdx = 0; tIdx < triCount; tIdx++)
            {
                Vector3 a = l2w.MultiplyPoint3x4(verts[tris[tIdx * 3 + 0]]);
                Vector3 b = l2w.MultiplyPoint3x4(verts[tris[tIdx * 3 + 1]]);
                Vector3 c = l2w.MultiplyPoint3x4(verts[tris[tIdx * 3 + 2]]);
                v0[tIdx] = a; e1[tIdx] = b - a; e2[tIdx] = c - a;
                Vector3 cross = Vector3.Cross(e1[tIdx], e2[tIdx]);
                nrm[tIdx] = cross.sqrMagnitude > 1e-12f ? cross.normalized : Vector3.up;
                total += 0.5f * cross.magnitude;
                cumArea[tIdx] = total;
            }
            if (total <= 1e-6f) return FallbackWithNormals(count, out normals);

            var result = new Vector3[count];
            normals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float pick = Hash.Unit(i, 10) * total;
                int t = LowerBound(cumArea, pick);

                float u = Hash.Unit(i, 11);
                float v = Hash.Unit(i, 12);
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                result[i] = v0[t] + e1[t] * u + e2[t] * v;
                normals[i] = nrm[t];
            }
            return result;
        }

        Vector3[] FallbackWithNormals(int count, out Vector3[] normals)
        {
            var b = Bounds;
            var result = new Vector3[count];
            normals = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Hash.Direction(i);
                result[i] = b.center + Vector3.Scale(dir, b.extents);
                normals[i] = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            }
            return result;
        }

        /// <summary>
        /// 返回一个"把任意点投影到本物体最近表面点"的委托（一次性缓存世界空间三角形）。
        /// 供 NanobotTree 把表面段细分点贴回表面，避免分支弦切入凹陷处（穿模）。
        /// 无 mesh 时返回恒等委托（不投影）。
        /// </summary>
        public System.Func<Vector3, Vector3> GetSurfaceProjector()
        {
            var mesh = _meshFilter != null ? _meshFilter.sharedMesh : null;
            if (mesh == null) return p => p;

            var verts = mesh.vertices;
            var tris = mesh.triangles;
            int triCount = tris.Length / 3;
            if (triCount == 0 || verts.Length == 0) return p => p;

            var l2w = transform.localToWorldMatrix;
            var a = new Vector3[triCount];
            var b = new Vector3[triCount];
            var c = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                a[t] = l2w.MultiplyPoint3x4(verts[tris[t * 3 + 0]]);
                b[t] = l2w.MultiplyPoint3x4(verts[tris[t * 3 + 1]]);
                c[t] = l2w.MultiplyPoint3x4(verts[tris[t * 3 + 2]]);
            }

            return p =>
            {
                float best = float.PositiveInfinity;
                Vector3 bestPt = p;
                for (int t = 0; t < triCount; t++)
                {
                    Vector3 q = ClosestPointOnTriangle(p, a[t], b[t], c[t]);
                    float d = (q - p).sqrMagnitude;
                    if (d < best) { best = d; bestPt = q; }
                }
                return bestPt;
            };
        }

        /// <summary>三角形上离 p 最近的点（Ericson, Real-Time Collision Detection）。</summary>
        static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + ab * (d1 / (d1 - d3));

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom, w2 = vc * denom;
            return a + ab * v2 + ac * w2;
        }

        /// <summary>最远点采样：返回 pool 中被选中的 count 个索引（确定性，0 号起）。</summary>
        static int[] FarthestPointDownsample(Vector3[] pool, int count)
        {
            int n = pool.Length;
            var picked = new int[count];
            var minDist = new float[n];
            for (int k = 0; k < n; k++) minDist[k] = float.PositiveInfinity;

            int current = 0; // 确定性起点
            for (int i = 0; i < count; i++)
            {
                picked[i] = current;
                float best = -1f;
                int bestIdx = current;
                for (int k = 0; k < n; k++)
                {
                    float d = (pool[k] - pool[current]).sqrMagnitude;
                    if (d < minDist[k]) minDist[k] = d;
                    if (minDist[k] > best) { best = minDist[k]; bestIdx = k; }
                }
                current = bestIdx;
            }
            return picked;
        }

        /// <summary>
        /// 启动包裹 shader 动画：入射点 P 在底部接触点，距离场从 P 向外扫满。
        /// _MaxDist = P 到包围盒 8 角的最远距离（归一用）。
        /// </summary>
        public void BeginWrapShader(Vector3 entryPoint)
        {
            if (!_wrapMat.HasProperty(IdGrow))
            {
                Debug.LogWarning($"[Possessable] {name} 的材质没有 _Grow 属性，未使用 NanobotWrap shader？", this);
                return;
            }
            _wrapMat.SetVector(IdEntry, entryPoint);
            _wrapMat.SetFloat(IdMaxDist, FarthestCornerDistance(entryPoint));

            if (_wrapCo != null) StopCoroutine(_wrapCo);
            _wrapCo = StartCoroutine(GrowRoutine());
        }

        float FarthestCornerDistance(Vector3 p)
        {
            var b = Bounds;
            Vector3 c = b.center, e = b.extents;
            float max = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = c + new Vector3(sx * e.x, sy * e.y, sz * e.z);
                        max = Mathf.Max(max, Vector3.Distance(p, corner));
                    }
            return Mathf.Max(0.01f, max);
        }

        IEnumerator GrowRoutine()
        {
            float t = 0f;
            float dur = Mathf.Max(0.01f, WrapDuration);
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                _wrapMat.SetFloat(IdGrow, Mathf.Clamp01(t));
                yield return null;
            }
            _wrapMat.SetFloat(IdGrow, 1f);
            _wrapCo = null;
        }

        /// <summary>包裹完成、附身生效。占位：通知规则层（控制转移由编排层做）。</summary>
        public void OnPossessed()
        {
            Highlighted = false;
            Debug.Log($"[Possessable] {name} 已被附身。", this);
        }

        /// <summary>脱离附身：停止包裹动画并把 _Grow 归 0，还原物体外观。</summary>
        public void ResetWrap()
        {
            if (_wrapCo != null) { StopCoroutine(_wrapCo); _wrapCo = null; }
            if (_wrapMat.HasProperty(IdGrow)) _wrapMat.SetFloat(IdGrow, 0f);
        }

        /// <summary>附身/脱离时开关自身碰撞体：附身期间关掉，避免与玩家胶囊复合碰撞冲突。</summary>
        public void SetCollidersEnabled(bool enabled)
        {
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = enabled;
        }
    }
}
