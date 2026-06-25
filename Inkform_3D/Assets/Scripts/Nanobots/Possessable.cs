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

        /// <summary>包裹完成、附身生效。占位：切控制权 / 通知规则层。</summary>
        public void OnPossessed()
        {
            Highlighted = false;
            Debug.Log($"[Possessable] {name} 已被附身。", this);
        }
    }
}
