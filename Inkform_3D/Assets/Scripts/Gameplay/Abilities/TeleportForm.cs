using UnityEngine;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 传送形态：膨胀重组时记录标记=当前位置；使用能力瞬移回标记（带冷却）。
    /// GDD 核心三之一——放标记、跨单向断点后瞬回。
    /// </summary>
    public class TeleportForm : MimicFormBase
    {
        public TeleportForm(S_AbilityConfig config) : base(config) { }

        const float Cooldown = 1.5f;
        Vector3 _marker;
        bool _hasMarker;
        float _readyAt;

        public override void OnMaterialize(PlayerContext ctx)
        {
            base.OnMaterialize(ctx);
            _marker = ctx.Transform.position;
            _hasMarker = true;
            _readyAt = Time.time + Cooldown;
        }

        public override void OnUse(PlayerContext ctx)
        {
            if (!_hasMarker || Time.time < _readyAt) return;
            if (ctx.Rigidbody != null)
            {
                ctx.Rigidbody.linearVelocity = Vector3.zero;
                ctx.Rigidbody.position = _marker;
            }
            ctx.Transform.position = _marker;
            _readyAt = Time.time + Cooldown;
        }
    }
}
