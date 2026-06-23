using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 触发开关：玩家进入即激活，开启关联的闸门（用于 F 段远端出口机关）。
    /// 任意形态可触发（区别于只认船锚的 SluiceValve）。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TriggerSwitch : MonoBehaviour, IMechanism
    {
        public SimpleGate Gate;
        public AudioSource Sfx;

        public bool IsActive { get; private set; }

        void Awake()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsActive && other.CompareTag("Player")) SetActive(true);
        }

        public void SetActive(bool on)
        {
            if (IsActive == on) return;
            IsActive = on;
            if (on)
            {
                Gate?.SetActive(true);
                if (Sfx != null) Sfx.Play();
            }
        }

        public void ResetToCheckpoint() => IsActive = false;
    }
}
