using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 水闸阀（水底）：当 Anchor(船锚) 形态的玩家沉到此处即激活，触发关联水体排水。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SluiceValve : MonoBehaviour, IMechanism
    {
        public WaterVolume Water;
        public SimpleGate Gate;      // 激活时开启的水闸门
        public AudioSource Sfx;      // 占位开阀音
        public int PuzzleId = 100;

        public bool IsActive { get; private set; }

        void Awake() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (IsActive || !other.CompareTag("Player")) return;
            var ability = other.GetComponentInParent<AbilitySystem>();
            if (ability == null || ability.CurrentForm != FormId.Anchor) return;
            SetActive(true);
        }

        public void SetActive(bool on)
        {
            if (IsActive == on) return;
            IsActive = on;
            if (on)
            {
                Water?.Drain();
                Gate?.SetActive(true);
                if (Sfx != null) Sfx.Play();
            }
        }

        public void ResetToCheckpoint() => IsActive = false;
    }
}
