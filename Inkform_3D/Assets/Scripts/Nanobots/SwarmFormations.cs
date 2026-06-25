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
    /// 折线路径：按弧长参数化采样。A→F→P 的 L 形路径用它表达。
    /// 段长在构造时缓存，PointAt01 按归一弧长插值。
    /// </summary>
    public sealed class PolylinePath
    {
        readonly Vector3[] _pts;
        readonly float[] _cumLen; // _cumLen[k] = 从起点到第 k 个点的累计弧长
        public float TotalLength { get; }

        public PolylinePath(params Vector3[] points)
        {
            _pts = points;
            _cumLen = new float[points.Length];
            float acc = 0f;
            _cumLen[0] = 0f;
            for (int k = 1; k < points.Length; k++)
            {
                acc += Vector3.Distance(points[k - 1], points[k]);
                _cumLen[k] = acc;
            }
            TotalLength = acc;
        }

        /// <summary>归一弧长 s∈[0,1] 处的世界点。</summary>
        public Vector3 PointAt01(float s)
        {
            if (_pts.Length == 1 || TotalLength <= 1e-5f) return _pts[_pts.Length - 1];
            s = Mathf.Clamp01(s);
            float target = s * TotalLength;
            for (int k = 1; k < _pts.Length; k++)
            {
                if (target <= _cumLen[k] || k == _pts.Length - 1)
                {
                    float segLen = _cumLen[k] - _cumLen[k - 1];
                    float f = segLen > 1e-5f ? (target - _cumLen[k - 1]) / segLen : 0f;
                    return Vector3.Lerp(_pts[k - 1], _pts[k], Mathf.Clamp01(f));
                }
            }
            return _pts[_pts.Length - 1];
        }

        /// <summary>归一弧长 s 处的路径切线方向（前进方向，已归一化）。</summary>
        public Vector3 TangentAt01(float s)
        {
            if (_pts.Length < 2 || TotalLength <= 1e-5f) return Vector3.forward;
            float eps = 0.01f;
            Vector3 ahead = PointAt01(Mathf.Min(1f, s + eps));
            Vector3 behind = PointAt01(Mathf.Max(0f, s - eps));
            Vector3 dir = ahead - behind;
            return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
        }
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
    /// 蔓延+立起形态：bot 沿折线 A→F→P 流动，途中从一股分裂成几条支流（河流三角洲），
    /// 接近目标处各支流收束汇合。支流内再叠分形(fBm)细抖动。
    /// trail 控制队伍拉散程度；t=1 时全员汇聚到终点 P（为包裹做准备）。
    /// ⚠ 收敛性铁律：所有横向(支流)/分形偏移在 s→1 处归零 → t=1 全员汇聚 P，绝不可破。
    /// </summary>
    public sealed class PathFlowFormation : ISwarmFormation
    {
        readonly PolylinePath _path;
        readonly float _trail;
        readonly int _branchCount;     // 支流数（一股分成几股）
        readonly float _branchSpread;  // 支流最大横向间距
        readonly float _fractalAmp;    // 支流内分形细抖动幅度
        readonly float _fractalScale;  // 噪声频率
        readonly float _flowSpeed;     // 沿时间漂移，让虫群"活"起来
        const int Octaves = 3;

        public PathFlowFormation(PolylinePath path, float trail = 0.35f,
            int branchCount = 4, float branchSpread = 2.5f,
            float fractalAmp = 0.35f, float fractalScale = 1.5f, float flowSpeed = 0.4f)
        {
            _path = path;
            _trail = Mathf.Max(0f, trail);
            _branchCount = Mathf.Max(1, branchCount);
            _branchSpread = Mathf.Max(0f, branchSpread);
            _fractalAmp = Mathf.Max(0f, fractalAmp);
            _fractalScale = Mathf.Max(0.01f, fractalScale);
            _flowSpeed = flowSpeed;
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            // 每个 bot 落后一点点（确定性），队伍因此在路上拉成一条。
            // t=1 时 (1+trail) - hash*trail >= 1 → Clamp01 全部为 1 → 汇聚到 P。
            float lag = Hash.Unit(i, 5) * _trail;
            float s = Mathf.Clamp01(t * (1f + _trail) - lag);
            Vector3 basePos = _path.PointAt01(s);

            // 分叉包络：sin(π·s) 在 s=0(A,一源) 与 s=1(P,汇合) 处为 0，中段最大。
            // 立起段靠近 s=1，包络已小 → 支流自然收束成一股立起。
            float branchEnv = Mathf.Sin(Mathf.PI * s);

            Vector3 pos = basePos;

            // ① 支流横向偏移：每个 bot 归属一条支流，沿路径横向(地面平面)拉开。
            if (_branchCount > 1 && _branchSpread > 0f && branchEnv > 1e-4f)
            {
                int branch = i % _branchCount;
                float lane = (branch / (float)(_branchCount - 1) - 0.5f) * 2f; // [-1,1]
                Vector3 tangent = _path.TangentAt01(s);
                Vector3 side = Vector3.Cross(tangent, Vector3.up);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right; // 竖直立起段退化兜底
                side.Normalize();

                // 支流内宽度：同支流的 bot 用 hash 在通道里小幅散开，不挤成一条线。
                float laneJitter = (Hash.Unit(i, 9) - 0.5f) * (_branchSpread / _branchCount);
                // lane∈[-1,1]，乘 _branchSpread → 最外两股相距 2*_branchSpread。
                pos += side * ((lane * _branchSpread + laneJitter) * branchEnv);
            }

            // ② 支流内分形细抖动：确定性相位 + 时间漂移；同样按 s→1 衰减。
            if (_fractalAmp > 0f)
            {
                Vector3 phase = new Vector3(Hash.Unit(i, 6), Hash.Unit(i, 7), Hash.Unit(i, 8)) * 50f;
                Vector3 noisePos = basePos * _fractalScale + phase + Vector3.one * (Time.time * _flowSpeed);
                pos += Hash.Fbm3(noisePos, Octaves) * (_fractalAmp * branchEnv);
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
