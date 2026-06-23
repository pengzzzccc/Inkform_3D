using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    public interface IThreatSource { }

    /// <summary>
    /// 旋转探照灯扫描体：玩家被光束（Trigger）罩住且与光源之间无掩体遮挡 ⇒ 即死。
    /// 本组件挂在"光束 Trigger"物体上；该物体作为旋转父节点(LightOrigin)的子物体随之扫过。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ScanField : MonoBehaviour, IThreatSource
    {
        [Tooltip("旋转父节点（探照灯枢轴）。旋转它即带动光束扫过；也作为遮挡判定的光源点。")]
        public Transform LightOrigin;

        [Tooltip("探照灯旋转速度（度/秒）。0 = 不旋转。")]
        public float RotateSpeed = 40f;

        [Tooltip("掩体层：处于光源与玩家之间则遮挡（豁免即死）。")]
        public LayerMask CoverMask;

        public string Source = "ScanField";

        Transform _pivot;

        void Awake() => _pivot = LightOrigin != null ? LightOrigin : transform;

        void Update()
        {
            if (RotateSpeed != 0f && _pivot != null)
                _pivot.Rotate(Vector3.up, RotateSpeed * Time.deltaTime, Space.World);
        }

        void OnTriggerStay(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            Vector3 lightPos = _pivot != null ? _pivot.position : transform.position;
            if (IsLethal(lightPos, other.transform.position, CoverMask))
                EventBus.Publish(new PlayerKilled { Position = other.transform.position, Source = Source });
        }

        /// <summary>
        /// 纯逻辑（可单测）：光源到玩家之间无 Cover 遮挡 ⇒ 致命。
        /// </summary>
        public static bool IsLethal(Vector3 lightPos, Vector3 playerPos, LayerMask coverMask)
        {
            Vector3 dir = playerPos - lightPos;
            float dist = dir.magnitude;
            if (dist < 0.0001f) return true;
            // 射线在到达玩家前命中 Cover ⇒ 被遮挡 ⇒ 不致命。
            bool blocked = Physics.Raycast(lightPos, dir / dist, dist, coverMask, QueryTriggerInteraction.Ignore);
            return !blocked;
        }
    }
}
