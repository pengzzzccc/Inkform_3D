using UnityEngine;
using Inkform.Core;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 形态基类：数据驱动（MovementProfile 取自 S_AbilityConfig）。
    /// 默认实现已处理"套用/还原移动参数 + 形态视觉钩子"，子类只写差异（主动能力等）。
    /// </summary>
    public abstract class MimicFormBase : IMimicForm
    {
        protected readonly S_AbilityConfig Config;

        protected MimicFormBase(S_AbilityConfig config)
        {
            Config = config;
        }

        public FormId Id => Config.Form;
        public MovementProfile Movement => Config.Movement;

        public virtual void OnMaterialize(PlayerContext ctx)
        {
            ctx.Motor.ApplyProfile(Movement);
            ctx.Visual?.ApplyForm(Config); // 形态视觉：膨胀重组
        }

        public virtual void OnDissolve(PlayerContext ctx)
        {
            ctx.Motor.ApplyProfile(MovementProfile.Default);
            ctx.Visual?.ResetToCore();
        }

        public virtual void OnUse(PlayerContext ctx) { }
        public virtual void Tick(PlayerContext ctx, float dt) { }
        public virtual string AimHint(PlayerContext ctx) => null;

        /// <summary>在玩家周围 range 内找最近的某接口/组件实例（用于操控类能力）。</summary>
        protected static T FindNearest<T>(Vector3 from, float range) where T : class
        {
            T best = null;
            float bestSq = range * range;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is T t)
                {
                    float d = (mb.transform.position - from).sqrMagnitude;
                    if (d < bestSq) { bestSq = d; best = t; }
                }
            }
            return best;
        }
    }
}
