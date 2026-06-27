using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 蔓延形态（Spreading）：metacube 沿预解算的 <see cref="SpreadPathSolver"/> 路径生长。
    /// 统一生长前沿（弧长 front = prog·(MaxLen+lag)，lag 让队伍拉散成流）。cube 按 i%LeafCount
    /// 分摊到各叶路径；横向 thickness 让分支成簇有体积，末梢/源端收束包络保证终点精确落叶、不回聚。
    /// reverse=true：从叶收回源（脱离用）。
    /// </summary>
    public sealed class SpreadForm : IMetacubeForm
    {
        readonly SpreadPathSolver _solver;
        readonly float _maxLag;
        readonly float _thickness;
        readonly bool _reverse;

        public SpreadForm(SpreadPathSolver solver, float trail = 1.5f,
            float thickness = 0.25f, bool reverse = false)
        {
            _solver = solver;
            _maxLag = Mathf.Max(0f, trail);
            _thickness = Mathf.Max(0f, thickness);
            _reverse = reverse;
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            int L = _solver.LeafCount;
            if (L == 0) return Vector3.zero;
            BotPath path = _solver.LeafPaths[i % L];

            float lag = Hash.Unit(i, 5) * _maxLag;
            float prog = _reverse ? (1f - t) : t;
            float front = prog * (_solver.MaxLen + _maxLag);
            float arc = Mathf.Clamp(front - lag, 0f, path.Length);
            path.Sample(arc, out Vector3 pos, out Vector3 tangent);

            // 末梢/源端收束包络：两端归零 → 主干/末梢清晰、终点精确落叶（覆盖均匀、不回聚）。
            float taper = Mathf.Max(0.01f, _thickness * 4f);
            float env = Mathf.Clamp01(arc / taper) * Mathf.Clamp01((path.Length - arc) / taper);
            if (_thickness > 0f && env > 1e-4f)
            {
                Vector3 n1 = Vector3.Cross(tangent, Vector3.up);
                if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.right;
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tangent, n1).normalized;
                float a = (Hash.Unit(i, 13) - 0.5f) * 2f;
                float b = (Hash.Unit(i, 14) - 0.5f) * 2f;
                pos += (n1 * a + n2 * b) * (_thickness * env);
            }
            return pos;
        }

        public bool IsComplete(float t) => t >= 1f;

        public float FollowSmoothTime => -1f; // 用系统默认（液态流动）
    }
}
