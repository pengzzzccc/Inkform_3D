using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 玩家组装入口：连接输入 → 移动 / 扫描 / 能力。
    /// Interact：若在某 ScanTarget 范围内则扫描该形态，否则还原基态。
    /// 输入在被锁定（重生/过场）时忽略。
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(PlayerMotor), typeof(AbilitySystem))]
    public class PlayerActor : MonoBehaviour
    {
        public InputReader Input;

        PlayerMotor _motor;
        AbilitySystem _ability;
        PlayerContext _ctx;
        ScanTarget _nearby;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _ability = GetComponent<AbilitySystem>();
            var rb = GetComponent<Rigidbody>();
            var visual = GetComponent<PlayerFormVisual>();
            _ctx = new PlayerContext(transform, rb, _motor, visual);
            _ability.Init(_ctx);
        }

        void Update()
        {
            // 检测最近可扫描目标的变化，广播给交互提示 UI。
            var t = FindNearbyTarget();
            if (t != _nearby)
            {
                _nearby = t;
                EventBus.Publish(new NearbyScanTargetChanged
                {
                    HasTarget = t != null,
                    FormName = (t != null && t.Config != null) ? t.Config.DisplayName : ""
                });
            }
        }

        void OnEnable()
        {
            if (Input == null) return;
            Input.MoveChanged += OnMove;
            Input.InteractPressed += OnInteract;
            Input.UsePressed += OnUse;
            Input.JumpPressed += OnJump;
        }

        void OnDisable()
        {
            if (Input == null) return;
            Input.MoveChanged -= OnMove;
            Input.InteractPressed -= OnInteract;
            Input.UsePressed -= OnUse;
            Input.JumpPressed -= OnJump;
        }

        bool Locked => ManagerRoot.Instance != null && ManagerRoot.Instance.InputLock.IsLocked;

        void OnMove(Vector2 v) => _motor.SetMoveInput(Locked ? Vector2.zero : v);
        void OnJump() { if (!Locked) _motor.RequestJump(); }
        void OnUse() { if (!Locked) _ability.UseAbility(); }

        void OnInteract()
        {
            if (Locked) return;
            var t = FindNearbyTarget();
            if (t != null) _ability.Scan(t.Config);
            else _ability.RevertToCore();
        }

        static ScanTarget FindNearbyTarget()
        {
            // M1 场景中 ScanTarget 数量很少，直接遍历取在范围内的那个。
            foreach (var st in FindObjectsByType<ScanTarget>(FindObjectsSortMode.None))
                if (st.PlayerInRange) return st;
            return null;
        }
    }
}
