using Inkform.Data;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 轻形态：轻 / 快 / 跳高，作为与船锚的移动手感对比，便于验证形态切换效果。
    /// </summary>
    public class LightForm : MimicFormBase
    {
        public LightForm(S_AbilityConfig config) : base(config) { }
    }
}
