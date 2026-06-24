using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>高处开关：玩家进入其 Trigger（需气球飘到高处）即激活，开启关联闸门。</summary>
    [RequireComponent(typeof(Collider))]
    public class HighSwitch : MonoBehaviour, IMechanism
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
