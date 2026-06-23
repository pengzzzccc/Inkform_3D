using UnityEngine;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 遥控形态：使用能力时操控半径内最近的可遥控机关（IRemoteControllable）。
    /// GDD 核心三之一——遥控巨型机械（吊臂/集装箱/闸门…）。
    /// </summary>
    public class RemoteForm : MimicFormBase
    {
        public RemoteForm(S_AbilityConfig config) : base(config) { }

        const float Range = 7f;

        public override void OnUse(PlayerContext ctx)
        {
            IRemoteControllable best = null;
            float bestSq = Range * Range;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IRemoteControllable rc)
                {
                    float d = (mb.transform.position - ctx.Transform.position).sqrMagnitude;
                    if (d < bestSq) { bestSq = d; best = rc; }
                }
            }
            best?.Operate();
        }
    }
}
