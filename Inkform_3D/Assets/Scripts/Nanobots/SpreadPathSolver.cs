using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 弧长参数化折线：metacube 沿之生长。Points 为路点，Cum 为累计弧长（单调）。
    /// Sample(dist) 返回该弧长处的位置与切向。构造时去重相邻重合点，保证弧长单调。
    /// </summary>
    public sealed class BotPath
    {
        public readonly Vector3[] Points;
        public readonly float[] Cum;

        public float Length => Cum[Cum.Length - 1];

        public BotPath(List<Vector3> raw)
        {
            var pts = new List<Vector3>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
                if (pts.Count == 0 || (raw[i] - pts[pts.Count - 1]).sqrMagnitude > 1e-8f)
                    pts.Add(raw[i]);
            if (pts.Count == 0) pts.Add(Vector3.zero);
            if (pts.Count == 1) pts.Add(pts[0] + Vector3.up * 0.001f); // 防零长

            Points = pts.ToArray();
            Cum = new float[Points.Length];
            float acc = 0f;
            for (int i = 1; i < Points.Length; i++)
            {
                acc += Vector3.Distance(Points[i - 1], Points[i]);
                Cum[i] = acc;
            }
        }

        public void Sample(float dist, out Vector3 pos, out Vector3 tangent)
        {
            int n = Points.Length;
            float L = Cum[n - 1];
            dist = Mathf.Clamp(dist, 0f, L);

            int lo = 0, hi = n - 1;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (Cum[mid] < dist) lo = mid + 1; else hi = mid; }
            int i1 = Mathf.Clamp(lo, 1, n - 1);
            int i0 = i1 - 1;

            float seg = Cum[i1] - Cum[i0];
            float f = seg > 1e-6f ? (dist - Cum[i0]) / seg : 0f;
            pos = Vector3.Lerp(Points[i0], Points[i1], f);
            Vector3 t = Points[i1] - Points[i0];
            tangent = t.sqrMagnitude > 1e-10f ? t.normalized : Vector3.forward;
        }
    }

    /// <summary>
    /// 蔓延几何求解器（重写自旧 TentacleSolver）。把"源点 → 贴地主干 → 侧分支离干 → 爬上目标表面叶点"
    /// 预解算成每叶一条 <see cref="BotPath"/>。<see cref="SpreadForm"/> 按统一弧长生长前沿驱动 cube 沿之流动。
    /// 纯几何：贴地/贴面靠注入的回调（GroundSnap / SurfaceProject / SourceProject），不直接碰 Unity 物理外的东西。
    /// </summary>
    public sealed class SpreadPathSolver
    {
        public struct Settings
        {
            public float GroundSamplesPerUnit; // 贴地折线每米采样点数
            public int SurfaceSubdiv;          // 爬面段细分（投影回表面防穿模）
            public float DepartMin, DepartMax;  // 侧分支离干分数范围（沿主干 0→1）
            public Func<Vector3, Vector3> GroundSnap;      // 任意点 → 贴地点
            public Func<Vector3, Vector3> SurfaceProject;  // 任意点 → 目标表面最近点
            public Func<Vector3, Vector3> SourceProject;   // 任意点 → 源物体表面最近点（null=单源/蜂群中心）
        }

        public BotPath[] LeafPaths { get; private set; } = Array.Empty<BotPath>();
        public Vector3[] Leaves { get; private set; } = Array.Empty<Vector3>();
        public float MaxLen { get; private set; }
        public int LeafCount => LeafPaths.Length;
        public readonly List<(Vector3 a, Vector3 b)> Edges = new();

        public static SpreadPathSolver Solve(Vector3[] sources, Vector3 targetFoot, Vector3 targetCenter,
            Vector3[] leaves, in Settings s)
        {
            var solver = new SpreadPathSolver();
            int L = leaves != null ? leaves.Length : 0;
            if (L == 0 || sources == null || sources.Length == 0)
            {
                solver.Leaves = leaves ?? Array.Empty<Vector3>();
                return solver;
            }

            float spu = Mathf.Max(0.5f, s.GroundSamplesPerUnit);
            int subdiv = Mathf.Max(1, s.SurfaceSubdiv);
            var groundSnap = s.GroundSnap ?? (p => p);
            var surfProj = s.SurfaceProject ?? (p => p);
            var sourceProj = s.SourceProject; // 可空

            // ① 每叶分配最近源（水平距离）。
            int[] srcOf = new int[L];
            for (int i = 0; i < L; i++)
            {
                int best = 0; float bd = float.MaxValue;
                for (int j = 0; j < sources.Length; j++)
                {
                    float d = Horiz2(sources[j], leaves[i]);
                    if (d < bd) { bd = d; best = j; }
                }
                srcOf[i] = best;
            }

            // ② 每源组内按侧偏量排序分配离干分数：侧偏大的先离干（取 DepartMin）。
            float[] departFrac = new float[L];
            var group = new List<int>();
            for (int j = 0; j < sources.Length; j++)
            {
                Vector3 srcG = groundSnap(sources[j]);
                Vector3 trunkDir = Flat(targetFoot - srcG);
                trunkDir = trunkDir.sqrMagnitude < 1e-6f ? Vector3.forward : trunkDir.normalized;
                Vector3 perp = Vector3.Cross(Vector3.up, trunkDir);

                group.Clear();
                for (int i = 0; i < L; i++) if (srcOf[i] == j) group.Add(i);
                group.Sort((a, b) =>
                    Mathf.Abs(Vector3.Dot(Flat(leaves[b] - srcG), perp))
                    .CompareTo(Mathf.Abs(Vector3.Dot(Flat(leaves[a] - srcG), perp))));
                for (int k = 0; k < group.Count; k++)
                {
                    float f = group.Count <= 1
                        ? s.DepartMin
                        : Mathf.Lerp(s.DepartMin, s.DepartMax, (float)k / (group.Count - 1));
                    departFrac[group[k]] = f;
                }
            }

            // ③ 逐叶建路径：源 →(沿源面下降)→ 贴地主干 → 侧分支贴地 → 爬上叶点。
            var paths = new BotPath[L];
            float maxLen = 0f;
            for (int i = 0; i < L; i++)
            {
                Vector3 src = sources[srcOf[i]];
                Vector3 srcG = groundSnap(src);
                Vector3 leaf = leaves[i];
                Vector3 leafFoot = groundSnap(leaf);
                Vector3 departPt = groundSnap(Vector3.Lerp(srcG, targetFoot, Mathf.Clamp01(departFrac[i])));

                var pts = new List<Vector3>();
                pts.Add(src);

                // 源 → srcG 下降（case B 沿旧物体表面）。
                if ((src - srcG).sqrMagnitude > 0.01f)
                {
                    int ds = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(src, srcG) * spu));
                    for (int k = 1; k <= ds; k++)
                    {
                        float f = (float)k / ds;
                        Vector3 p = Vector3.Lerp(src, srcG, f);
                        if (sourceProj != null && f < 0.7f) p = sourceProj(p);
                        pts.Add(p);
                    }
                }
                pts.Add(srcG);

                AppendGround(pts, srcG, departPt, spu, groundSnap);   // 主干
                AppendGround(pts, departPt, leafFoot, spu, groundSnap); // 侧分支

                // 爬面：leafFoot → leaf，逐点投影回目标表面。
                for (int k = 1; k <= subdiv; k++)
                {
                    float f = (float)k / subdiv;
                    pts.Add(surfProj(Vector3.Lerp(leafFoot, leaf, f)));
                }
                pts.Add(leaf);

                var bp = new BotPath(pts);
                paths[i] = bp;
                if (bp.Length > maxLen) maxLen = bp.Length;
                for (int k = 1; k < bp.Points.Length; k++)
                    solver.Edges.Add((bp.Points[k - 1], bp.Points[k]));
            }

            solver.LeafPaths = paths;
            solver.Leaves = leaves;
            solver.MaxLen = maxLen;
            return solver;
        }

        // 贴地折线：from→to 水平距离按 spu 采样，每点 GroundSnap。
        static void AppendGround(List<Vector3> pts, Vector3 from, Vector3 to, float spu,
            Func<Vector3, Vector3> groundSnap)
        {
            float dist = Flat(to - from).magnitude;
            int n = Mathf.Max(1, Mathf.RoundToInt(dist * spu));
            for (int k = 1; k <= n; k++)
            {
                float f = (float)k / n;
                pts.Add(groundSnap(Vector3.Lerp(from, to, f)));
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float Horiz2(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
