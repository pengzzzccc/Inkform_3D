using UnityEngine;
using Inkform.Core;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 基于 Rigidbody 的 WASD 平面移动（世界 XZ 平面，A/D=X 左右、W/S=Z 前后）。
    /// 当前形态通过 MovementProfile 调制 质量/阻尼/速度/跳跃/浮力 —— 实现"形态改变手感"。
    /// 发布 Jumped/Landed 事件，并驱动脚步循环音。
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

        [Header("音频")]
        [Tooltip("脚步循环音源（移动且着地时播放）")]
        public AudioSource FootstepSource;

        Rigidbody _rb;
        MovementProfile _profile = MovementProfile.Default;
        Vector2 _moveInput;
        bool _jumpQueued;
        bool _wasGrounded = true;

        public bool IsGrounded { get; private set; }
        public MovementProfile CurrentProfile => _profile;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotation; // 平面自由移动，仅锁旋转防翻倒
            ApplyProfile(MovementProfile.Default);
        }

        public void SetMoveInput(Vector2 v) => _moveInput = v;

        public void RequestJump()
        {
            if (_profile.CanJump && IsGrounded) _jumpQueued = true;
        }

        public void ApplyProfile(MovementProfile p)
        {
            _profile = p;
            _rb.mass = BaseMass * Mathf.Max(0.01f, p.MassMul);
            _rb.linearDamping = Mathf.Max(0f, p.Drag);
        }

        void Update()
        {
            // 脚步循环音：着地且有移动输入时播放
            if (FootstepSource != null)
            {
                bool moving = IsGrounded && _moveInput.sqrMagnitude > 0.02f;
                if (moving && !FootstepSource.isPlaying) FootstepSource.Play();
                else if (!moving && FootstepSource.isPlaying) FootstepSource.Pause();
            }
        }

        void FixedUpdate()
        {
            IsGrounded = Physics.Raycast(transform.position, Vector3.down, GroundCheckDistance,
                GroundMask, QueryTriggerInteraction.Ignore);

            if (IsGrounded && !_wasGrounded) EventBus.Publish(new Landed());
            _wasGrounded = IsGrounded;

            // 平面移动：A/D → X，W/S → Z（世界轴）
            float speed = BaseSpeed * _profile.MoveSpeedMul;
            Vector3 vel = _rb.linearVelocity;
            vel.x = _moveInput.x * speed;
            vel.z = _moveInput.y * speed;
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
                EventBus.Publish(new Jumped());
            }
        }
    }
}
