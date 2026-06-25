using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 形态层：回答"第 i 个 bot 此刻去哪"。每个阶段 = 一种 target 排布。
    /// NanobotSwarm 只负责把 bot 平滑追到这些目标点，形态本身无状态、可随意替换。
    /// 进度 t（0→1）由 swarm 按 speed 推进。
    /// </summary>
    public interface ISwarmFormation
    {
        /// <summary>第 i 个 bot（共 count 个）在进度 t 时的世界目标点。</summary>
        Vector3 SampleTarget(int i, int count, float t);

        /// <summary>此形态是否已到达终态（idle 类常驻形态恒 false）。</summary>
        bool IsComplete(float t);
    }

    /// <summary>
    /// 确定性伪随机：同一 i 永远得到同一组值，避免 idle 抖动。
    /// 切勿用 UnityEngine.Random 替代——那会让稳定形态每帧跳。
    /// </summary>
    internal static class Hash
    {
        /// <summary>返回 [0,1) 的确定性值。</summary>
        public static float Unit(int i, int salt = 0)
        {
            uint h = (uint)(i * 374761393 + salt * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>球面上的确定性方向。</summary>
        public static Vector3 Direction(int i)
        {
            float u = Unit(i, 1) * 2f - 1f;          // cosθ
            float phi = Unit(i, 2) * Mathf.PI * 2f;   // 方位角
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
            return new Vector3(r * Mathf.Cos(phi), u, r * Mathf.Sin(phi));
        }

        // ── 确定性分形噪声（fBm）：纯计算、无 GC、不用 UnityEngine.Random ──

        static float CellHash(int x, int y, int z, int salt)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + salt * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000; // [0,1)
        }

        /// <summary>格点哈希 + 三线性插值的 3D 值噪声，返回 [0,1)。</summary>
        public static float ValueNoise(Vector3 p, int salt = 0)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float fx = p.x - xi, fy = p.y - yi, fz = p.z - zi;
            // smoothstep 缓动
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);

            float c000 = CellHash(xi, yi, zi, salt), c100 = CellHash(xi + 1, yi, zi, salt);
            float c010 = CellHash(xi, yi + 1, zi, salt), c110 = CellHash(xi + 1, yi + 1, zi, salt);
            float c001 = CellHash(xi, yi, zi + 1, salt), c101 = CellHash(xi + 1, yi, zi + 1, salt);
            float c011 = CellHash(xi, yi + 1, zi + 1, salt), c111 = CellHash(xi + 1, yi + 1, zi + 1, salt);

            float x00 = Mathf.Lerp(c000, c100, fx), x10 = Mathf.Lerp(c010, c110, fx);
            float x01 = Mathf.Lerp(c001, c101, fx), x11 = Mathf.Lerp(c011, c111, fx);
            float y0 = Mathf.Lerp(x00, x10, fy), y1 = Mathf.Lerp(x01, x11, fy);
            return Mathf.Lerp(y0, y1, fz);
        }

        /// <summary>多倍频 fBm，返回 [-1,1] 附近。</summary>
        static float Fbm(Vector3 p, int salt, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < octaves; o++)
            {
                sum += (ValueNoise(p * freq, salt) - 0.5f) * 2f * amp;
                amp *= 0.5f;
                freq *= 2.02f;
            }
            return sum;
        }

        /// <summary>三个独立 salt 的 fBm 合成一个 [-1,1]³ 向量位移。</summary>
        public static Vector3 Fbm3(Vector3 p, int octaves = 3)
        {
            return new Vector3(
                Fbm(p, 11, octaves),
                Fbm(p, 23, octaves),
                Fbm(p, 37, octaves));
        }
    }

    /// <summary>
    /// 稳定/idle 形态：绕 center 的呼吸团（blob），常驻跟随玩家。
    /// 方向用确定性 hash，半径叠低频 Perlin 让团缓慢起伏。
    /// </summary>
    public sealed class BlobFormation : ISwarmFormation
    {
        readonly System.Func<Vector3> _center; // 跟随源（玩家位置）
        readonly float _radius;
        readonly float _wobble;

        public BlobFormation(System.Func<Vector3> center, float radius = 1.1f, float wobble = 0.25f)
        {
            _center = center;
            _radius = radius;
            _wobble = wobble;
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            Vector3 dir = Hash.Direction(i);
            // 半径：确定性基底 + 低频时间扰动（同一 i 的相位固定）
            float baseR = _radius * (0.5f + 0.5f * Hash.Unit(i, 3));
            float n = Mathf.PerlinNoise(Hash.Unit(i, 4) * 10f, Time.time * 0.6f) - 0.5f;
            float r = baseR + n * _wobble;
            return _center() + dir * r;
        }

        public bool IsComplete(float t) => false; // idle 常驻
    }

    /// <summary>
    /// 树状分形分支形态：bot 沿预解算的 <see cref="NanobotTree"/> 生长。
    /// 起点 A → 共享树干 → 递归二分分叉 → 表面均匀叶子末梢。
    ///
    /// 用**生长前沿**（统一物理推进弧长 front = t·(MaxLen+lag)）驱动：共享段上的 bot
    /// 世界点重合 → 树干清晰、在分叉点精确裂开，像真的树在生长。bot 按 i%LeafCount 分摊
    /// 到叶子路径，叠加横向粗细偏移使分支有体积、末梢成簇。
    ///
    /// ⚠ 发散铁律（与旧 PathFlowFormation 相反）：树只发散不汇聚。横向偏移/抖动包络在
    /// 末梢(arc→Length)归零 → 终点精确落在均匀叶子上，保覆盖均匀且分支不回聚。
    /// 抖动幅度随离面高度衰减 → 空中段略卷曲、表面段近乎不抖（蔓延而非缠绕）。
    /// </summary>
    public sealed class TreeBranchFormation : ISwarmFormation
    {
        readonly NanobotTree _tree;
        readonly float _maxLag;       // 队伍拉散（弧长单位）
        readonly float _thickness;    // 分支粗细（同叶多 bot 横向簇宽）
        readonly float _jitterAmp;    // 空中段分形抖动幅度
        readonly float _jitterScale;  // 抖动噪声频率
        readonly float _flowSpeed;    // 抖动沿时间漂移
        const int Octaves = 3;

        public TreeBranchFormation(NanobotTree tree, float trail = 2f, float thickness = 0.3f,
            float jitterAmp = 0.3f, float jitterScale = 0.6f, float flowSpeed = 0.4f)
        {
            _tree = tree;
            _maxLag = Mathf.Max(0f, trail);
            _thickness = Mathf.Max(0f, thickness);
            _jitterAmp = Mathf.Max(0f, jitterAmp);
            _jitterScale = Mathf.Max(0.01f, jitterScale);
            _flowSpeed = flowSpeed;
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            int L = _tree.LeafCount;
            if (L == 0) return Vector3.zero;
            BotPath path = _tree.LeafPaths[i % L];

            // 生长前沿：统一物理推进弧长，lag 让队伍在路上拉成一条。
            // t=1 时 front-lag ≥ MaxLen ≥ 任意 path.Length → 全员到达各自叶子。
            float lag = Hash.Unit(i, 5) * _maxLag;
            float front = t * (_tree.MaxLen + _maxLag);
            float arc = Mathf.Clamp(front - lag, 0f, path.Length);
            path.Sample(arc, out Vector3 pos, out float height, out Vector3 tangent);

            // 末梢/起点收束包络：在 arc→0(A) 与 arc→Length(叶) 处归零 → 树干/末梢清晰，
            // 终点精确落在均匀叶子上（保覆盖均匀、不回聚）。
            float taperLen = Mathf.Max(0.01f, _thickness * 4f);
            float envelope = Mathf.Clamp01(arc / taperLen)
                           * Mathf.Clamp01((path.Length - arc) / taperLen);

            // ① 横向粗细：沿切线的法平面用确定性方向散开 → 分支有体积、末梢成簇。
            if (_thickness > 0f && envelope > 1e-4f)
            {
                Vector3 n1 = Vector3.Cross(tangent, Vector3.up);
                if (n1.sqrMagnitude < 1e-6f) n1 = Vector3.right;
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tangent, n1).normalized;
                float a = (Hash.Unit(i, 13) - 0.5f) * 2f;
                float b = (Hash.Unit(i, 14) - 0.5f) * 2f;
                pos += (n1 * a + n2 * b) * (_thickness * envelope);
            }

            // ② 分形抖动：随离面高度衰减 → 表面段几乎不抖（蔓延非缠绕）。
            float airT = _tree.OutwardLift > 1e-4f ? Mathf.Clamp01(height / _tree.OutwardLift) : 0f;
            if (_jitterAmp > 0f && airT > 1e-3f)
            {
                Vector3 phase = new Vector3(Hash.Unit(i, 6), Hash.Unit(i, 7), Hash.Unit(i, 8)) * 50f;
                Vector3 np = pos * _jitterScale + phase + Vector3.one * (Time.time * _flowSpeed);
                pos += Hash.Fbm3(np, Octaves) * (_jitterAmp * airT * envelope);
            }

            return pos;
        }

        public bool IsComplete(float t) => t >= 1f;
    }

    /// <summary>
    /// 包裹形态：bot 散布到目标表面采样点。
    /// 点数与 bot 数不等也能跑（i % points.Length，密度随之变化）。
    /// </summary>
    public sealed class SurfaceWrapFormation : ISwarmFormation
    {
        readonly Vector3[] _points;

        public SurfaceWrapFormation(Vector3[] surfacePoints)
        {
            _points = (surfacePoints != null && surfacePoints.Length > 0)
                ? surfacePoints
                : new[] { Vector3.zero };
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            return _points[i % _points.Length];
        }

        public bool IsComplete(float t) => t >= 1f;
    }
}
