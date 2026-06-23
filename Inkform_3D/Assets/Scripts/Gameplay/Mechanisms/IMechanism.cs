using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>可被激活/复位的机关（水闸阀、集装箱、闸门…）。</summary>
    public interface IMechanism : IResettable
    {
        bool IsActive { get; }
        void SetActive(bool on);
    }

    /// <summary>可被遥控形态操控的机关。</summary>
    public interface IRemoteControllable
    {
        void Operate();
    }
}
