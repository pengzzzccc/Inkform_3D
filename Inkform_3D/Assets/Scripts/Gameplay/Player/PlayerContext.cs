using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>聚合玩家可被形态操作的部件，避免能力直接到处 GetComponent。</summary>
    public class PlayerContext
    {
        public readonly Transform Transform;
        public readonly Rigidbody Rigidbody;
        public readonly PlayerMotor Motor;

        public PlayerContext(Transform t, Rigidbody rb, PlayerMotor motor)
        {
            Transform = t;
            Rigidbody = rb;
            Motor = motor;
        }
    }
}
