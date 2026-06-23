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

        // 编辑器场景视图可视化：画出检查点触发体与重生点（OnDrawGizmos 仅在编辑器调用）。
        void OnDrawGizmos()
        {
            var col = GetComponent<BoxCollider>();
            Vector3 size = col != null ? Vector3.Scale(col.size, transform.lossyScale) : Vector3.one * 2f;
            Vector3 center = col != null ? transform.TransformPoint(col.center) : transform.position;

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.18f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(center, size);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(Respawn, 0.2f);
        }
    }
}
