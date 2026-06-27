using System;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 聚合形态（常态 Idle）：贴地的**半球** metacube 团，常驻跟随玩家脚下地面点。
    /// 方向取上半球（y≥0、底面贴地）；方位随时间缓慢漂移 = 身上 cube 流动。
    /// 子态由 <see cref="Dispersion"/> 驱动：0=收紧**聚合**、1=松散**离散**（半径放大 + 抖动加大）。
    /// center() 应返回玩家**脚下地面点**（半球底面落在地面）。
    /// </summary>
    public sealed class AggregateForm : IMetacubeForm
    {
        readonly Func<Vector3> _center;

        /// <summary>聚合时的基础半径。</summary>
        public float Radius;
        /// <summary>低频 wobble 幅度（呼吸般的表面起伏）。</summary>
        public float Wobble;
        /// <summary>方位漂移速度（cube 在身上流动的快慢）。</summary>
        public float Flow;
        /// <summary>0..1：0=聚合（收紧）、1=离散（松散铺开）。编排层每帧平滑设置。</summary>
        public float Dispersion;

        public AggregateForm(Func<Vector3> center, float radius = 1.1f, float wobble = 0.18f, float flow = 0.5f)
        {
            _center = center;
            Radius = radius;
            Wobble = wobble;
            Flow = flow;
            Dispersion = 0f;
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            float disp = Mathf.Clamp01(Dispersion);

            // 上半球方向：cosθ∈[0,1] → y≥0；方位随时间漂移让 cube 在身上流动。
            float cosT = Hash.Unit(i, 1);                 // [0,1) → 上半球
            float sinT = Mathf.Sqrt(Mathf.Max(0f, 1f - cosT * cosT));
            float phi = Hash.Unit(i, 2) * Mathf.PI * 2f + Time.time * Flow * (0.5f + Hash.Unit(i, 5));
            Vector3 dir = new Vector3(sinT * Mathf.Cos(phi), cosT, sinT * Mathf.Sin(phi));

            // 填充实心穹顶：每 cube 确定性半径 + 低频时间 wobble。离散时整体放大、抖动加剧。
            float radMul = Mathf.Lerp(1f, 1.8f, disp);
            float baseR = Radius * radMul * (0.35f + 0.65f * Hash.Unit(i, 3));
            float wob = (Mathf.PerlinNoise(Hash.Unit(i, 4) * 10f, Time.time * 0.6f) - 0.5f)
                        * Wobble * (1f + disp * 2f);
            return _center() + dir * (baseR + wob);
        }

        public bool IsComplete(float t) => false; // 常驻

        public float FollowSmoothTime => -1f; // 用系统默认（液态流动）
    }
}
