using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 危险区视觉脉动：让红色"扫描光/警示带"明显闪动，提示致命区域。
    /// 需挂在使用透明材质的渲染体上。
    /// </summary>
    public class DangerPulse : MonoBehaviour
    {
        public Color BaseColor = new Color(1f, 0.15f, 0.15f, 1f);
        public float Speed = 3f;
        public float MinAlpha = 0.18f;
        public float MaxAlpha = 0.5f;

        Material _mat;

        void Awake()
        {
            var r = GetComponent<Renderer>();
            if (r != null) _mat = r.material;
        }

        void Update()
        {
            if (_mat == null) return;
            float k = Mathf.Sin(Time.time * Speed) * 0.5f + 0.5f;
            float a = Mathf.Lerp(MinAlpha, MaxAlpha, k);
            var c = BaseColor; c.a = a;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
            if (_mat.HasProperty("_EmissionColor")) _mat.SetColor("_EmissionColor", BaseColor * (a * 2f));
        }
    }
}
