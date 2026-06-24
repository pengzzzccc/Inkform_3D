using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 灯泡·电池形态：持有时自身发光（照亮暗区 / 触发光敏开关）；
    /// 使用能力给范围内最近的供电点（IPowerable）供电。
    /// </summary>
    public class BulbForm : MimicFormBase
    {
        public BulbForm(S_AbilityConfig config) : base(config) { }

        const float Range = 6f;

        public override void OnMaterialize(PlayerContext ctx)
        {
            base.OnMaterialize(ctx);
            if (ctx.Glow != null) ctx.Glow.enabled = true;
        }

        public override void OnDissolve(PlayerContext ctx)
        {
            base.OnDissolve(ctx);
            if (ctx.Glow != null) ctx.Glow.enabled = false;
        }

        public override void OnUse(PlayerContext ctx)
            => FindNearest<IPowerable>(ctx.Transform.position, Range)?.Power();

        public override string AimHint(PlayerContext ctx)
            => FindNearest<IPowerable>(ctx.Transform.position, Range) != null ? "[左键] 供电" : null;
    }
}
