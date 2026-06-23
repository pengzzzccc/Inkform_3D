using UnityEngine;
using Cysharp.Threading.Tasks;
using Inkform.Core;
using Inkform.UI;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 检查点—进度系统：订阅 PlayerKilled，执行 淡出→回最近检查点→恢复→淡入 的重生序列。
    /// </summary>
    public class CheckpointSystem : MonoBehaviour
    {
        public static CheckpointSystem Instance { get; private set; }

        public Transform Player;
        public ScreenFader Fader;          // 可空：为空则跳过淡入淡出
        public float FadeDuration = 0.35f;

        CheckpointVolume _active;
        Vector3 _start;
        bool _respawning;

        void Awake()
        {
            Instance = this;
            if (Player != null) _start = Player.position;
        }

        void OnEnable() => EventBus.Subscribe<PlayerKilled>(OnKilled);

        void OnDisable()
        {
            EventBus.Unsubscribe<PlayerKilled>(OnKilled);
            if (Instance == this) Instance = null;
        }

        public void Activate(CheckpointVolume cp)
        {
            _active = cp;
            EventBus.Publish(new CheckpointReached { Id = cp.Id });
        }

        Vector3 Respawn => _active != null ? _active.Respawn : _start;

        void OnKilled(PlayerKilled _)
        {
            if (_respawning) return; // 重生过程中再次触发则忽略
            RespawnRoutine().Forget();
        }

        async UniTaskVoid RespawnRoutine()
        {
            _respawning = true;
            var root = ManagerRoot.Instance;
            root?.InputLock.Acquire();
            root?.State.Set(GameState.Respawning);

            if (Fader != null) await Fader.FadeOut(FadeDuration);

            if (Player != null)
            {
                if (Player.TryGetComponent<Rigidbody>(out var rb))
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                Player.position = Respawn;
                if (Player.TryGetComponent<AbilitySystem>(out var ability))
                    ability.RevertToCore();
            }

            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IResettable res) res.ResetToCheckpoint();

            if (Fader != null) await Fader.FadeIn(FadeDuration);

            root?.State.Set(GameState.Playing);
            root?.InputLock.Release();
            EventBus.Publish(new OnRespawn { CheckpointId = _active != null ? _active.Id : -1 });
            _respawning = false;
        }
    }
}
