using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 水体：在区域内给玩家施加上浮力（非船锚形态浮于水面、船锚因高质量+负浮力下沉）。
    /// Drain() 降水面并停浮力，露出水下通道。可由 SluiceValve 触发。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class WaterVolume : MonoBehaviour, IResettable
    {
        [Tooltip("水面视觉（半透明蓝），Drain 时下降")]
        public Transform WaterSurface;

        [Tooltip("施加给水中玩家的上浮加速度")]
        public float Buoyancy = 14f;

        [Tooltip("排水后水面下降的高度")]
        public float DrainDrop = 4f;

        [Tooltip("排水动画时长")]
        public float DrainDuration = 1.2f;

        bool _drained;
        Vector3 _surfaceStart;
        float _drainT;

        void Awake()
        {
            if (WaterSurface != null) _surfaceStart = WaterSurface.localPosition;
            GetComponent<BoxCollider>().isTrigger = true;
        }

        void OnTriggerStay(Collider other)
        {
            if (_drained || !other.CompareTag("Player")) return;
            if (other.attachedRigidbody != null)
                other.attachedRigidbody.AddForce(Vector3.up * Buoyancy, ForceMode.Acceleration);
        }

        /// <summary>开始排水：水面下降、停止浮力。</summary>
        public void Drain()
        {
            if (_drained) return;
            _drained = true;
            _drainT = 0f;
        }

        void Update()
        {
            if (!_drained || WaterSurface == null) return;
            if (_drainT < DrainDuration)
            {
                _drainT += Time.deltaTime;
                float k = Mathf.Clamp01(_drainT / DrainDuration);
                WaterSurface.localPosition = _surfaceStart - Vector3.up * (DrainDrop * k);
            }
        }

        public void ResetToCheckpoint()
        {
            _drained = false;
            _drainT = 0f;
            if (WaterSurface != null) WaterSurface.localPosition = _surfaceStart;
        }
    }
}
