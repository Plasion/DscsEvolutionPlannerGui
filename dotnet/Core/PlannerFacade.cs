using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.Core;

/// <summary>建图结果：关系网 + 工具条上那两行文案。</summary>
public sealed record CodexBuild(CodexGraphVm Graph, string CenterText, string Summary);

/// <summary>一次解析（名字 / 继承技）的结果。</summary>
public sealed record ResolveInfo(int? Id, string Key, double Score);

/// <summary>一次路线查询的全部产物。</summary>
public sealed record QueryResult(
    bool Ok,
    string? Error,
    IReadOnlyList<string> Messages,
    IReadOnlyList<RouteVm> Routes,
    IReadOnlyList<string> TextLines,
    double ElapsedMs);

/// <summary>
/// 核心 → 视图模型的唯一出口：把算法结果、图鉴数据、图片与解析提示整理成绑定用的对象。
/// 视图模型只跟它打交道，不直接碰 Catalog / Planner / PathFinder。
/// </summary>
public sealed class PlannerFacade
{
    private readonly DigimonCatalog _catalog;
    private readonly EvolutionPlanner _planner;
    private readonly ImageStore _images;
    private readonly AppSettings _settings;

    private readonly Dictionary<int, Digimon> _byId = new();

    public PlannerFacade(DigimonCatalog catalog, ImageStore images, AppSettings settings)
    {
        _catalog = catalog;
        _planner = new EvolutionPlanner(catalog);
        _images = images;
        _settings = settings;

        foreach (var digimon in catalog.ById.Values) _byId[digimon.Id] = digimon;
        Routes = new RoutePresenter(_byId, images);
        Codex = new CodexPresenter(catalog, _byId, images, settings);
    }

    // ------------------------------------------------------------------ 基本数据
    public string DataPath => _catalog.SourcePath;
    public int DigimonCount => _catalog.ById.Count;
    public bool PinyinLoaded => _catalog.PinyinLoaded;
    public bool PinyinStale => _catalog.PinyinStale;
    public IReadOnlyList<string> Warnings => _catalog.Warnings;

    /// <summary>排除世代可选项（保持 CSV 出现顺序，丢掉空世代）。</summary>
    public IReadOnlyList<string> Generations
    {
        get
        {
            var list = new List<string>();
            foreach (var generation in _catalog.GenerationKeys)
                if (generation.Length > 0) list.Add(generation);
            return list;
        }
    }

    /// <summary>继承技候选（名称 + 全部数码兽里最早学到的等级）。</summary>
    public IReadOnlyList<(string Name, string LevelText)> Skills()
    {
        var list = new List<(string, string)>(_catalog.SkillKeys.Count);
        foreach (var skill in _catalog.SkillKeys)
            list.Add((skill, _catalog.SkillMinLevels.TryGetValue(skill, out var level) ? $"最早 Lv{level}" : ""));
        return list;
    }

    /// <summary>图片目录变了就重设并丢缓存。</summary>
    public void UseImageDirectory(string directory)
    {
        _images.Directory = directory;
        _images.Clear();
    }

    public bool HasDigimon(int id) => _byId.ContainsKey(id);

    /// <summary>编号升序里的第一只（图鉴没指定中心时的兜底）。</summary>
    public int? FirstId()
    {
        var best = (int?)null;
        foreach (var id in _byId.Keys)
            if (best is null || id < best) best = id;
        return best;
    }

    // ------------------------------------------------------------------ 解析（输入框提示 / 查询都走这里）
    public ResolveInfo ResolveName(string? text) => Convert(_planner.ResolveName(text));

    public ResolveInfo ResolveSkill(string? text) => Convert(_planner.ResolveSkill(text));

    /// <summary>输入框下方的即时提示（颜色由视图按 ResourceKeys 取）。</summary>
    public (string Text, string Key) Describe(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0) return ("", ResourceKeys.FaintColor);

        var resolved = _planner.ResolveName(value);
        if (resolved.Id is { } id && _byId.TryGetValue(id, out var digimon))
        {
            var confidence = resolved.Score >= _settings.ExactScore
                ? "完全匹配"
                : resolved.Score >= _settings.GoodScore
                    ? $"相似度 {resolved.Score:0.00}"
                    : $"弱匹配 {resolved.Score:0.00}（置信度低）";
            return ($"→ [{id}] {digimon.Name} · {digimon.DisplayGeneration} · {confidence}",
                resolved.Score >= _settings.GoodScore ? ResourceKeys.StartColor : ResourceKeys.WarnColor);
        }

        if (resolved.Error is { Length: > 0 } error) return (error, ResourceKeys.ErrorColor);

        var closest = resolved.Suggestions.Count > 0 ? resolved.Suggestions[0] : null;
        return (closest is null
                ? "未找到匹配的数码兽"
                : $"未找到匹配；最接近：{closest.Key}（{closest.Score:0.00}）",
            ResourceKeys.ErrorColor);
    }

    /// <summary>起点 / 终点候选（名称 + 匹配度）。</summary>
    public IReadOnlyList<NameSuggestion> Suggest(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<NameSuggestion>()
            : _catalog.Matcher.Suggest(text, _settings.SuggestLimit);

    private static ResolveInfo Convert(ResolveResult result) => new(result.Id, result.Key ?? "", result.Score);

    // ------------------------------------------------------------------ 查询与呈现
    /// <summary>求解结果 → 路线卡片数据。</summary>
    public RoutePresenter Routes { get; }

    /// <summary>图鉴数据：筛选出的卡片列表、以某只为中心的关系网。</summary>
    public CodexPresenter Codex { get; }

    /// <summary>求路线：算法 + 卡片数据一次做完。</summary>
    public QueryResult Search(PlannerQuery query, IReadOnlyList<string> wantedSkills)
    {
        var result = _planner.Search(query);
        var viaIds = ResolveViaIds(query.ViaGroups);

        var routes = new List<RouteVm>(result.Routes.Count);
        foreach (var route in result.Routes)
            routes.Add(Routes.Build(route, wantedSkills, viaIds));

        return new QueryResult(result.Ok, result.Error, result.Messages, routes, result.TextLines, result.ElapsedMs);
    }

    /// <summary>「途经数码兽」里能解析出的编号（给卡片打「途」标用）。</summary>
    public HashSet<int> ResolveViaIds(IReadOnlyList<List<string>> viaGroups)
    {
        var ids = new HashSet<int>();
        foreach (var group in viaGroups)
            foreach (var name in group)
                if (_planner.ResolveName(name).Id is { } id) ids.Add(id);
        return ids;
    }

    /// <summary>给图鉴高亮用：最近一次算出的路线的编号序列。</summary>
    public IReadOnlyList<IReadOnlyList<int>> RouteIds(IReadOnlyList<RouteVm> routes)
    {
        var list = new List<IReadOnlyList<int>>(routes.Count);
        foreach (var route in routes)
        {
            var ids = new List<int>(route.Steps.Count);
            foreach (var step in route.Steps) ids.Add(step.Id);
            list.Add(ids);
        }
        return list;
    }

    /// <summary>详情栏内容。</summary>
    public DigimonDetailVm Detail(int id) =>
        _byId.TryGetValue(id, out var digimon)
            ? DigimonDetailVm.Create(digimon, _images.Get(digimon.Id, digimon.Name))
            : new DigimonDetailVm();
}