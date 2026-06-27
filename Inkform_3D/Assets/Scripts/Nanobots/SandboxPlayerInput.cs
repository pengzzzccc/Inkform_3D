using UnityEngine;
using Inkform.Gameplay;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 沙盒用的极简玩家输入适配：只把 Move/Jump 喂给 PlayerMotor，
    /// 把 1/2(选择)·Interact(附身)·Attack(脱离) 留给 PossessionDirector。
    /// 不引入完整 PlayerActor + AbilitySystem，避免与附身系统抢同一组输入事件。
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class SandboxPlayerInput : MonoBehaviour
    {
        public InputReader Input;

        PlayerMotor _motor;

        void Awake() => _motor = GetComponent<PlayerMotor>();

        void OnEnable()
        {
            if (Input == null) return;
            Input.MoveChanged += OnMove;
            Input.JumpPressed += OnJump;
        }

        void OnDisable()
        {
            if (Input == null) return;
            Input.MoveChanged -= OnMove;
            Input.JumpPressed -= OnJump;
        }

        void OnMove(Vector2 v) => _motor.SetMoveInput(v);
        void OnJump() => _motor.RequestJump();
    }
}
