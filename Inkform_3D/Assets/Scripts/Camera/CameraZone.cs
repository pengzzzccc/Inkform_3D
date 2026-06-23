using UnityEngine;
using Unity.Cinemachine;

namespace Inkform.GameCamera
{
    /// <summary>
    /// 镜头切换区：玩家进入即提升关联 vcam 的优先级（CinemachineBrain 自动混合切换），
    /// 离开复原。用于 F 大厅等需要专属机位的段落。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CameraZone : MonoBehaviour
    {
        public CinemachineCamera Cam;
        public int ActivePriority = 20;
        public int InactivePriority = 0;

        void Awake()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
            if (Cam != null) Cam.Priority = InactivePriority;
        }

        void OnTriggerEnter(Collider other)
        {
            if (Cam != null && other.CompareTag("Player")) Cam.Priority = ActivePriority;
        }

        void OnTriggerExit(Collider other)
        {
            if (Cam != null && other.CompareTag("Player")) Cam.Priority = InactivePriority;
        }
    }
}
