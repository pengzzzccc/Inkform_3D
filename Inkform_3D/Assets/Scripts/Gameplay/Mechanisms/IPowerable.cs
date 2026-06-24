using UnityEngine;

namespace Inkform.Gameplay
{
    /// <summary>可被灯泡·电池形态供电的机关。</summary>
    public interface IPowerable
    {
        bool IsPowered { get; }
        void Power();
    }

    /// <summary>可被磁铁形态拉动的金属物。</summary>
    public interface IMagnetic
    {
        void Pull(Transform puller);
    }
}
