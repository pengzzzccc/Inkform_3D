using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// metacube 视觉核心：固定数量（~1000）的方块池，构成围绕附身玩法的呼吸金属质量体。
    /// 每帧把每个 cube 用 SmoothDamp 平滑追到当前形态(<see cref="IMetacubeForm"/>)的目标点，
    /// 并按**确定性相位**脉冲缩放（逐渐变大变小）。
    ///
    /// 渲染两路（<see cref="Mode"/>）：
    ///   · InstancedCubes：DrawMeshInstanced 画离散方块（轻、回退用）。
    ///   · Metaball：把每 cube 当 box-SDF 源，compute 建均匀网格 + 包围盒上 raymarch 平滑并集
    ///     → 相邻方块融成连续块状金属表面（真·metacube）。需 RaymarchMaterial + GridCompute + 支持 compute。
    /// 手感总旋钮 = SmoothTime（小=机械硬、大=液态黏）。
    /// </summary>
    public class MetacubeSystem : MonoBehaviour
    {
        public enum RenderMode { InstancedCubes, Metaball }

        [Header("规模")]
        public int Count = 1000;

        [Header("渲染")]
        [Tooltip("InstancedCubes=离散方块（回退）；Metaball=box-SDF 光线步进融合。")]
        public RenderMode Mode = RenderMode.Metaball;

        [Header("流动手感")]
        [Tooltip("SmoothDamp 平滑时间。小=机械硬，大=液态黏。")]
        public float SmoothTime = 0.25f;
        [Tooltip("单 cube 最大速度（m/s）。")]
        public float MaxSpeed = 30f;
        [Tooltip("形态进度推进速度（每秒 t 增量）。")]
        [SerializeField] float _defaultFormationSpeed = 1f;

        [Header("脉冲（逐渐变大变小）")]
        public float MinCubeSize = 0.06f;
        public float MaxCubeSize = 0.18f;
        public float PulseFreq = 1.5f;
        [Range(0f, 1f)] public float PulseFreqJitter = 0.4f;
        [Range(0f, 0.8f)] public float SizeJitter = 0.3f;

        [Header("旋转（仅 InstancedCubes）")]
        [Tooltip("每 cube 慢速自转角速度（度/秒）。0=不转。")]
        public float SpinSpeed = 18f;

        [Header("InstancedCubes 外观")]
        public Mesh CubeMesh;
        public Material CubeMaterial;

        [Header("Metaball（光线步进融合）")]
        [Tooltip("box-SDF 平滑并集圆角/融合强度（米）。大=更圆更融。")]
        public float SmoothK = 0.06f;
        [Tooltip("加速网格分辨率（每轴格数）。大=更准更贵。")]
        public int GridRes = 32;
        [Tooltip("每格 box 容量上限（溢出截断）。密集区可调大。")]
        public int MaxCubesPerCell = 24;
        [Tooltip("单条光线最大步进次数。")]
        public int RayMaxSteps = 96;
        [Tooltip("raymarch 材质（Inkform/MetacubeRaymarch）。留空则回退 InstancedCubes。")]
        public Material RaymarchMaterial;
        [Tooltip("网格构建 compute（MetacubeGrid.compute）。留空则回退 InstancedCubes。")]
        public ComputeShader GridCompute;
        public Color MetaballColor = new Color(0.7f, 0.78f, 0.9f);
        public Color MetaballEmission = new Color(0.10f, 0.18f, 0.30f);

        const int BatchMax = 1023;

        [StructLayout(LayoutKind.Sequential)]
        struct GpuCube { public Vector3 pos; public float halfExtent; }

        Vector3[] _current;
        Vector3[] _velocity;
        float[] _phase;
        float[] _freqJit;
        float[] _sizeMul;
        Vector3[] _spinAxis;
        float[] _spinSign;
        Matrix4x4[] _matrices;
        Matrix4x4[] _batchBuf;

        // Metaball GPU 资源
        GpuCube[] _gpuCubes;
        GraphicsBuffer _cubeBuf;
        GraphicsBuffer _cellCount;
        GraphicsBuffer _cellItems;
        int _kClear = -1, _kScatter = -1;
        int _allocCubeN, _allocCells, _allocItems;

        IMetacubeForm _form;
        float _t;
        float _speed;
        bool _warnedFallback;

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
                _current[i] = p;
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

            _t = Mathf.Clamp01(_t + _speed * Time.deltaTime);

            float fst = _form.FollowSmoothTime;
            float st = Mathf.Max(0.0001f, fst >= 0f ? fst : SmoothTime);
            float dt = Time.deltaTime;
            float time = Time.time;
            int n = Count;

            bool metaOk = Mode == RenderMode.Metaball && RaymarchMaterial != null
                          && GridCompute != null && SystemInfo.supportsComputeShaders;
            if (Mode == RenderMode.Metaball && !metaOk && !_warnedFallback)
            {
                _warnedFallback = true;
                Debug.LogWarning("[MetacubeSystem] Metaball 资源缺失或不支持 compute，回退 InstancedCubes。", this);
            }
            if (metaOk) EnsureMetaBuffers(n);

            Vector3 sum = Vector3.zero;
            Vector3 minB = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maxB = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < n; i++)
            {
                Vector3 target = _form.SampleTarget(i, n, _t);
                _current[i] = Vector3.SmoothDamp(_current[i], target, ref _velocity[i], st, MaxSpeed, dt);
                sum += _current[i];

                float pulse01 = 0.5f + 0.5f * Mathf.Sin(time * PulseFreq * _freqJit[i] + _phase[i]);
                float size = Mathf.Lerp(MinCubeSize, MaxCubeSize, pulse01) * _sizeMul[i];

                if (metaOk)
                {
                    _gpuCubes[i].pos = _current[i];
                    _gpuCubes[i].halfExtent = size * 0.5f;
                    minB = Vector3.Min(minB, _current[i]);
                    maxB = Vector3.Max(maxB, _current[i]);
                }
                else
                {
                    Quaternion rot = SpinSpeed != 0f
                        ? Quaternion.AngleAxis(time * SpinSpeed * _spinSign[i], _spinAxis[i])
                        : Quaternion.identity;
                    _matrices[i] = Matrix4x4.TRS(_current[i], rot, new Vector3(size, size, size));
                }
            }
            Centroid = sum / n;
            OnPositionsUpdated?.Invoke(_current);

            if (metaOk) RenderMetaball(n, minB, maxB);
            else DrawBatched(n);
        }

        // ───────────────── InstancedCubes ─────────────────
        void DrawBatched(int n)
        {
            if (n <= BatchMax)
            {
                Graphics.DrawMeshInstanced(CubeMesh, 0, CubeMaterial, _matrices, n);
                return;
            }
            for (int start = 0; start < n; start += BatchMax)
            {
                int count = Mathf.Min(BatchMax, n - start);
                if (_batchBuf == null || _batchBuf.Length != count) _batchBuf = new Matrix4x4[count];
                Array.Copy(_matrices, start, _batchBuf, 0, count);
                Graphics.DrawMeshInstanced(CubeMesh, 0, CubeMaterial, _batchBuf, count);
            }
        }

        // ───────────────── Metaball ─────────────────
        void EnsureMetaBuffers(int n)
        {
            if (_kClear < 0 && GridCompute != null)
            {
                _kClear = GridCompute.FindKernel("Clear");
                _kScatter = GridCompute.FindKernel("Scatter");
            }
            int cells = GridRes * GridRes * GridRes;
            int items = cells * Mathf.Max(1, MaxCubesPerCell);

            if (_gpuCubes == null || _allocCubeN != n)
            {
                _gpuCubes = new GpuCube[n];
                _cubeBuf?.Release();
                _cubeBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, Marshal.SizeOf<GpuCube>());
                _allocCubeN = n;
            }
            if (_cellCount == null || _allocCells != cells)
            {
                _cellCount?.Release();
                _cellCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, sizeof(int));
                _allocCells = cells;
            }
            if (_cellItems == null || _allocItems != items)
            {
                _cellItems?.Release();
                _cellItems = new GraphicsBuffer(GraphicsBuffer.Target.Structured, items, sizeof(int));
                _allocItems = items;
            }
        }

        void RenderMetaball(int n, Vector3 minB, Vector3 maxB)
        {
            _cubeBuf.SetData(_gpuCubes, 0, 0, n);

            float bound = MaxCubeSize * 0.5f + SmoothK + 0.02f;
            minB -= Vector3.one * bound;
            maxB += Vector3.one * bound;
            Vector3 center = (minB + maxB) * 0.5f;
            Vector3 sz = maxB - minB;
            float side = Mathf.Max(0.1f, Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)));
            Vector3 gridMin = center - Vector3.one * (side * 0.5f);
            float cellSize = side / GridRes;
            int cells = GridRes * GridRes * GridRes;
            float influence = SmoothK + 0.02f;

            // 建网格
            GridCompute.SetInt("_GridRes", GridRes);
            GridCompute.SetInt("_MaxPerCell", MaxCubesPerCell);
            GridCompute.SetInt("_Count", n);
            GridCompute.SetFloat("_CellSize", cellSize);
            GridCompute.SetFloat("_Influence", influence);
            GridCompute.SetVector("_GridMin", gridMin);

            GridCompute.SetBuffer(_kClear, "_CellCount", _cellCount);
            GridCompute.Dispatch(_kClear, Mathf.CeilToInt(cells / 64f), 1, 1);

            GridCompute.SetBuffer(_kScatter, "_Cubes", _cubeBuf);
            GridCompute.SetBuffer(_kScatter, "_CellCount", _cellCount);
            GridCompute.SetBuffer(_kScatter, "_CellItems", _cellItems);
            GridCompute.Dispatch(_kScatter, Mathf.CeilToInt(n / 64f), 1, 1);

            // raymarch 材质
            RaymarchMaterial.SetBuffer("_Cubes", _cubeBuf);
            RaymarchMaterial.SetBuffer("_CellCount", _cellCount);
            RaymarchMaterial.SetBuffer("_CellItems", _cellItems);
            RaymarchMaterial.SetInt("_GridRes", GridRes);
            RaymarchMaterial.SetInt("_MaxPerCell", MaxCubesPerCell);
            RaymarchMaterial.SetInt("_Count", n);
            RaymarchMaterial.SetInt("_MaxSteps", RayMaxSteps);
            RaymarchMaterial.SetFloat("_CellSize", cellSize);
            RaymarchMaterial.SetFloat("_SmoothK", Mathf.Max(1e-4f, SmoothK));
            RaymarchMaterial.SetVector("_GridMin", gridMin);
            RaymarchMaterial.SetColor("_BaseColor", MetaballColor);
            RaymarchMaterial.SetColor("_EmissionColor", MetaballEmission);

            Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * side);
            Graphics.DrawMesh(CubeMesh, m, RaymarchMaterial, gameObject.layer);
        }

        void OnDisable()
        {
            _cubeBuf?.Release(); _cubeBuf = null;
            _cellCount?.Release(); _cellCount = null;
            _cellItems?.Release(); _cellItems = null;
            _allocCubeN = _allocCells = _allocItems = 0;
        }
    }
}
