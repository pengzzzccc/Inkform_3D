using UnityEngine;
using Inkform.Core;

namespace Inkform.Data
{
    /// <summary>
    /// 形态/能力配置（数据驱动）。新增形态 / 调平衡只改资产，不改代码。
    /// </summary>
    [CreateAssetMenu(menuName = "Inkform/Ability Config", fileName = "S_AbilityConfig")]
    public class S_AbilityConfig : ScriptableObject
    {
        public FormId Form = FormId.Core;
        public string DisplayName = "";

        [Tooltip("该形态的移动玩法参数")]
        public MovementProfile Movement = MovementProfile.Default;

        [Header("形态视觉（膨胀重组）")]
        [Tooltip("变身后身体颜色")]
        public Color BodyColor = Color.white;

        [Tooltip("变身后体型缩放（船锚矮胖 / 轻形态细高）")]
        public Vector3 BodyScale = Vector3.one;

        [Tooltip("HUD 用图标（可空）")]
        public Sprite Icon;
    }
}
