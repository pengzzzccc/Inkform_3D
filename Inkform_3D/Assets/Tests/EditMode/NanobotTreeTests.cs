using System;
using NUnit.Framework;
using UnityEngine;
using Inkform.Nanobots;

namespace Inkform.Tests
{
    /// <summary>
    /// NanobotTree（纯逻辑生长树）的 EditMode 单测。验收要点：
    /// 每个叶子是唯一表面终点、树只发散不汇聚、起点统一、表面段贴面投影、确定性。
    /// </summary>
    public class NanobotTreeTests
    {
        const float Eps = 1e-3f;

        // 确定性、非共线、无重复的散点（不依赖内部 Hash）。
        static Vector3[] SpreadPoints(int n)
        {
            var a = new Vector3[n];
            for (int i = 0; i < n; i++)
                a[i] = new Vector3(
                    Mathf.Sin(i * 1.3f) * 3f,
                    Mathf.Cos(i * 0.7f) * 3f,
                    Mathf.Sin(i * 2.1f + 0.5f) * 3f);
            return a;
        }

        static bool Approx(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < Eps * Eps;

        [Test]
        public void Build_OneLeafPerPoint_EndpointsAreInjective()
        {
            var pts = SpreadPoints(16);
            var start = new Vector3(0f, 10f, 0f);
            var tree = NanobotTree.Build(start, pts, Vector3.zero, airBranchDepth: 2,
                outwardLift: 2f, surfaceSubdiv: 0);

            Assert.AreEqual(pts.Length, tree.LeafCount, "叶子数应等于请求点数");

            // 每条叶子路径终点 == 对应输入点；且互不相同（每分支奔向不同表面点）。
            for (int i = 0; i < pts.Length; i++)
            {
                Assert.IsNotNull(tree.LeafPaths[i]);
                var p = tree.LeafPaths[i].Points;
                Assert.IsTrue(Approx(p[p.Length - 1], pts[i]), $"叶 {i} 终点应落在表面点 {i}");
            }
            for (int i = 0; i < pts.Length; i++)
                for (int j = i + 1; j < pts.Length; j++)
                {
                    var pi = tree.LeafPaths[i].Points;
                    var pj = tree.LeafPaths[j].Points;
                    Assert.IsFalse(Approx(pi[pi.Length - 1], pj[pj.Length - 1]),
                        $"叶 {i} 与叶 {j} 终点不应相同");
                }
        }

        [Test]
        public void Build_AllPathsStartAtA()
        {
            var pts = SpreadPoints(12);
            var start = new Vector3(1f, 8f, -2f);
            var tree = NanobotTree.Build(start, pts, Vector3.zero, 2, 2f, 0);
            foreach (var path in tree.LeafPaths)
                Assert.IsTrue(Approx(path.Points[0], start), "所有路径应从 A 出发（共享树干）");
        }

        [Test]
        public void Build_BranchesNeverReconvergeAfterDiverging()
        {
            var pts = SpreadPoints(20);
            var start = new Vector3(0f, 12f, 0f);
            // subdiv=0 → 路径即节点折线，便于逐点比对；这是核心"不回聚"铁律。
            var tree = NanobotTree.Build(start, pts, Vector3.zero, airBranchDepth: 3,
                outwardLift: 2.5f, surfaceSubdiv: 0);

            for (int i = 0; i < pts.Length; i++)
                for (int j = i + 1; j < pts.Length; j++)
                {
                    var a = tree.LeafPaths[i].Points;
                    var b = tree.LeafPaths[j].Points;

                    // 找首个分叉点。
                    int k = 0;
                    while (k < a.Length && k < b.Length && Approx(a[k], b[k])) k++;

                    // 分叉之后，两条路径不得再共享任何一点（否则就是回聚）。
                    for (int x = k; x < a.Length; x++)
                        for (int y = k; y < b.Length; y++)
                            Assert.IsFalse(Approx(a[x], b[y]),
                                $"叶 {i}/{j} 在分叉后于点 a[{x}]=b[{y}] 处回聚了");
                }
        }

        [Test]
        public void Build_IsDeterministic()
        {
            var pts = SpreadPoints(18);
            var start = new Vector3(0f, 9f, 0f);
            var t1 = NanobotTree.Build(start, pts, Vector3.zero, 3, 2.5f, 1, p => p);
            var t2 = NanobotTree.Build(start, pts, Vector3.zero, 3, 2.5f, 1, p => p);

            Assert.AreEqual(t1.LeafCount, t2.LeafCount);
            Assert.AreEqual(t1.Edges.Count, t2.Edges.Count, "边数应一致");
            Assert.AreEqual(t1.MaxLen, t2.MaxLen, 1e-4f, "MaxLen 应一致");
            for (int i = 0; i < t1.LeafCount; i++)
            {
                var a = t1.LeafPaths[i].Points;
                var b = t2.LeafPaths[i].Points;
                Assert.AreEqual(a.Length, b.Length, $"叶 {i} 路径点数应一致");
                for (int k = 0; k < a.Length; k++)
                    Assert.IsTrue(Approx(a[k], b[k]), $"叶 {i} 点 {k} 应一致");
            }
        }

        [Test]
        public void Build_SurfaceSegmentsProjectedOntoSphere()
        {
            // 叶子全在半径 R 球面上；airBranchDepth=0 → 节点全贴面；subdiv>0 + 球面投影
            // → 除起点 A 外所有点都应落在球面上（贴面蔓延、不切入物体）。
            const float R = 4f;
            int n = 24;
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float u = (i / (float)n) * 2f - 1f;
                float phi = i * 2.3999f; // 黄金角，铺满球面
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
                pts[i] = new Vector3(r * Mathf.Cos(phi), u, r * Mathf.Sin(phi)) * R;
            }
            var start = new Vector3(0f, 10f, 0f);
            Func<Vector3, Vector3> sphere = p =>
                p.sqrMagnitude > 1e-8f ? p.normalized * R : new Vector3(R, 0f, 0f);

            var tree = NanobotTree.Build(start, pts, Vector3.zero, airBranchDepth: 0,
                outwardLift: 0f, surfaceSubdiv: 3, projectToSurface: sphere);

            foreach (var path in tree.LeafPaths)
                for (int k = 1; k < path.Points.Length; k++) // k=0 是 A，跳过
                    Assert.AreEqual(R, path.Points[k].magnitude, 1e-2f,
                        "贴面段的点应被投影到球面半径 R 上");
        }

        [Test]
        public void BotPath_Sample_HitsStartAndLeaf_AndAdvances()
        {
            var pts = SpreadPoints(8);
            var start = new Vector3(0f, 6f, 0f);
            var tree = NanobotTree.Build(start, pts, Vector3.zero, 2, 2f, 0);
            var path = tree.LeafPaths[0];

            path.Sample(0f, out var p0, out _, out _);
            Assert.IsTrue(Approx(p0, start), "弧长 0 处应在 A");

            path.Sample(path.Length + 5f, out var pe, out _, out _);
            Assert.IsTrue(Approx(pe, pts[0]), "超过总弧长应停在叶子终点");

            // 按弧长采样：前进 δ 弧长，空间位移不应超过 δ（折线上弦长 ≤ 弧长）。
            float step = path.Length / 16f;
            for (float d = 0f; d + step <= path.Length; d += step)
            {
                path.Sample(d, out var p, out _, out _);
                path.Sample(d + step, out var q, out _, out _);
                Assert.LessOrEqual(Vector3.Distance(p, q), step + 1e-3f,
                    "弧长采样位移不应超过弧长步长");
            }
        }
    }
}
