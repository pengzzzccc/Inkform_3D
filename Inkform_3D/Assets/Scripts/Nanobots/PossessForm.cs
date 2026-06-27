using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 附身稳态（Possessed）：metacube 贴在**移动中的**附身物体表面跟随。
    /// 表面点以物体**局部空间**存储，每帧 obj.TransformPoint 还原 → 物体随驾驶移动时 cube 整体跟随。
    /// 叠加极小 idle 摆动。局部点用蔓延末态的同一批表面叶子反变换得到 → 附身瞬间无缝衔接。
    ///
    /// 子形态 <see cref="Mode"/>：
    ///   · Cover（全覆盖，本期实现）：cube 直接铺在表面采样点上。
    ///   · Strap（固定轨迹捆绑，未来）：cube 沿若干环绕轨迹排布——接口预留，暂回退到 Cover。
    /// </summary>
    public sealed class PossessForm : IMetacubeForm
    {
        public enum Mode { Cover, Strap }

        readonly Transform _obj;
        readonly Vector3[] _local;
        readonly float _wobble;
        readonly Mode _mode;
        readonly float _followSmooth;
        static bool _strapWarned;

        public PossessForm(Transform obj, Vector3[] localPoints, float wobble = 0.04f,
            Mode mode = Mode.Cover, float followSmooth = 0.03f)
        {
            _obj = obj;
            _local = (localPoints != null && localPoints.Length > 0) ? localPoints : new[] { Vector3.zero };
            _wobble = wobble;
            _mode = mode;
            _followSmooth = followSmooth;

            if (_mode == Mode.Strap && !_strapWarned)
            {
                _strapWarned = true;
                Debug.LogWarning("[PossessForm] Strap（固定轨迹捆绑）尚未实现，暂回退到全覆盖 Cover。");
            }
        }

        public Vector3 SampleTarget(int i, int count, float t)
        {
            // Strap 暂回退 Cover：行为同全覆盖（未来在此分支生成环绕轨迹点）。
            Vector3 world = _obj != null
                ? _obj.TransformPoint(_local[i % _local.Length])
                : _local[i % _local.Length];

            if (_wobble > 0f)
            {
                float nz = Mathf.PerlinNoise(Hash.Unit(i, 15) * 10f, Time.time * 0.7f) - 0.5f;
                world += Hash.Direction(i) * (nz * _wobble);
            }
            return world;
        }

        public bool IsComplete(float t) => false; // 常驻

        // 近乎刚性跟随：贴死在移动物体上，避免驾驶时拖尾。
        public float FollowSmoothTime => _followSmooth;
    }
}
