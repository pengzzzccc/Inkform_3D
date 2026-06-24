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
            => FindNearest<IRemoteControllable>(ctx.Transform.position, Range)?.Operate();

        public override string AimHint(PlayerContext ctx)
            => FindNearest<IRemoteControllable>(ctx.Transform.position, Range) != null ? "[左键] 操控机关" : null;
    }
}
