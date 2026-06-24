using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>供电点：被灯泡·电池形态供电后开启关联闸门。</summary>
    public class PowerNode : MonoBehaviour, IPowerable, IMechanism
    {
        public SimpleGate Gate;
        public AudioSource Sfx;

        public bool IsPowered { get; private set; }
        public bool IsActive => IsPowered;

        public void Power() => SetActive(true);

        public void SetActive(bool on)
        {
            if (IsPowered == on) return;
            IsPowered = on;
            if (on)
            {
                Gate?.SetActive(true);
                if (Sfx != null) Sfx.Play();
            }
        }

        public void ResetToCheckpoint() => IsPowered = false;
    }
}
