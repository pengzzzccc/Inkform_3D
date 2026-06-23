using System.Collections;
using UnityEngine;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 形态视觉表现（"膨胀重组"的最小实现）：按 S_AbilityConfig 的 BodyColor/BodyScale
    /// 平滑过渡颜色与体型。规则层通过 PlayerContext.Visual 调用，保持表现与规则解耦。
    /// </summary>
    public class PlayerFormVisual : MonoBehaviour
    {
        [Tooltip("要改色的渲染体（默认取自身 Renderer）")]
        public Renderer BodyRenderer;

        [Tooltip("要缩放的根（默认本物体 transform）")]
        public Transform BodyRoot;

        public Color CoreColor = new Color(0.1f, 0.1f, 0.14f);
        public float TransitionDuration = 0.18f;

        Vector3 _coreScale;
        Material _mat;
        Coroutine _co;

        void Awake()
        {
            if (BodyRenderer == null) BodyRenderer = GetComponentInChildren<Renderer>();
            if (BodyRoot == null) BodyRoot = transform;
            _coreScale = BodyRoot.localScale;
            if (BodyRenderer != null) _mat = BodyRenderer.material; // 实例化，安全改色
            SetColorImmediate(CoreColor);
        }

        public void ApplyForm(S_AbilityConfig cfg)
        {
            if (cfg == null) return;
            StartTransition(cfg.BodyColor, Vector3.Scale(_coreScale, cfg.BodyScale));
        }

        public void ResetToCore()
        {
            StartTransition(CoreColor, _coreScale);
        }

        void StartTransition(Color toColor, Vector3 toScale)
        {
            if (!isActiveAndEnabled) { SetColorImmediate(toColor); if (BodyRoot) BodyRoot.localScale = toScale; return; }
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(Transition(toColor, toScale));
        }

        IEnumerator Transition(Color toColor, Vector3 toScale)
        {
            Color fromColor = _mat != null ? GetColor() : toColor;
            Vector3 fromScale = BodyRoot != null ? BodyRoot.localScale : toScale;
            float t = 0f;
            while (t < TransitionDuration)
            {
                t += Time.deltaTime;
                float k = TransitionDuration <= 0f ? 1f : t / TransitionDuration;
                if (_mat != null) SetColor(Color.Lerp(fromColor, toColor, k));
                if (BodyRoot != null) BodyRoot.localScale = Vector3.Lerp(fromScale, toScale, k);
                yield return null;
            }
            if (_mat != null) SetColor(toColor);
            if (BodyRoot != null) BodyRoot.localScale = toScale;
            _co = null;
        }

        void SetColorImmediate(Color c) { if (_mat != null) SetColor(c); }

        void SetColor(Color c)
        {
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
        }

        Color GetColor()
        {
            if (_mat.HasProperty("_BaseColor")) return _mat.GetColor("_BaseColor");
            if (_mat.HasProperty("_Color")) return _mat.GetColor("_Color");
            return Color.white;
        }
    }
}
