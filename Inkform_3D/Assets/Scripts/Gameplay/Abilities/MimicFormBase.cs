using Inkform.Core;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 形态基类：数据驱动（MovementProfile 取自 S_AbilityConfig）。
    /// 默认实现已处理"套用/还原移动参数"，子类只写差异（主动能力等）。
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

        public virtual void OnMaterialize(PlayerContext ctx) => ctx.Motor.ApplyProfile(Movement);
        public virtual void OnDissolve(PlayerContext ctx) => ctx.Motor.ApplyProfile(MovementProfile.Default);
        public virtual void OnUse(PlayerContext ctx) { }
        public virtual void Tick(PlayerContext ctx, float dt) { }
    }
}
