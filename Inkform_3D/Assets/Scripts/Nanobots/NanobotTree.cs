using System;
using System.Collections.Generic;
using UnityEngine;

namespace Inkform.Nanobots
{
    /// <summary>
    /// 一条预解算的 bot 路径：root→叶 折线，按绝对弧长采样。
    /// 同时携带每点"离面高度"（抖动衰减用）。
    /// </summary>
    public sealed class BotPath
    {
        public readonly Vector3[] Points;
        public readonly float[] Cum;    // 累计弧长
        public readonly float[] Height; // 每点离面高度（表面段=0）

        public float Length => Cum.Length > 0 ? Cum[Cum.Length - 1] : 0f;

        public BotPath(Vector3[] points, float[] heights)
        {
            Points = points;
            Height = heights;
            Cum = new float[points.Length];
            float acc = 0f;
            Cum[0] = 0f;
            for (int k = 1; k < points.Length; k++)
            {
                acc += Vector3.Distance(points[k - 1], points[k]);
                Cum[k] = acc;
            }
        }

        /// <summary>按绝对弧长 dist 采样位置、离面高度、前进切线。</summary>
        public void Sample(float dist, out Vector3 pos, out float height, out Vector3 tangent)
        {
            int n = Points.Length;
            if (n == 1) { pos = Points[0]; height = Height[0]; tangent = Vector3.forward; return; }

            float total = Cum[n - 1];
            if (dist <= 0f)
            {
                pos = Points[0]; height = Height[0];
                tangent = SafeDir(Points[1] - Points[0]); return;
            }
            if (dist >= total)
            {
                pos = Points[n - 1]; height = Height[n - 1];
                tangent = SafeDir(Points[n - 1] - Points[n - 2]); return;
            }

            int k = LowerBound(Cum, dist); // 第一个 Cum[k] >= dist → 段 [k-1,k]
            float segLen = Cum[k] - Cum[k - 1];
            float f = segLen > 1e-6f ? (dist - Cum[k - 1]) / segLen : 0f;
            pos = Vector3.Lerp(Points[k - 1], Points[k], f);
            height = Mathf.Lerp(Height[k - 1], Height[k], f);
            tangent = SafeDir(Points[k] - Points[k - 1]);
        }

        static Vector3 SafeDir(Vector3 v) => v.sqrMagnitude > 1e-10f ? v.normalized : Vector3.forward;

