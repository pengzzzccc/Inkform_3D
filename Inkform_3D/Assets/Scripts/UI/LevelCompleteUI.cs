using UnityEngine;
using Inkform.Core;

namespace Inkform.UI
{
    /// <summary>到达 EXIT(LevelCompleted) 时显示全屏 "LEVEL COMPLETE" 面板。</summary>
    public class LevelCompleteUI : MonoBehaviour
    {
        [Tooltip("通关面板根（默认隐藏，通关时显示）")]
        public GameObject Panel;

        void Awake()
        {
            if (Panel != null) Panel.SetActive(false);
        }

        void OnEnable() => EventBus.Subscribe<LevelCompleted>(OnCompleted);
        void OnDisable() => EventBus.Unsubscribe<LevelCompleted>(OnCompleted);

        void OnCompleted(LevelCompleted _)
        {
            if (Panel != null) Panel.SetActive(true);
        }
    }
}
