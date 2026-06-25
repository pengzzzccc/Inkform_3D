using System;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 身体层：一堆 bot 的运动积分。每帧用 SmoothDamp 把每个 bot 追到当前形态的目标点。
    /// 不关心"为什么"换形态——那是编排层(PossessionDirector)的事；只负责"怎么流动"。
    /// 手感总旋钮 = smoothTime（小=机械硬、大=液态黏）。
    /// 渲染层订阅 OnPositionsUpdated 自行绘制（本类不渲染）。
    /// </summary>
    public class NanobotSwarm : MonoBehaviour
    {
        [Header("规模")]
        [Tooltip("bot 数量。验证阶段 256 足够；上千需改 Jobs+Burst（接口不变）。")]
        public int Count = 256;

        [Header("手感")]
        [Tooltip("SmoothDamp 平滑时间。小=机械硬，大=液态黏。统一控制所有形态质感。")]
        public float SmoothTime = 0.25f;
        [Tooltip("单 bot 最大速度（m/s）。")]
        public float MaxSpeed = 30f;

        [Tooltip("形态进度推进速度（每秒 t 增量）。1 = 约 1 秒走完一个形态。")]
        [SerializeField] float _defaultFormationSpeed = 1f;

        Vector3[] _current;
        Vector3[] _velocity;
        ISwarmFormation _formation;
        float _t;          // 当前形态进度 0→1
        float _speed;      // 当前形态的推进速度

        /// <summary>每帧位置更新后触发，渲染层订阅它绘制 bot。</summary>
        public event Action<Vector3[]> OnPositionsUpdated;

        public Vector3 Centroid { get; private set; }
        public float Progress => _t;
        public bool FormationComplete => _formation != null && _formation.IsComplete(_t);
        public int BotCount => Count;

        void Awake()
        {
            _current = new Vector3[Count];
            _velocity = new Vector3[Count];
            // 初始全部堆在自身位置，避免第一帧从原点飞过来。
            for (int i = 0; i < Count; i++) _current[i] = transform.position;
            Centroid = transform.position;
        }

        /// <summary>切换形态并重置进度。speed≤0 时用默认推进速度。</summary>
        public void SetFormation(ISwarmFormation formation, float speed = -1f)
        {
            _formation = formation;
            _t = 0f;
            _speed = speed > 0f ? speed : _defaultFormationSpeed;
        }

        void Update()
        {
            if (_formation == null) return;

            // 推进形态进度（IsComplete 的形态在 t=1 后不再增长）。
            _t = Mathf.Clamp01(_t + _speed * Time.deltaTime);

            Vector3 sum = Vector3.zero;
            float st = Mathf.Max(0.0001f, SmoothTime);
            float dt = Time.deltaTime;
            for (int i = 0; i < Count; i++)
            {
                Vector3 target = _formation.SampleTarget(i, Count, _t);
                _current[i] = Vector3.SmoothDamp(_current[i], target, ref _velocity[i], st, MaxSpeed, dt);
                sum += _current[i];
            }
            Centroid = sum / Count;

            OnPositionsUpdated?.Invoke(_current);
        }
    }
}
