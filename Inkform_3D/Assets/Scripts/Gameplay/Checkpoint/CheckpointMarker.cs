using UnityEngine;
using Inkform.Core;

namespace Inkform.Gameplay
{
    /// <summary>
    /// 检查点的运行时低调标记：默认淡蓝，玩家激活该检查点时高亮为绿色。
    /// 让玩家在游戏中也能大致看到检查点位置（隐式检查点的轻量提示）。
    /// </summary>
    public class CheckpointMarker : MonoBehaviour
    {
        public int Id;
        public Renderer Target;
        public Color Idle = new Color(0.3f, 0.6f, 1f, 0.22f);
        public Color Active = new Color(0.4f, 1f, 0.6f, 0.45f);

        Material _mat;

        void Awake()
        {
            if (Target == null) Target = GetComponentInChildren<Renderer>();
            if (Target != null) _mat = Target.material;
            SetColor(Idle);
        }

        void OnEnable() => EventBus.Subscribe<CheckpointReached>(OnReached);
        void OnDisable() => EventBus.Unsubscribe<CheckpointReached>(OnReached);

        void OnReached(CheckpointReached e) => SetColor(e.Id == Id ? Active : Idle);

        void SetColor(Color c)
        {
            if (_mat == null) return;
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
        }
    }
}
