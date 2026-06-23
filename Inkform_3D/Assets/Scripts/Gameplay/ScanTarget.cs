using UnityEngine;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 场景中可被扫描的目标（替代旧"吞噬点"）。携带一个 S_AbilityConfig；
    /// 玩家进入其 Trigger 范围后按 Interact 即可扫描拟物。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ScanTarget : MonoBehaviour
    {
        public S_AbilityConfig Config;

        public bool PlayerInRange { get; private set; }

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player")) PlayerInRange = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player")) PlayerInRange = false;
        }
    }
}