        static int LowerBound(float[] cum, float value)
        {
            int lo = 1, hi = cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    /// <summary>
    /// 预解算的纳米机器人"生长树"：触手伸出前一次性算好的树状分形结构。
    ///
    /// 结构 = 起点 A → 共享树干 → 递归二分的分叉 → 表面上均匀分布的叶子末梢。
    /// 浅层（depth &lt; airBranchDepth）节点沿"离物体中心向外"方向抬出 → 空中树状伸展；
    /// 深层节点贴面 + 表面段细分投影回最近表面点 → 表面分形蔓延、不切入物体。
    ///
    /// 设计铁律（与旧 PathFlowFormation 相反）：树**只发散不汇聚**。每条 root→叶 路径
    /// 一旦在某分叉点与兄弟分开，之后绝不再共享同一点 → 分支分离后不回聚。
    ///
    /// 不穿模：递归**沿最长轴按中位数二分**，兄弟子树占据不相交的空间半区，
    /// 因此同父分支射向不相交区域、整条子树互不交叉。
    ///
    /// 纯逻辑、确定性、无 Unity 运行时依赖（投影委托由调用方注入）→ 可 EditMode 单测。
    /// </summary>
    public sealed class NanobotTree
    {
        /// <summary>每个叶子末梢的 root→叶 路径。bot 按 i % LeafCount 分摊到这些路径。</summary>
        public BotPath[] LeafPaths { get; private set; } = Array.Empty<BotPath>();

        /// <summary>叶子末梢的表面终点（== 均匀采样点），供验收/调试。</summary>
        public Vector3[] Leaves { get; private set; } = Array.Empty<Vector3>();

        /// <summary>所有 bot 路径中的最长弧长，生长前沿用它统一推进。</summary>
        public float MaxLen { get; private set; }

        /// <summary>分叉线段（父→子），仅供 Gizmos 可视化。</summary>
        public List<(Vector3 a, Vector3 b)> Edges { get; } = new();

        public int LeafCount => LeafPaths.Length;

        /// <summary>抬出高度基准，formation 用它把"离面高度"归一到 [0,1] 做抖动衰减。</summary>
        public float OutwardLift { get; private set; }

        sealed class Node
        {
            public Vector3 Pos;
            public float Height;   // 离面高度（抬出量）；表面节点=0
            public Node Left, Right;
            public int LeafIndex = -1;
            public bool IsLeaf => Left == null && Right == null;
        }

        // 构建期上下文（避免层层传参）。
        int _surfaceSubdiv;
        Func<Vector3, Vector3> _project;

        /// <summary>构建生长树。参数见字段注释。</summary>
        public static NanobotTree Build(
            Vector3 start,
            Vector3[] leafPoints,
            Vector3 surfaceCenter,
            int airBranchDepth = 3,
            float outwardLift = 2.5f,
            int surfaceSubdiv = 2,
            Func<Vector3, Vector3> projectToSurface = null)
        {
            var tree = new NanobotTree
            {
                OutwardLift = Mathf.Max(0f, outwardLift),
                _surfaceSubdiv = Mathf.Max(0, surfaceSubdiv),
                _project = projectToSurface,
            };

            int n = leafPoints != null ? leafPoints.Length : 0;
            if (n == 0) { tree.MaxLen = 0f; return tree; }

            tree.Leaves = (Vector3[])leafPoints.Clone();

            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            Node root = tree.BuildNode(leafPoints, surfaceCenter, idx, 0, n, 0,
                Mathf.Max(0, airBranchDepth), tree.OutwardLift);

            // 从 A 出发的共享树干。A 离面高度取 root 高度（视为空中段）。
            tree.Edges.Add((start, root.Pos));
            tree.LeafPaths = new BotPath[n];
            var posStack = new List<Vector3> { start };
            var hStack = new List<float> { root.Height };
            tree.Collect(root, posStack, hStack);

            float maxLen = 0f;
            for (int i = 0; i < n; i++)
                if (tree.LeafPaths[i] != null)
                    maxLen = Mathf.Max(maxLen, tree.LeafPaths[i].Length);
            tree.MaxLen = Mathf.Max(1e-4f, maxLen);
            return tree;
        }

        // 对 indices[lo,hi) 子集在给定 depth 递归二分。
        Node BuildNode(Vector3[] pts, Vector3 center, int[] indices, int lo, int hi,
            int depth, int airBranchDepth, float outwardLift)
        {
            int len = hi - lo;
            if (len == 1)
            {
                int idx = indices[lo];
                return new Node { Pos = pts[idx], Height = 0f, LeafIndex = idx };
            }

            // 子集包围盒 + 质心 → 最长轴。
            Vector3 min = pts[indices[lo]], max = min, centroid = Vector3.zero;
            for (int k = lo; k < hi; k++)
            {
                Vector3 p = pts[indices[k]];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                centroid += p;
            }
            centroid /= len;
            Vector3 ext = max - min;
            int axis = (ext.x >= ext.y && ext.x >= ext.z) ? 0 : (ext.y >= ext.z ? 1 : 2);

            // 沿最长轴中位二分（确定性 → 兄弟子树空间不相交，杜绝穿插）。
            Array.Sort(indices, lo, len, new AxisComparer(pts, axis));
            int mid = lo + len / 2;

            Node left = BuildNode(pts, center, indices, lo, mid, depth + 1, airBranchDepth, outwardLift);
            Node right = BuildNode(pts, center, indices, mid, hi, depth + 1, airBranchDepth, outwardLift);

            // 分叉点 = 子集 medoid（离质心最近的表面点 → 必在表面上）。
            int medoid = indices[lo];
            float bestSqr = (pts[medoid] - centroid).sqrMagnitude;
            for (int k = lo + 1; k < hi; k++)
            {
                float d = (pts[indices[k]] - centroid).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; medoid = indices[k]; }
            }
            Vector3 basePos = pts[medoid];

            // 浅层向外抬出 → 空中分支；深层 lift=0 → 贴面。
            float lift = airBranchDepth > 0
                ? outwardLift * Mathf.Max(0f, (airBranchDepth - depth) / (float)airBranchDepth)
                : 0f;
            Vector3 dir = basePos - center;
            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            Vector3 pos = basePos + dir * lift;

            Edges.Add((pos, left.Pos));
            Edges.Add((pos, right.Pos));
            return new Node { Pos = pos, Height = lift, Left = left, Right = right };
        }

        sealed class AxisComparer : IComparer<int>
        {
            readonly Vector3[] _pts; readonly int _axis;
            public AxisComparer(Vector3[] pts, int axis) { _pts = pts; _axis = axis; }
            public int Compare(int x, int y) => _pts[x][_axis].CompareTo(_pts[y][_axis]);
        }

        // DFS：把 root→叶 的位置/高度栈在叶子处固化成 BotPath。
        void Collect(Node node, List<Vector3> posStack, List<float> hStack)
        {
            posStack.Add(node.Pos);
            hStack.Add(node.Height);

            if (node.IsLeaf)
                LeafPaths[node.LeafIndex] = BuildPath(posStack, hStack);
            else
            {
                Collect(node.Left, posStack, hStack);
                Collect(node.Right, posStack, hStack);
            }

            posStack.RemoveAt(posStack.Count - 1);
            hStack.RemoveAt(hStack.Count - 1);
        }

        // 把折线固化成 BotPath：对"两端都贴面"的段细分并投影回表面（贴面蔓延、不穿模）。
        BotPath BuildPath(List<Vector3> positions, List<float> heights)
        {
            var outP = new List<Vector3>(positions.Count * (1 + _surfaceSubdiv)) { positions[0] };
            var outH = new List<float>(outP.Capacity) { heights[0] };

            for (int s = 1; s < positions.Count; s++)
            {
                Vector3 p0 = positions[s - 1], p1 = positions[s];
                float h0 = heights[s - 1], h1 = heights[s];

                bool surfSeg = _surfaceSubdiv > 0 && _project != null
                    && h0 <= 1e-4f && h1 <= 1e-4f;
                if (surfSeg)
                {
                    for (int j = 1; j <= _surfaceSubdiv; j++)
                    {
                        float f = j / (float)(_surfaceSubdiv + 1);
                        outP.Add(_project(Vector3.Lerp(p0, p1, f)));
                        outH.Add(0f);
                    }
                }
                outP.Add(p1);
                outH.Add(h1);
            }

            return new BotPath(outP.ToArray(), outH.ToArray());
        }
    }
}
