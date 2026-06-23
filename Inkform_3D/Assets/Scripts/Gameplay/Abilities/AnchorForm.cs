using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 船锚形态：重 / 慢 / 跳矮 / 可下沉。
    /// M1 的"移动手感随形态变"主验证形态——差异全部由 MovementProfile 表达。
    /// </summary>
    public class AnchorForm : MimicFormBase
    {
        public AnchorForm(S_AbilityConfig config) : base(config) { }
    }
}
