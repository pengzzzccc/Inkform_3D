using Inkform.Core;
using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>一个"可模仿形态"= 一种能力策略（扫描遥控器→遥控形态…）。</summary>
    public interface IMimicForm
    {
        FormId Id { get; }
        MovementProfile Movement { get; }
        void OnMaterialize(PlayerContext ctx); // 膨胀重组完成、参数生效
        void OnDissolve(PlayerContext ctx);    // 还原回基态
        void OnUse(PlayerContext ctx);         // “使用能力”输入触发
        void Tick(PlayerContext ctx, float dt);

        /// <summary>当前是否有可操作目标在范围内，返回提示文本（null=无）。用于操控类能力的瞄准提示。</summary>
        string AimHint(PlayerContext ctx);
    }
}
