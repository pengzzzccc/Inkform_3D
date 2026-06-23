using UnityEngine;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 基于 Rigidbody 的 2.5D 移动控制（侧向 X 轴移动，锁 Z）。
    /// 当前形态通过 MovementProfile 调制 质量/阻尼/速度/跳跃/浮力 —— 实现"形态改变手感"。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("基础数值（基态）")]
        public float BaseSpeed = 5f;
        public float BaseJumpHeight = 1.6f;
        public float BaseMass = 1f;

        [Header("地面检测")]
        [Tooltip("从质心向下的探测距离。Capsule 半高约 1.0，需略大于它（默认 1.1）。")]
        public float GroundCheckDistance = 1.1f;
        public LayerMask GroundMask = ~0;

        Rigidbody _rb;
        MovementProfile _profile = MovementProfile.Default;
        Vector2 _moveInput;
        bool _jumpQueued;

        public bool IsGrounded { get; private set; }
        public MovementProfile CurrentProfile => _profile;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            ApplyProfile(MovementProfile.Default);
        }

        public void SetMoveInput(Vector2 v) => _moveInput = v;

        public void RequestJump()
        {
            if (_profile.CanJump && IsGrounded) _jumpQueued = true;
        }

        /// <summary>套用一个形态的移动参数。</summary>
        public void ApplyProfile(MovementProfile p)
        {
            _profile = p;
            _rb.mass = BaseMass * Mathf.Max(0.01f, p.MassMul);
            _rb.linearDamping = Mathf.Max(0f, p.Drag);
        }

        void FixedUpdate()
        {
            IsGrounded = Physics.Raycast(transform.position, Vector3.down, GroundCheckDistance,
                GroundMask, QueryTriggerInteraction.Ignore);

            // 侧向移动（2.5D）：用输入 x 控制 X 轴速度
            float speed = BaseSpeed * _profile.MoveSpeedMul;
            Vector3 vel = _rb.linearVelocity;
            vel.x = _moveInput.x * speed;
            vel.z = 0f;
            _rb.linearVelocity = vel;

            // 浮力 / 下沉
            if (Mathf.Abs(_profile.Buoyancy) > 0.0001f)
                _rb.AddForce(Vector3.up * _profile.Buoyancy, ForceMode.Acceleration);

            if (_jumpQueued)
            {
                _jumpQueued = false;
                float jumpHeight = BaseJumpHeight * _profile.JumpHeightMul;
                float g = Mathf.Abs(Physics.gravity.y);
                float jumpVel = Mathf.Sqrt(2f * g * Mathf.Max(0.01f, jumpHeight));
                var v = _rb.linearVelocity;
                v.y = jumpVel;
                _rb.linearVelocity = v;
            }
        }
    }
}
