using System;

namespace Inkform.Data
{
    /// <summary>
    /// 移动玩法参数：随所拟形态切换，实现"形态改变手感"。
    /// 各倍率以基态(1.0)为基准；Buoyancy &gt;0 上浮 / &lt;0 下沉。
    /// </summary>
    [Serializable]
    public struct MovementProfile
    {
        public float MoveSpeedMul;   // 移动速度倍率（船锚 <1）
        public float MassMul;        // 质量倍率（船锚 >1，影响下沉）
        public float JumpHeightMul;  // 跳跃高度倍率（船锚 <1）
        public float Buoyancy;       // 浮力加速度（气球 >0 / 船锚 <0）
        public float Drag;           // 线性阻尼
        public bool  CanJump;        // 船锚可设 false

        public static MovementProfile Default => new MovementProfile
        {
            MoveSpeedMul = 1f,
            MassMul = 1f,
            JumpHeightMul = 1f,
            Buoyancy = 0f,
            Drag = 0f,
            CanJump = true,
        };
    }
}
