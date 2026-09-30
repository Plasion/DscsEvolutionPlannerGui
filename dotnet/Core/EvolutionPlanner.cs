using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DscsEvolutionPlanner.Core;

public enum EndpointMode
{
    /// <summary>指定一个终点。</summary>
    Single,
    /// <summary>给出多只，任一只都可以作为终点。</summary>
    Multi,
    /// <summary>任意数码兽都可以作为终点（必须配合途经或继承技约束）。</summary>
    Any,
}

/// <summary>一次查询的全部输入（等价 planner.py 的命令行参数）。</summary>
public sealed class PlannerQuery
{
    public string StartText { get; set; } = "";
    public EndpointMode Mode { get; set; } = EndpointMode.Single;
    public string EndText { get; set; } = "";
    public List<string> MultiEnds { get; } = new();

    /// <summary>途经组：组内任一只即可，组之间都要满足。</summary>
    public List<List<string>> ViaGroups { get; } = new();

    /// <summary>沿途必须能收集到的继承技。</summary>
    public List<string> Skills { get; } = new();

    /// <summary>要排除的世代。</summary>
    public List<string> ExcludeGenerations { get; } = new();

    public int K { get; set; } = 3;
    public bool Reverse { get; set; }
}

public sealed class RouteStep
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>与上一只的关系：↗ 进化 / ↘ 退化 / ⮂ 形态转换 / → 回落。首只为空。</summary>
    public string Arrow { get; init; } = "";
}

public sealed class RouteResult
{
    public int Index { get; init; }
    public List<RouteStep> Steps { get; } = new();
    public string Text { get; set; } = "";
}

public sealed class MatchCandidateInfo
{
    public string Key { get; init; } = "";
    public double Score { get; init; }
    public int? Id { get; init; }
}

/// <summary>一次“名字解析”的结果，界面用它做输入框下方的即时提示。</summary>
public sealed class ResolveResult
{
    public int? Id { get; init; }
    public string? Key { get; init; }
    public double Score { get; init; }
    public bool Exact { get; init; }
    public List<MatchCandidateInfo> Suggestions { get; init; } = new();
    public string? Error { get; init; }
}

public sealed class PlannerResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<string> Messages { get; } = new();
    public List<RouteResult> Routes { get; } = new();
    /// <summary>与命令行完全一致的输出行（不含加载期警告，警告见 Messages）。</summary>
    public List<string> TextLines { get; } = new();
    public double ElapsedMs { get; set; }
    public bool Truncated { get; set; }
}

/// <summary>进化路线算法（对应 planner.py 的匹配部分，运行时不需要 Python）：ResolveName / ResolveSkill 做解析，Search 求路线。</summary>
public sealed class EvolutionPlanner
{
    private readonly DigimonCatalog _catalog;
    private readonly IReadOnlyDictionary<int, int> _zeroStates;

    public EvolutionPlanner(DigimonCatalog catalog)
    {
        _catalog = catalog;
        _zeroStates = catalog.ById.Keys.ToDictionary(id => id, _ => 0);
    }

    public DigimonCatalog Catalog => _catalog;

    // ------------------------------------------------------------------ 解析（界面即时提示也走这里）
    public ResolveResult ResolveName(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0) return new ResolveResult();

        if (int.TryParse(value, out var id))
        {
            return _catalog.ById.ContainsKey(id)
                ? new ResolveResult { Id = id, Key = _catalog.ById[id].Name, Score = 1, Exact = true }
                : new ResolveResult { Error = $"找不到编号 {value}" };
        }

