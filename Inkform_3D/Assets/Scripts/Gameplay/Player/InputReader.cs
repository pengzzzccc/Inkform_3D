using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 读取现有 InputSystem_Actions 的 Player Map，向上暴露强类型事件。
    /// 无需为 inputactions 生成 C# 包装类：运行时按名查找 Action。
    /// 映射：Move=移动 / Interact=扫描·还原 / Attack=使用能力 / Jump=跳 / Crouch=掩体。
    /// </summary>
    public class InputReader : MonoBehaviour
    {
        [Tooltip("拖入 Assets/InputSystem_Actions")]
        public InputActionAsset Actions;

        public event Action<Vector2> MoveChanged;
        public event Action InteractPressed;
        public event Action UsePressed;
        public event Action JumpPressed;
        public event Action CrouchPressed;
        public event Action ScanPressed;   // 附身扫描/切候选（绑 Sprint=LeftShift）

        InputActionMap _player;
        InputAction _move, _interact, _use, _jump, _crouch, _scan;

        void Awake()
        {
            if (Actions == null)
            {
                Debug.LogError("[InputReader] Actions 资产未赋值。");
                enabled = false;
                return;
            }
            _player = Actions.FindActionMap("Player", true);
            _move = _player.FindAction("Move", true);
            _interact = _player.FindAction("Interact", true);
            _use = _player.FindAction("Attack", true);
            _jump = _player.FindAction("Jump", false);
            _crouch = _player.FindAction("Crouch", false);
            _scan = _player.FindAction("Sprint", false); // 复用 Sprint(LeftShift) 作扫描
        }

        void OnEnable()
        {
            if (_player == null) return;
            _player.Enable();
            _move.performed += OnMove;
            _move.canceled += OnMove;
            _interact.performed += OnInteract;
            _use.performed += OnUse;
            if (_jump != null) _jump.performed += OnJump;
            if (_crouch != null) _crouch.performed += OnCrouch;
            if (_scan != null) _scan.performed += OnScan;
        }

        void OnDisable()
        {
            if (_player == null) return;
            _move.performed -= OnMove;
            _move.canceled -= OnMove;
            _interact.performed -= OnInteract;
            _use.performed -= OnUse;
            if (_jump != null) _jump.performed -= OnJump;
            if (_crouch != null) _crouch.performed -= OnCrouch;
            if (_scan != null) _scan.performed -= OnScan;
            _player.Disable();
        }

        void OnMove(InputAction.CallbackContext c) => MoveChanged?.Invoke(c.ReadValue<Vector2>());
        void OnInteract(InputAction.CallbackContext c) => InteractPressed?.Invoke();
        void OnUse(InputAction.CallbackContext c) => UsePressed?.Invoke();
        void OnJump(InputAction.CallbackContext c) => JumpPressed?.Invoke();
        void OnCrouch(InputAction.CallbackContext c) => CrouchPressed?.Invoke();
        void OnScan(InputAction.CallbackContext c) => ScanPressed?.Invoke();
    }
}
