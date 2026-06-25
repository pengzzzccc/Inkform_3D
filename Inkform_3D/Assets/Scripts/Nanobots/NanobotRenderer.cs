using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 渲染层：订阅 NanobotSwarm.OnPositionsUpdated，用 DrawMeshInstanced 把每个 bot
    /// 画成沿运动方向拉丝的金属丝条 —— 「纳米金属洪流」。身体层不知道渲染存在。
    /// 速度由位置差分自派生（不改 Swarm API），按速度拉长、对齐方向；慢则收回近球。
    /// DrawMeshInstanced 单批上限 1023，超出自动分批。
    /// </summary>
    [RequireComponent(typeof(NanobotSwarm))]
    public class NanobotRenderer : MonoBehaviour
    {
        [Header("外观")]
        public Mesh BotMesh;
        public Material BotMaterial;
        [Tooltip("单个 bot 的基础半径（米）。")]
        public float BotRadius = 0.09f;
        [Tooltip("大小随机抖动：0=全一致，0.3=±30%。")]
        [Range(0f, 0.8f)] public float SizeJitter = 0.35f;

        [Header("拉丝（沿速度方向）")]
        [Tooltip("最大拉长倍数：丝条长轴 = 半径 *(1 + 此值 * 速度归一)。")]
        public float StretchAmount = 6f;
        [Tooltip("达到满拉长所需的速度（m/s）。越小越容易拉长。")]
        public float StretchSpeedRef = 6f;
        [Tooltip("低于此速度视为静止，朝向退化为球（避免抖）。")]
        public float MinSpeedForStretch = 0.15f;

        const int BatchMax = 1023;

        NanobotSwarm _swarm;
        Matrix4x4[] _matrices;
        Vector3[] _latest;
        Vector3[] _prev;        // 上一帧位置，用于派生速度
        float[] _sizeMul;       // 每 bot 确定性大小倍数
        bool _hasPrev;

        void Awake()
        {
            _swarm = GetComponent<NanobotSwarm>();
            // 默认用内置 Capsule（局部 Y 长轴，天然适合拉丝）。
            if (BotMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                BotMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            if (BotMaterial == null)
            {
                var sh = Shader.Find("Inkform/NanobotFlow")
                    ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                BotMaterial = new Material(sh) { enableInstancing = true };
            }
            BotMaterial.enableInstancing = true;
        }

        void OnEnable() { _swarm.OnPositionsUpdated += OnPositions; }
        void OnDisable() { _swarm.OnPositionsUpdated -= OnPositions; }

        void OnPositions(Vector3[] positions)
        {
            _latest = positions;
            int n = positions.Length;
            if (_matrices == null || _matrices.Length != n)
            {
                _matrices = new Matrix4x4[n];
                _prev = new Vector3[n];
                _sizeMul = new float[n];
                for (int i = 0; i < n; i++)
                    _sizeMul[i] = 1f + (Hash.Unit(i, 20) - 0.5f) * 2f * SizeJitter;
                _hasPrev = false;
            }
        }

        Matrix4x4[] _batchBuf; // >1023 时的分批缓冲

        void LateUpdate()
        {
            if (_latest == null || _matrices == null) return;

            int n = _latest.Length;
            float invDt = Time.deltaTime > 1e-5f ? 1f / Time.deltaTime : 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = _latest[i];
                Vector3 vel = _hasPrev ? (p - _prev[i]) * invDt : Vector3.zero;
                float speed = vel.magnitude;
                float r = BotRadius * _sizeMul[i];

                Quaternion rot;
                Vector3 scale;
                if (speed > MinSpeedForStretch)
                {
                    rot = Quaternion.FromToRotation(Vector3.up, vel / speed);
                    float speed01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, StretchSpeedRef));
                    float lenMul = 1f + StretchAmount * speed01;
                    scale = new Vector3(r * 2f, r * 2f * lenMul, r * 2f); // 长轴沿局部 Y
                }
                else
                {
                    rot = Quaternion.identity;
                    scale = Vector3.one * (r * 2f);
                }
                _matrices[i] = Matrix4x4.TRS(p, rot, scale);
                _prev[i] = p;
            }
            _hasPrev = true;

            // 常见情况：一批画完。
            if (n <= BatchMax)
            {
                Graphics.DrawMeshInstanced(BotMesh, 0, BotMaterial, _matrices, n);
                return;
            }

            // 超出单批上限：DrawMeshInstanced 无 offset 参数，按批拷贝绘制。
            for (int start = 0; start < n; start += BatchMax)
            {
                int count = Mathf.Min(BatchMax, n - start);
                if (_batchBuf == null || _batchBuf.Length != count) _batchBuf = new Matrix4x4[count];
                System.Array.Copy(_matrices, start, _batchBuf, 0, count);
                Graphics.DrawMeshInstanced(BotMesh, 0, BotMaterial, _batchBuf, count);
            }
        }
    }
}
