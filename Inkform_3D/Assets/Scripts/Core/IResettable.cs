namespace Inkform.Core
{
    /// <summary>可在重生时复位的元素（可重置机关 / 威胁相位等）。</summary>
    public interface IResettable
    {
        void ResetToCheckpoint();
    }
}
