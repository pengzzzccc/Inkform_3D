using UnityEngine;
using UnityEngine.UI;
using Inkform.Core;

namespace Inkform.UI
{
    /// <summary>
    /// 交互提示：靠近可扫描目标时显示 "[E] 扫描：船锚"；扫描/还原时弹出短暂 toast。
    /// 只订阅 Core 事件，不依赖 Gameplay。
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        public Text PromptText;        // 常驻提示（靠近扫描目标时）
        public Text AbilityPromptText; // 操控类能力的瞄准提示（[左键] 操控…）
        public Text ToastText;         // 短暂反馈
        public float ToastDuration = 1.2f;

        float _toastUntil;

        void Awake()
        {
            if (PromptText != null) PromptText.text = "";
            if (AbilityPromptText != null) AbilityPromptText.text = "";
            if (ToastText != null) ToastText.text = "";
        }

        void OnEnable()
        {
            EventBus.Subscribe<NearbyScanTargetChanged>(OnNearby);
            EventBus.Subscribe<TargetScanned>(OnScanned);
            EventBus.Subscribe<FormDissolved>(OnDissolved);
            EventBus.Subscribe<AbilityTargetInRange>(OnAbilityTarget);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<NearbyScanTargetChanged>(OnNearby);
            EventBus.Unsubscribe<TargetScanned>(OnScanned);
            EventBus.Unsubscribe<FormDissolved>(OnDissolved);
            EventBus.Unsubscribe<AbilityTargetInRange>(OnAbilityTarget);
        }

        void OnAbilityTarget(AbilityTargetInRange e)
        {
            if (AbilityPromptText != null) AbilityPromptText.text = e.HasTarget ? e.Hint : "";
        }

        void OnNearby(NearbyScanTargetChanged e)
        {
            if (PromptText == null) return;
            PromptText.text = e.HasTarget
                ? $"[E] 扫描：{e.FormName}"
                : "[E] 还原基态（远离目标时）";
        }

        void OnScanned(TargetScanned e) => Toast($"已扫描 → {e.Form} 形态");
        void OnDissolved(FormDissolved e) => Toast("已还原基态");

        void Toast(string s)
        {
            if (ToastText == null) return;
            ToastText.text = s;
            _toastUntil = Time.time + ToastDuration;
        }

        void Update()
        {
            if (ToastText != null && ToastText.text.Length > 0 && Time.time > _toastUntil)
                ToastText.text = "";
        }
    }
}
