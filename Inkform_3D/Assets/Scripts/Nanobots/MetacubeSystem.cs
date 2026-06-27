using System;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// metacube 视觉核心：固定数量（~1000）的方块池，构成围绕附身玩法的呼吸金属质量体。
    /// 每帧把每个 cube 用 SmoothDamp 平滑追到当前形态(<see cref="IMetacubeForm"/>)的目标点，
    /// 并按**确定性相位**脉冲缩放（逐渐变大变小），最后 GPU 实例化（DrawMeshInstanced）一次性绘制。
    /// 形态本身无状态、可随意替换——"为什么换形态"是编排层(PossessionDirector)的事。
    /// 手感总旋钮 = SmoothTime（小=机械硬、大=液态黏）。
    /// </summary>
    public class MetacubeSystem : MonoBehaviour
    {
        [Header("规模")]
        [Tooltip("cube 数量。≤1023 时单批绘制；更大需分批或上 Jobs/Burst（接口不变）。")]
        public int Count = 1000;

        [Header("流动手感")]
        [Tooltip("SmoothDamp 平滑时间。小=机械硬，大=液态黏。统一控制所有形态质感。")]
        public float SmoothTime = 0.25f;
        [Tooltip("单 cube 最大速度（m/s）。")]
        public float MaxSpeed = 30f;
        [Tooltip("形态进度推进速度（每秒 t 增量）。1 ≈ 1 秒走完一个形态。")]
        [SerializeField] float _defaultFormationSpeed = 1f;

        [Header("脉冲（逐渐变大变小）")]
        [Tooltip("cube 最小边长（米）。")]
        public float MinCubeSize = 0.06f;
        [Tooltip("cube 最大边长（米）。")]
        public float MaxCubeSize = 0.18f;
        [Tooltip("脉冲基础频率（Hz 量级）。")]
        public float PulseFreq = 1.5f;
        [Tooltip("每 cube 频率抖动：0=全同步、0.4=±40%，错相位更有机。")]
        [Range(0f, 1f)] public float PulseFreqJitter = 0.4f;
        [Tooltip("每 cube 整体大小抖动：0=全一致、0.3=±30%，碎块大小不一更有机。")]
        [Range(0f, 0.8f)] public float SizeJitter = 0.3f;

        [Header("旋转")]
        [Tooltip("每 cube 慢速自转角速度（度/秒），增加金属碎块的生命感。0=不转。")]
        public float SpinSpeed = 18f;

        [Header("外观")]
        [Tooltip("留空则用内置 Cube 网格。")]
        public Mesh CubeMesh;
        [Tooltip("留空则建一个 URP Lit 金属实例化材质。")]
        public Material CubeMaterial;

        const int BatchMax = 1023;

        Vector3[] _current;
        Vector3[] _velocity;
        float[] _phase;          // 每 cube 脉冲相位
        float[] _freqJit;        // 每 cube 频率倍率
        float[] _sizeMul;        // 每 cube 整体大小倍率
        Vector3[] _spinAxis;     // 每 cube 自转轴
        float[] _spinSign;       // 每 cube 自转方向 ±1
        Matrix4x4[] _matrices;
        Matrix4x4[] _batchBuf;   // >1023 时分批缓冲

        IMetacubeForm _form;
        float _t;                // 当前形态进度 0→1
        float _speed;            // 当前形态推进速度

        /// <summary>每帧位置更新后触发（供未来叠加层订阅；本类已自绘）。</summary>
        public event Action<Vector3[]> OnPositionsUpdated;

        public Vector3 Centroid { get; private set; }
        public float Progress => _t;
        public bool FormComplete => _form != null && _form.IsComplete(_t);
        public int BotCount => Count;

        void Awake()
        {
            Alloc();

            if (CubeMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                CubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            if (CubeMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                CubeMaterial = new Material(sh) { enableInstancing = true };
                if (CubeMaterial.HasProperty("_Metallic")) CubeMaterial.SetFloat("_Metallic", 0.85f);
                if (CubeMaterial.HasProperty("_Smoothness")) CubeMaterial.SetFloat("_Smoothness", 0.75f);
                if (CubeMaterial.HasProperty("_BaseColor")) CubeMaterial.SetColor("_BaseColor", new Color(0.7f, 0.78f, 0.9f));
                // 暗场景里高金属度易发黑，给一点冷色自发光保底。
                CubeMaterial.EnableKeyword("_EMISSION");
                if (CubeMaterial.HasProperty("_EmissionColor"))
                    CubeMaterial.SetColor("_EmissionColor", new Color(0.10f, 0.18f, 0.30f));
            }
            CubeMaterial.enableInstancing = true;
        }

        void Alloc()
        {
            int n = Mathf.Max(1, Count);
            Count = n;
            _current = new Vector3[n];
            _velocity = new Vector3[n];
            _phase = new float[n];
            _freqJit = new float[n];
            _sizeMul = new float[n];
            _spinAxis = new Vector3[n];
            _spinSign = new float[n];
            _matrices = new Matrix4x4[n];

            Vector3 p = transform.position;
            for (int i = 0; i < n; i++)
            {
                _current[i] = p; // 初始堆在自身位置，避免第一帧从原点飞过来
                _phase[i] = Hash.Unit(i, 20) * Mathf.PI * 2f;
                _freqJit[i] = 1f + (Hash.Unit(i, 21) - 0.5f) * 2f * PulseFreqJitter;
                _sizeMul[i] = 1f + (Hash.Unit(i, 23) - 0.5f) * 2f * SizeJitter;
                _spinAxis[i] = Hash.Direction(i);
                _spinSign[i] = Hash.Unit(i, 22) < 0.5f ? -1f : 1f;
            }
            Centroid = p;
        }

        /// <summary>切换形态并重置进度。speed≤0 时用默认推进速度。</summary>
        public void SetForm(IMetacubeForm form, float speed = -1f)
        {
            _form = form;
            _t = 0f;
            _speed = speed > 0f ? speed : _defaultFormationSpeed;
        }

        void LateUpdate()
        {
            if (_form == null || _current == null) return;

            // 推进形态进度（IsComplete 的形态在 t=1 后不再增长）。
            _t = Mathf.Clamp01(_t + _speed * Time.deltaTime);

            // 跟随平滑时间：形态可覆盖（附身贴面用很小值近乎刚性，避免移动拖尾）；<0 用系统默认。
            float fst = _form.FollowSmoothTime;
            float st = Mathf.Max(0.0001f, fst >= 0f ? fst : SmoothTime);
            float dt = Time.deltaTime;
            float time = Time.time;
            int n = Count;
            Vector3 sum = Vector3.zero;

            for (int i = 0; i < n; i++)
            {
                Vector3 target = _form.SampleTarget(i, n, _t);
                _current[i] = Vector3.SmoothDamp(_current[i], target, ref _velocity[i], st, MaxSpeed, dt);
                sum += _current[i];

                // 脉冲：确定性相位 + 频率抖动 → 逐渐变大变小；叠加每 cube 大小抖动。
                float pulse01 = 0.5f + 0.5f * Mathf.Sin(time * PulseFreq * _freqJit[i] + _phase[i]);
                float size = Mathf.Lerp(MinCubeSize, MaxCubeSize, pulse01) * _sizeMul[i];

                Quaternion rot = SpinSpeed != 0f
                    ? Quaternion.AngleAxis(time * SpinSpeed * _spinSign[i], _spinAxis[i])
                    : Quaternion.identity;

                _matrices[i] = Matrix4x4.TRS(_current[i], rot, new Vector3(size, size, size));
            }
            Centroid = sum / n;

            OnPositionsUpdated?.Invoke(_current);
            DrawBatched(n);
        }

        void DrawBatched(int n)
        {
            if (n <= BatchMax)
            {
                Graphics.DrawMeshInstanced(CubeMesh, 0, CubeMaterial, _matrices, n);
                return;
            }
            // 超出单批上限：DrawMeshInstanced 无 offset 参数，按批拷贝绘制。
            for (int start = 0; start < n; start += BatchMax)
            {
                int count = Mathf.Min(BatchMax, n - start);
                if (_batchBuf == null || _batchBuf.Length != count) _batchBuf = new Matrix4x4[count];
                Array.Copy(_matrices, start, _batchBuf, 0, count);
                Graphics.DrawMeshInstanced(CubeMesh, 0, CubeMaterial, _batchBuf, count);
            }
        }
    }
}
