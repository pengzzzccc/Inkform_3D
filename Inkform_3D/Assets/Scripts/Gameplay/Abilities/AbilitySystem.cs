using UnityEngine;
using Inkform.Core;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 持有当前模仿形态，驱动 扫描→重组→还原，转发"使用能力"。
    /// 同一时刻只持有一个形态；扫描新目标即替换。
    /// </summary>
    public class AbilitySystem : MonoBehaviour
    {
        PlayerContext _ctx;
        IMimicForm _current; // null = 基态

        public FormId CurrentForm => _current != null ? _current.Id : FormId.Core;

        public void Init(PlayerContext ctx) => _ctx = ctx;

        /// <summary>扫描一个目标配置 → 缓存蓝图 → 膨胀重组为该形态。</summary>
        public void Scan(S_AbilityConfig config)
        {
            if (config == null || _ctx == null) return;

            _current?.OnDissolve(_ctx);
            _current = Create(config);

            EventBus.Publish(new TargetScanned { Form = config.Form });
            _current.OnMaterialize(_ctx);
            EventBus.Publish(new FormMaterialized { Form = config.Form });
        }

        /// <summary>还原回基态。</summary>
        public void RevertToCore()
        {
            if (_current == null || _ctx == null) return;
            var id = _current.Id;
            _current.OnDissolve(_ctx);
            _current = null;
            EventBus.Publish(new FormDissolved { Form = id });
        }

        public void UseAbility()
        {
            if (_current == null || _ctx == null) return;
            _current.OnUse(_ctx);
            EventBus.Publish(new AbilityUsed { Form = _current.Id });
        }

        void Update() => _current?.Tick(_ctx, Time.deltaTime);

        static IMimicForm Create(S_AbilityConfig c)
        {
            switch (c.Form)
            {
                case FormId.Remote:   return new RemoteForm(c);
                case FormId.Anchor:   return new AnchorForm(c);
                case FormId.Teleport: return new TeleportForm(c);
                default:              return new AnchorForm(c); // 兜底
            }
        }
    }
}
