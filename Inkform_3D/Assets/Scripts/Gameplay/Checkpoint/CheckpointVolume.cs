using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 分布式隐式检查点：玩家进入即登记为"最近检查点"。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CheckpointVolume : MonoBehaviour
    {
        public int Id;

        [Tooltip("重生点（不设则用本物体位置）")]
        public Transform RespawnPoint;

        public Vector3 Respawn => RespawnPoint != null ? RespawnPoint.position : transform.position;

        void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
                CheckpointSystem.Instance?.Activate(this);
        }
    }
}
