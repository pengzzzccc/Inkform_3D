using UnityEngine;
using Inkform.Core;

namespace Inkform.Audio
{
    /// <summary>
    /// 事件驱动的音效管理：订阅 Core 事件，用 SFX 音源播放对应占位音。
    /// 每个事件一个 AudioClip 字段（占位，可在 Inspector 替换）。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        [Header("SFX 占位（事件触发）")]
        public AudioClip ScanClip;       // 扫描拟物成功
        public AudioClip RevertClip;     // 还原基态
        public AudioClip AbilityClip;    // 使用能力
        public AudioClip JumpClip;       // 跳跃
        public AudioClip LandClip;       // 落地
        public AudioClip DeathClip;      // 被发现 / 死亡
        public AudioClip RespawnClip;    // 重生
        public AudioClip CheckpointClip; // 抵达检查点
        public AudioClip CompleteClip;   // 通关

        public AudioSource SfxSource;

        void Awake()
        {
            if (SfxSource == null) SfxSource = GetComponent<AudioSource>();
        }

        void OnEnable()
        {
            EventBus.Subscribe<FormMaterialized>(OnScan);
            EventBus.Subscribe<FormDissolved>(OnRevert);
            EventBus.Subscribe<AbilityUsed>(OnAbility);
            EventBus.Subscribe<Jumped>(OnJump);
            EventBus.Subscribe<Landed>(OnLand);
            EventBus.Subscribe<PlayerKilled>(OnDeath);
            EventBus.Subscribe<OnRespawn>(OnRespawn);
            EventBus.Subscribe<CheckpointReached>(OnCheckpoint);
            EventBus.Subscribe<LevelCompleted>(OnComplete);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<FormMaterialized>(OnScan);
            EventBus.Unsubscribe<FormDissolved>(OnRevert);
            EventBus.Unsubscribe<AbilityUsed>(OnAbility);
            EventBus.Unsubscribe<Jumped>(OnJump);
            EventBus.Unsubscribe<Landed>(OnLand);
            EventBus.Unsubscribe<PlayerKilled>(OnDeath);
            EventBus.Unsubscribe<OnRespawn>(OnRespawn);
            EventBus.Unsubscribe<CheckpointReached>(OnCheckpoint);
            EventBus.Unsubscribe<LevelCompleted>(OnComplete);
        }

        void Play(AudioClip clip)
        {
            if (clip != null && SfxSource != null) SfxSource.PlayOneShot(clip);
        }

        void OnScan(FormMaterialized _) => Play(ScanClip);
        void OnRevert(FormDissolved _) => Play(RevertClip);
        void OnAbility(AbilityUsed _) => Play(AbilityClip);
        void OnJump(Jumped _) => Play(JumpClip);
        void OnLand(Landed _) => Play(LandClip);
        void OnDeath(PlayerKilled _) => Play(DeathClip);
        void OnRespawn(OnRespawn _) => Play(RespawnClip);
        void OnCheckpoint(CheckpointReached _) => Play(CheckpointClip);
        void OnComplete(LevelCompleted _) => Play(CompleteClip);
    }
}
