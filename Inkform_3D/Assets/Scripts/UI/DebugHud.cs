using UnityEngine;
using UnityEngine.UI;
using Inkform.Core;

namespace Inkform.UI
{
    /// <summary>
    /// 验证用调试 HUD：屏显当前形态 / 游戏状态 / 最近检查点 / 最近事件 + 操作提示。
    /// 只订阅 EventBus（Core），不引用 Gameplay，保持 UI 层依赖干净。
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        public Text Label;

        FormId _form = FormId.Core;
        int _checkpoint = -1;
        string _lastEvent = "-";

        void OnEnable()
        {
            EventBus.Subscribe<FormMaterialized>(OnMaterialized);
            EventBus.Subscribe<FormDissolved>(OnDissolved);
            EventBus.Subscribe<CheckpointReached>(OnCheckpoint);
            EventBus.Subscribe<PlayerKilled>(OnKilled);
            EventBus.Subscribe<OnRespawn>(OnRespawned);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<FormMaterialized>(OnMaterialized);
            EventBus.Unsubscribe<FormDissolved>(OnDissolved);
            EventBus.Unsubscribe<CheckpointReached>(OnCheckpoint);
            EventBus.Unsubscribe<PlayerKilled>(OnKilled);
            EventBus.Unsubscribe<OnRespawn>(OnRespawned);
        }

        void OnMaterialized(FormMaterialized e) => _form = e.Form;
        void OnDissolved(FormDissolved e) => _form = FormId.Core;
        void OnCheckpoint(CheckpointReached e) => _checkpoint = e.Id;
        void OnKilled(PlayerKilled e) => _lastEvent = "KILLED by " + e.Source;
        void OnRespawned(OnRespawn e) => _lastEvent = "Respawned @ cp" + e.CheckpointId;

        void Update()
        {
            if (Label == null) return;
            string state = ManagerRoot.Instance != null ? ManagerRoot.Instance.State.Current.ToString() : "-";
            Label.text =
                $"Form: {_form}\n" +
                $"State: {state}\n" +
                $"Last Checkpoint: {_checkpoint}\n" +
                $"Event: {_lastEvent}\n" +
                "──────────\n" +
                "[A/D] move  [Space] jump\n" +
                "[E] scan (near target) / revert\n" +
                "[Mouse L] use ability";
        }
    }
}