        return Pick(_catalog.NameMatcher, value, key => ToId(key));
    }

    public ResolveResult ResolveGeneration(string? text)
    {
        var value = text?.Trim() ?? "";
        return value.Length == 0 ? new ResolveResult() : Pick(_catalog.GenerationMatcher, value, _ => null);
    }

    public ResolveResult ResolveSkill(string? text)
    {
        var value = NormalizeSkillName(text?.Trim() ?? "");
        return value.Length == 0 ? new ResolveResult() : Pick(_catalog.SkillMatcher, value, _ => null);
    }

    private static ResolveResult Pick(FuzzyMatcher matcher, string text, Func<string, int?> toId)
    {
        // 与 planner.py 一致：取最佳候选，不做阈值过滤（界面会把低分标成橙色提示）
        var hits = matcher.Match(text, 4);
        if (hits.Count == 0) return new ResolveResult();

        var best = hits[0];
        return new ResolveResult
        {
            Key = best.Key,
            Score = best.Score,
            Exact = best.Score >= 0.999,
            Id = toId(best.Key),
            Suggestions = hits.Skip(1).Select(hit => new MatchCandidateInfo
            {
                Key = hit.Key,
                Score = hit.Score,
                Id = toId(hit.Key),
            }).ToList(),
        };
    }

    private int? ToId(string key) => _catalog.NameToId.TryGetValue(key, out var id) ? id : null;

    /// <summary>
    /// 继承技写法归一：Unicode 罗马数字（ⅠⅡⅢ）与 ASCII 写法（I/II/III，大小写）都转成阿拉伯数字。
    /// 注：planner.py 的同类函数因正则候选顺序，会把 ASCII 的 "II" 变成 "11"，这里按文档本意处理。
    /// </summary>
    public static string NormalizeSkillName(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var rest = text.AsSpan(i);
            if (rest.StartsWith("III", StringComparison.Ordinal)) { builder.Append('3'); i += 2; continue; }
            if (rest.StartsWith("iii", StringComparison.Ordinal)) { builder.Append('3'); i += 2; continue; }
            if (rest.StartsWith("II", StringComparison.Ordinal)) { builder.Append('2'); i++; continue; }
            if (rest.StartsWith("ii", StringComparison.Ordinal)) { builder.Append('2'); i++; continue; }

            builder.Append(text[i] switch
            {
                'Ⅰ' or 'I' or 'i' => '1',
                'Ⅱ' => '2',
                'Ⅲ' => '3',
                var ch => ch,
            });
        }
        return builder.ToString();
    }

    // ------------------------------------------------------------------ 搜索
    public PlannerResult Search(PlannerQuery query)
    {
        var started = Environment.TickCount64;
        var result = new PlannerResult();
        result.Messages.AddRange(_catalog.Warnings);

        var start = ResolveName(query.StartText).Id;
        if (start is null) return Fail(result, $"错误：找不到起点数码兽「{query.StartText}」。");

        var ends = new List<int>();
        switch (query.Mode)
        {
            case EndpointMode.Single:
                var end = ResolveName(query.EndText).Id;
                if (end is null) return Fail(result, $"错误：找不到终点数码兽「{query.EndText}」。");
                ends.Add(end.Value);
                break;

            case EndpointMode.Multi:
                if (query.MultiEnds.Count == 0)
                    return Fail(result, "错误：-em需要至少一个参数。输入?、-h或--help查看用法。");
                foreach (var text in query.MultiEnds)
                {
                    var id = ResolveName(text).Id;
                    if (id is null) return Fail(result, $"错误：找不到终点数码兽「{text}」。");
                    ends.Add(id.Value);
                }
                break;

            case EndpointMode.Any:
                if (query.ViaGroups.Count == 0 && query.Skills.Count == 0)
                    return Fail(result, "错误：使用了-a但未使用-pm或-ps进行约束。输入?、-h或--help查看用法。");
                break;
        }

        if (query.K < 1) return Fail(result, $"错误：提供给-k的参数「{query.K}」太小了，应该至少为1。");
        if (query.Mode == EndpointMode.Single && string.IsNullOrWhiteSpace(query.EndText))
            return Fail(result, "错误：参数太少了。输入?、-h或--help查看用法。");

        // ---- 逐项套用约束（顺序与 planner.py 相同：-d → -pm → -ps）
        var usedGraph = _catalog.Graph;
        if (query.ExcludeGenerations.Count > 0)
        {
            var filtered = _catalog.Graph.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var text in query.ExcludeGenerations)
            {
                var hit = _catalog.GenerationMatcher.Best(text);
                if (hit is null || !_catalog.GenerationToIds.TryGetValue(hit.Key, out var ids))
                    return Fail(result, $"错误：找不到世代「{text}」。");
                foreach (var id in ids) filtered.Remove(id);   // 只删节点，边交给 BFS 忽略
            }
            usedGraph = filtered;
        }

        Dictionary<int, int>? states = null;
        var stateCount = 0;

        foreach (var group in query.ViaGroups)
        {
            states ??= new Dictionary<int, int>(_zeroStates);
            foreach (var text in group)
            {
                var id = ResolveName(text).Id;
                if (id is null) return Fail(result, $"错误：找不到途径数码兽「{text}」。");
                states[id.Value] |= 1 << stateCount;
            }
            stateCount++;
        }

        foreach (var text in query.Skills)
        {
            var hit = _catalog.SkillMatcher.Best(NormalizeSkillName(text));
            if (hit is null || !_catalog.SkillToIds.TryGetValue(hit.Key, out var ids))
                return Fail(result, $"错误：找不到继承技「{text}」。");

            states ??= new Dictionary<int, int>(_zeroStates);
            foreach (var id in ids) states[id] |= 1 << stateCount;
            stateCount++;
        }

        var endState = (1 << stateCount) - 1;
        var finder = new PathFinder(usedGraph, states ?? _zeroStates);
        var paths = finder.YenSearch(start.Value, ends, endState, query.K);

        if (finder.Truncated)
            result.Messages.Add("警告：搜索状态过多，已按安全上限提前终止，结果可能不完整。");

        if (paths.Count == 0) return Fail(result, "错误：找不到可用进化路线");

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (query.Reverse) path.Reverse();
            var route = BuildRoute(i + 1, path);
            result.Routes.Add(route);
            result.TextLines.Add($"进化路线{route.Index}：{route.Text}");
        }

        result.Ok = true;
        result.ElapsedMs = Environment.TickCount64 - started;
        return result;
    }

    private static PlannerResult Fail(PlannerResult result, string error)
    {
        result.Ok = false;
        result.Error = error;
        result.TextLines.Clear();
        result.TextLines.Add(error);
        return result;
    }

    /// <summary>等价 planner.py 的 generate_output_text：带箭头地拼出路线文本。</summary>
    private RouteResult BuildRoute(int index, List<int> path)
    {
        var route = new RouteResult { Index = index };
        var text = new StringBuilder();
        var lastId = -1;

        foreach (var id in path)
        {
            var digimon = _catalog.ById[id];
            var arrow = "";

            if (lastId != -1)
            {
                var last = _catalog.ById[lastId];
                if (last.Evolutions.Contains(id) && digimon.Evolutions.Contains(lastId)) arrow = "⮂";       // 形态转换
                else if (last.Evolutions.Contains(id) && digimon.Devolutions.Contains(lastId)) arrow = "↗"; // 进化
                else if (last.Devolutions.Contains(id) && digimon.Evolutions.Contains(lastId)) arrow = "↘"; // 退化
                else arrow = "→";                                                                            // 映射错误回落
                text.Append(arrow).Append(' ');
            }

            route.Steps.Add(new RouteStep { Id = id, Name = digimon.Name, Arrow = arrow });
            text.Append($"[{id}]{digimon.Name}");
            lastId = id;
        }

        route.Text = text.ToString();
        return route;
    }
}
