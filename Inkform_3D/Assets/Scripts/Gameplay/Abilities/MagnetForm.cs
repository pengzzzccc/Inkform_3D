using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 磁铁形态：使用能力时拉动半径内最近的金属物（IMagnetic）——拉开金属门 / 吸金属板。
    /// </summary>
    public class MagnetForm : MimicFormBase
    {
        public MagnetForm(S_AbilityConfig config) : base(config) { }

        const float Range = 7f;

        public override void OnUse(PlayerContext ctx)
            => FindNearest<IMagnetic>(ctx.Transform.position, Range)?.Pull(ctx.Transform);

        public override string AimHint(PlayerContext ctx)
            => FindNearest<IMagnetic>(ctx.Transform.position, Range) != null ? "[左键] 拉动金属" : null;
    }
}
