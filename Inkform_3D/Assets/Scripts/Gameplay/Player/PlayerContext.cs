using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>聚合玩家可被形态操作的部件，避免能力直接到处 GetComponent。</summary>
    public class PlayerContext
    {
        public readonly Transform Transform;
        public readonly Rigidbody Rigidbody;
        public readonly PlayerMotor Motor;
        public readonly PlayerFormVisual Visual; // 形态表现钩子（可空）
        public readonly Light Glow;              // 灯泡形态发光（可空）

        public PlayerContext(Transform t, Rigidbody rb, PlayerMotor motor,
            PlayerFormVisual visual = null, Light glow = null)
        {
            Transform = t;
            Rigidbody = rb;
            Motor = motor;
            Visual = visual;
            Glow = glow;
        }
    }
}
