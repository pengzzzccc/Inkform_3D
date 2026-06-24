using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 气球·氦气形态：被动飘浮（MovementProfile 正浮力 + 跳高 + 轻质量），
    /// 用于飘过高墙、够到高处开关。无主动能力。
    /// </summary>
    public class BalloonForm : MimicFormBase
    {
        public BalloonForm(S_AbilityConfig config) : base(config) { }
    }
}
