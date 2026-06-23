using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>掉落/深渊致死区：玩家进入即死（复用现有重生流程）。</summary>
    [RequireComponent(typeof(Collider))]
    public class KillVolume : MonoBehaviour
    {
        public string Source = "Fall";

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
                EventBus.Publish(new PlayerKilled { Position = other.transform.position, Source = Source });
        }
    }
}
