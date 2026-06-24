namespace Inkform.Core
{
    /// <summary>
    /// 形态标识。Core(基态) = 未模仿任何目标的纳米机器人基础形态。
    /// 核心三：Remote(遥控)/Anchor(船锚)/Teleport(传送)；
    /// 段落专属三：Bulb(灯泡·电池)/Magnet(磁铁)/Balloon(气球)。
    /// </summary>
    public enum FormId
    {
        Core = 0,
        Remote = 1,
        Anchor = 2,
        Teleport = 3,
        Bulb = 4,
        Magnet = 5,
        Balloon = 6,
    }
}
