using System;
using System.Collections.Generic;
using System.Linq;

namespace DscsEvolutionPlanner.Core;

/// <summary>搜索状态：(节点, 已满足的约束位)。</summary>
public readonly record struct PlannerNode(int Point, int State);

/// <summary>
/// 路径搜索（C# 版，逐行对齐 planner.py 的 bfs_search / yen_search）。
/// 连「候选路径用 Node 序列的字典序比较」这个细节都保持一致，以保证路线顺序相同。
/// </summary>
internal sealed class PathFinder
{
    private readonly IReadOnlyDictionary<int, List<int>> _graph;
    private readonly IReadOnlyDictionary<int, int> _states;
    private readonly int _visitLimit;

    public PathFinder(IReadOnlyDictionary<int, List<int>> graph, IReadOnlyDictionary<int, int> states, int visitLimit = 400_000)
    {
        _graph = graph;
        _states = states;
        _visitLimit = visitLimit;
    }

    /// <summary>状态空间过大触发安全上限时为 true（planner.py 没有这个保护，会一直跑下去）。</summary>
    public bool Truncated { get; private set; }

    private int StateOf(int point) => _states.TryGetValue(point, out var state) ? state : 0;

    /// <summary>BFS 找一条满足约束的路径（首条最短路径 / 偏离点之后的最短路径都用它）。</summary>
    public List<PlannerNode>? FindPath(PlannerNode start, IReadOnlyList<int> ends, int endState,
        HashSet<(PlannerNode From, PlannerNode To)> forbiddenEdges, HashSet<PlannerNode> forbiddenNodes)
    {
        if (forbiddenNodes.Contains(start)) return null;
        if (ends.Count > 0 && ends.All(end => forbiddenNodes.Contains(new PlannerNode(end, endState)))) return null;

        var queue = new Queue<PlannerNode>();
        queue.Enqueue(start);
        var parent = new Dictionary<PlannerNode, PlannerNode> { [start] = start };
        var visited = new HashSet<PlannerNode> { start };
        PlannerNode? endNode = null;

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            var reached = ends.Count > 0
                ? ends.Contains(u.Point) && u.State == endState
                : u.State == endState;
            if (reached)
            {
                endNode = u;
                break;
            }

            if (!_graph.TryGetValue(u.Point, out var neighbours)) continue;
            foreach (var vPoint in neighbours)
            {
                if (!_graph.ContainsKey(vPoint)) continue;   // 被世代过滤删掉的节点：连同它的边一起忽略
                var v = new PlannerNode(vPoint, u.State | StateOf(vPoint));

                if (forbiddenEdges.Contains((u, v)) || forbiddenEdges.Contains((v, u))) continue;  // 无向边两种顺序都要看
                if (forbiddenNodes.Contains(v)) continue;
                if (visited.Contains(v)) continue;

                if (visited.Count >= _visitLimit)
                {
                    Truncated = true;
                    return null;
                }

                visited.Add(v);
                parent[v] = u;
                queue.Enqueue(v);
            }
        }

        if (endNode is null) return null;

        var path = new List<PlannerNode>();
        var cursor = endNode.Value;
        while (true)
        {
            path.Add(cursor);
            var previous = parent[cursor];
            if (previous.Equals(cursor)) break;   // 起点的父是自己
            cursor = previous;
        }
        path.Reverse();
        return path;
    }

    /// <summary>Yen 算法求前 k 条路径（返回节点编号序列）。</summary>
    public List<List<int>> YenSearch(int start, IReadOnlyList<int> ends, int endState, int k)
    {
        var startState = StateOf(start);
        if (ends.Contains(start) && startState == endState)
            return new List<List<int>> { new() { start } };

        var firstPath = FindPath(new PlannerNode(start, startState), ends, endState,
            new HashSet<(PlannerNode, PlannerNode)>(), new HashSet<PlannerNode>());
        if (firstPath is null) return new List<List<int>>();

        var accepted = new List<List<PlannerNode>> { firstPath };
        var seen = new HashSet<string> { PathKey(firstPath) };
        var candidates = new List<List<PlannerNode>>();

        for (var iteration = 1; iteration < k; iteration++)
        {
            var lastPath = accepted[^1];

            for (var i = 0; i < lastPath.Count - 1; i++)
            {
                var spurNode = lastPath[i];
                var rootPath = lastPath.Take(i + 1).ToList();

                // 前缀路径里除偏离点之外的节点禁止再走（保证是简单路径）
                var forbiddenNodes = new HashSet<PlannerNode>(rootPath.Take(rootPath.Count - 1));

                // 已经走过的、与当前前缀相同的路径，禁用它们从偏离点出发的那条边
                var forbiddenEdges = new HashSet<(PlannerNode, PlannerNode)>();
                foreach (var path in accepted)
                {
                    if (path.Count > i && rootPath.SequenceEqual(path.Take(i + 1)))
                        forbiddenEdges.Add((spurNode, path[i + 1]));
                }

                var spurPath = FindPath(spurNode, ends, endState, forbiddenEdges, forbiddenNodes);
                if (spurPath is null) continue;

                var total = rootPath.Concat(spurPath.Skip(1)).ToList();
                if (!seen.Add(PathKey(total))) continue;
                candidates.Add(total);
            }

            if (candidates.Count == 0) break;

            // 与 py 的 heapq 等价：取“字典序最小”的候选
            var best = 0;
            for (var c = 1; c < candidates.Count; c++)
                if (ComparePaths(candidates[c], candidates[best]) < 0) best = c;

            var next = candidates[best];
            candidates.RemoveAt(best);
            accepted.Add(next);
        }

        return accepted.Select(path => path.Select(node => node.Point).ToList()).ToList();
    }

    private static string PathKey(IEnumerable<PlannerNode> path) =>
        string.Join(",", path.Select(node => $"{node.Point}:{node.State}"));

    /// <summary>Python 里 list[Node] 的比较语义：逐元素比 (point, state)，前缀相同时短的更小。</summary>
    private static int ComparePaths(List<PlannerNode> a, List<PlannerNode> b)
    {
        var shared = Math.Min(a.Count, b.Count);
        for (var i = 0; i < shared; i++)
        {
            var byPoint = a[i].Point.CompareTo(b[i].Point);
            if (byPoint != 0) return byPoint;
            var byState = a[i].State.CompareTo(b[i].State);
            if (byState != 0) return byState;
        }
        return a.Count.CompareTo(b.Count);
    }
}
