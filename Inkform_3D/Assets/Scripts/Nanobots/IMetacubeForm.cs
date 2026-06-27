using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 形态层：回答"第 i 个 metacube 此刻去哪"。每个阶段 = 一种 target 排布。
    /// <see cref="MetacubeSystem"/> 只负责把 cube 平滑追到这些目标点；形态本身无状态、可随意替换。
    /// 进度 t（0→1）由 MetacubeSystem 按 speed 推进。
    /// </summary>
    public interface IMetacubeForm
    {
        /// <summary>第 i 个 cube（共 count 个）在进度 t 时的世界目标点。</summary>
        Vector3 SampleTarget(int i, int count, float t);

        /// <summary>此形态是否已到达终态（常驻形态恒 false）。</summary>
        bool IsComplete(float t);

        /// <summary>
        /// 此形态期望的跟随平滑时间（SmoothDamp）。返回 &lt; 0 表示用 <see cref="MetacubeSystem.SmoothTime"/>。
        /// 过渡形态返回 -1 保持液态流动；附身贴面返回很小值近乎刚性跟随（避免移动时拖尾）。
        /// </summary>
        float FollowSmoothTime { get; }
    }

    /// <summary>
    /// 确定性伪随机：同一 i 永远得到同一组值，避免常态抖动。
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
    }
}
