using System.Collections;
using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 清扫者：周期掠过大厅的庞然威胁。FSM —— 等待(危险关) → 掠过(直线扫过，危险开，
    /// 低频音渐强渐弱) → 等待 … 循环。致死由 Hazard 子物体上的 ScanField 负责（带掩体遮挡）。
    /// </summary>
    public class SweeperController : MonoBehaviour
    {
        public Vector3 StartPos;
        public Vector3 EndPos;
        public float SweepDuration = 6f;
        public float WaitDuration = 3f;

        [Tooltip("致死子物体（含 ScanField），掠过时启用、等待时禁用")]
        public GameObject Hazard;

        [Tooltip("清扫者低频接近音（音量随掠过渐强渐弱）")]
        public AudioSource ApproachSfx;

        public float MaxVolume = 0.7f;

        void Start() => StartCoroutine(Loop());

        IEnumerator Loop()
        {
            while (true)
            {
                // 等待（撤离/危险关闭）
                if (Hazard != null) Hazard.SetActive(false);
                if (ApproachSfx != null) ApproachSfx.volume = 0f;
                transform.position = StartPos;
                yield return new WaitForSeconds(WaitDuration);

                // 掠过（危险开启，直线扫过大厅）
                if (Hazard != null) Hazard.SetActive(true);
                float t = 0f;
                while (t < SweepDuration)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / SweepDuration);
                    transform.position = Vector3.Lerp(StartPos, EndPos, k);
                    if (ApproachSfx != null) ApproachSfx.volume = Mathf.Sin(k * Mathf.PI) * MaxVolume; // 接近→远离
                    yield return null;
                }
                transform.position = EndPos;
            }
        }
    }
}
