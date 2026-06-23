using UnityEngine;
using Cysharp.Threading.Tasks;

namespace Inkform.UI
{
    /// <summary>全屏淡入淡出（CanvasGroup）。用于死亡重生过渡。</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFader : MonoBehaviour
    {
        CanvasGroup _cg;

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            _cg.alpha = 0f;
        }

        /// <summary>淡到全黑。</summary>
        public UniTask FadeOut(float duration) => Fade(_cg != null ? _cg.alpha : 0f, 1f, duration);

        /// <summary>淡回透明。</summary>
        public UniTask FadeIn(float duration) => Fade(_cg != null ? _cg.alpha : 1f, 0f, duration);

        async UniTask Fade(float from, float to, float duration)
        {
            if (_cg == null) _cg = GetComponent<CanvasGroup>();
            if (duration <= 0f) { _cg.alpha = to; return; }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _cg.alpha = Mathf.Lerp(from, to, t / duration);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            _cg.alpha = to;
        }
    }
}
