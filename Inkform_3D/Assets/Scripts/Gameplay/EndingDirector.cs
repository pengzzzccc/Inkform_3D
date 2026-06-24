using UnityEngine;
using UnityEngine.Playables;
using Cysharp.Threading.Tasks;
using Inkform.Core;
using Inkform.UI;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 结尾过场：玩家到达终点 → 锁输入 + 外界之光渐亮 + 渐隐 + 通关。
    /// 可选 Timeline(PlayableDirector) 作氛围编排；脚本保证结尾可靠。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EndingDirector : MonoBehaviour
    {
        public Light EndLight;
        public ScreenFader Fader;
        public PlayableDirector Director;   // 可选 Timeline
        public float Duration = 4f;
        public float LightFrom = 0.5f;
        public float LightTo = 3.5f;

        bool _done;

        void Awake()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (_done || !other.CompareTag("Player")) return;
            _done = true;
            Run().Forget();
        }

        async UniTaskVoid Run()
        {
            var root = ManagerRoot.Instance;
            root?.InputLock.Acquire();
            root?.State.Set(GameState.Cutscene);

            if (Director != null) Director.Play(); // 可选 Timeline 氛围

            float t = 0f;
            while (t < Duration)
            {
                t += Time.deltaTime;
                if (EndLight != null) EndLight.intensity = Mathf.Lerp(LightFrom, LightTo, t / Duration);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (Fader != null) await Fader.FadeOut(1.2f);
            EventBus.Publish(new LevelCompleted());
        }
    }
}
