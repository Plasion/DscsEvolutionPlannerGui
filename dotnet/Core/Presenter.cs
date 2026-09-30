using System;
using System.Collections.Generic;
using System.Linq;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.Core;

/// <summary>求解结果 → 界面用的路线卡片数据（RouteVm / RouteStepVm / SkillChipVm）。</summary>
public sealed class RoutePresenter
{
    private readonly IReadOnlyDictionary<int, Digimon> _digimons;
    private readonly ImageStore _images;

    public RoutePresenter(IReadOnlyDictionary<int, Digimon> digimons, ImageStore images)
    {
        _digimons = digimons;
        _images = images;
    }

    /// <summary>一条路线：起/终/途徽标、箭头、继承技标、标题下的「沿途收集」摘要。</summary>
    public RouteVm Build(RouteResult route, IReadOnlyList<string> wantedSkills, ISet<int> viaIds)
    {
        var steps = new List<RouteStepVm>(route.Steps.Count);
        for (var i = 0; i < route.Steps.Count; i++)
        {
            var step = route.Steps[i];
            _digimons.TryGetValue(step.Id, out var digimon);

            var isStart = i == 0;
            var isEnd = i == route.Steps.Count - 1;
            var isVia = viaIds.Contains(step.Id);

            var badges = new List<BadgeVm>();
            if (isStart) badges.Add(new BadgeVm { Text = "起", Key = ResourceKeys.StartColor });
            if (isEnd && !isStart) badges.Add(new BadgeVm { Text = "终", Key = ResourceKeys.EndColor });
            if (isVia) badges.Add(new BadgeVm { Text = "途", Key = ResourceKeys.ViaColor });

            string? roleBar = null;
            if (isStart) roleBar = ResourceKeys.StartColor;
            else if (isEnd) roleBar = ResourceKeys.EndColor;
            else if (isVia) roleBar = ResourceKeys.ViaColor;

            steps.Add(new RouteStepVm
            {
                Id = step.Id,
                Name = digimon?.Name ?? step.Name,
                Generation = digimon?.DisplayGeneration ?? "",
                Image = digimon is null ? null : _images.Get(digimon.Id, digimon.Name),
                ArrowGlyph = ArrowGlyph(step.Arrow),
                ArrowLabel = ArrowLabel(step.Arrow),
                ArrowKey = ArrowKey(step.Arrow),
                Skills = digimon is null ? new List<SkillChipVm>() : CollectSkills(digimon, wantedSkills),
                Badges = badges,
                RoleBarKey = roleBar,
            });
        }

        var devolve = route.Steps.Count(step => step.Arrow == "↘");
        var modeChange = route.Steps.Count(step => step.Arrow == "⮂");
        var parts = new List<string> { $"{route.Steps.Count} 步" };
        if (devolve > 0) parts.Add($"{devolve} 次退化");
        if (modeChange > 0) parts.Add($"{modeChange} 次形态转换");

        return new RouteVm
        {
            Index = route.Index,
            Subtitle = string.Join(" · ", parts),
            Steps = steps,
            GoalText = BuildGoalText(route, wantedSkills),
        };
    }

    /// <summary>按空格 / 逗号 / 顿号等把一行拆成若干名字。</summary>
    public static List<string> SplitTokens(string? text) =>
        (text ?? "")
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToList();

    /// <summary>这只数码兽能提供的、本次查询里选中的继承技（技能名 + 学会等级）。</summary>
    private static List<SkillChipVm> CollectSkills(Digimon digimon, IReadOnlyList<string> wantedSkills)
    {
        var chips = new List<SkillChipVm>();
        if (wantedSkills.Count == 0) return chips;

        foreach (var skill in digimon.InheritSkills)
        {
            if (!wantedSkills.Contains(skill.Name)) continue;
            chips.Add(new SkillChipVm
            {
                Text = skill.Level is { } level ? $"{skill.Name} Lv{level}" : $"{skill.Name} Lv?",
                Tip = skill.Level is { } known
                    ? $"沿途收集：{skill.Name}（[{digimon.Id}] {digimon.Name} 在 Lv{known} 学会）"
                    : $"沿途收集：{skill.Name}（[{digimon.Id}] {digimon.Name}；data.csv 未标注学会等级）",
            });
        }

        return chips;
    }

    /// <summary>路线标题下那行「沿途收集：技能（等级 · 在哪只身上）」。</summary>
    private string BuildGoalText(RouteResult route, IReadOnlyList<string> wantedSkills)
    {
        if (wantedSkills.Count == 0) return "";

        var parts = new List<string>();
        foreach (var skill in wantedSkills)
        {
            string? found = null;
            foreach (var step in route.Steps)
            {
                if (!_digimons.TryGetValue(step.Id, out var digimon)) continue;
                var hit = digimon.InheritSkills.FirstOrDefault(candidate => candidate.Name == skill);
                if (hit is null) continue;

                var level = hit.Level is { } known ? $"Lv{known}" : "等级未标注";
                found = $"{skill}（{level} · #{digimon.Id} {digimon.Name}）";
                break;
            }

            parts.Add(found ?? $"{skill}（这条路线拿不到）");
        }

        return "沿途收集：" + string.Join("、", parts);
    }

    /// <summary>箭头语义（与 planner.py 输出一致）：↗ 进化、↘ 退化、⮂ 形态转换、→ 回落。</summary>
    private static string ArrowKey(string arrow) => arrow switch
    {
        "↗" => ResourceKeys.StartColor,
        "↘" => ResourceKeys.EndColor,
        "⮂" => ResourceKeys.ViaColor,
        _ => ResourceKeys.UnknownColor,
    };

    private static string ArrowGlyph(string arrow) => arrow switch
    {
        "↗" => "↗",
        "↘" => "↘",
        "⮂" => "⇄",
        _ => "→",
    };

    /// <summary>箭头的文字含义（悬停提示用）。</summary>
    private static string ArrowLabel(string arrow) => arrow switch
    {
        "↗" => "进化",
        "↘" => "退化",
        "⮂" => "形态转换",
        _ => "回落",
    };
}

/// <summary>图鉴数据：筛选出的卡片列表、以某只为中心的关系网。</summary>
public sealed class CodexPresenter
{
    /// <summary>从这一级开始才可能收成窄条（中心那圈直接邻居始终完整）。</summary>
    private const int CompactFromLevel = 2;

    /// <summary>这一层数量超过多少才值得收起来（少的时候收起来反而更难认）。</summary>
    private const int CompactMinCount = 6;

    private readonly DigimonCatalog _catalog;
    private readonly IReadOnlyDictionary<int, Digimon> _digimons;
    private readonly ImageStore _images;
    private readonly AppSettings _settings;

    public CodexPresenter(DigimonCatalog catalog, IReadOnlyDictionary<int, Digimon> digimons, ImageStore images, AppSettings settings)
    {
        _catalog = catalog;
        _digimons = digimons;
        _images = images;
        _settings = settings;
    }

    /// <summary>
    /// 左侧列表卡片：空关键字＝全部；否则按匹配度过滤（门槛见 config.json 的 codexFilterMinScore），
    /// 两种情况下都按编号排。
    /// </summary>
    public List<CodexEntryVm> BuildEntries(string? filter, int? centerId)
    {
        var keyword = (filter ?? "").Trim();

        IEnumerable<Digimon> source;
        if (keyword.Length == 0)
        {
            source = _digimons.Values.OrderBy(digimon => digimon.Id);
        }
        else
        {
            source = _catalog.Matcher.Suggest(keyword, _settings.CodexFilterLimit)
                .Where(suggestion => suggestion.Score >= _settings.CodexFilterMinScore && _digimons.ContainsKey(suggestion.Id))
                .Select(suggestion => _digimons[suggestion.Id])
                .OrderBy(digimon => digimon.Id);
        }

        return source.Select(digimon => MakeEntry(digimon, centerId)).ToList();
    }

    /// <summary>图鉴一共有多少只（异步预热时用来算进度）。</summary>
    public int Count => _digimons.Count;

    /// <summary>
    /// 按编号顺序生成第 [start, start+count) 只的卡片，供启动时的异步预热分块调用
    /// （一次建完会让状态栏的进度只有 0% 和 100% 两个值）。
    /// </summary>
    public List<CodexEntryVm> BuildEntryRange(int start, int count, int? centerId) =>
        _digimons.Values
            .OrderBy(digimon => digimon.Id)
            .Skip(start)
            .Take(count)
            .Select(digimon => MakeEntry(digimon, centerId))
            .ToList();

    /// <summary>
    /// 以 centerId 为中心按深度展开关系网。方向是单侧单方向的：右边只沿「进化」、
    /// 左边只沿「退化」，每一层都只沿自己那一侧继续走（a 进化出 b，就不再展开 b 的退化对象）。
    /// </summary>
    public CodexBuild Build(int centerId, int depth, bool highlightRoutes, IReadOnlyList<IReadOnlyList<int>> routeIds)
    {
        var root = _digimons[centerId];

        var level = new Dictionary<int, int> { [root.Id] = 0 };
        var order = new List<int> { root.Id };
        var queue = new Queue<int>();
        queue.Enqueue(root.Id);
        var truncated = false;

        void Expand(IEnumerable<int> ids, int nextLevel)
        {
            foreach (var other in ids.OrderBy(node => node))
            {
                if (level.ContainsKey(other)) continue;
                if (order.Count >= _settings.MaxNodes) { truncated = true; return; }

                level[other] = nextLevel;
                order.Add(other);
                queue.Enqueue(other);
            }
        }

        while (queue.Count > 0 && !truncated)
        {
            var id = queue.Dequeue();
            var current = level[id];
            if (Math.Abs(current) >= depth) continue;

            var digimon = _digimons[id];
            if (current == 0)
            {
                Expand(digimon.Evolutions, 1);
                Expand(digimon.Devolutions, -1);
            }
            else if (current > 0)
            {
                Expand(digimon.Evolutions, current + 1);
            }
            else
            {
                Expand(digimon.Devolutions, current - 1);
            }
        }

        var ordered = OrderByBarycenter(order, level, _catalog.Graph);
        var indexOf = new Dictionary<int, int>();
        for (var i = 0; i < ordered.Count; i++) indexOf[ordered[i]] = i;

        // 哪几个节点平时收成窄条：**第 2 级及以后，并且这一层数量 > 6**。
        // 前一条保证中心那圈直接邻居始终完整；后一条保证只有真正挤的那几层才收
        // （数量少的层收起来反而更难认）。
        var perLevel = new Dictionary<int, int>();
        foreach (var value in level.Values)
        {
            perLevel.TryGetValue(value, out var count);
            perLevel[value] = count + 1;
        }

        bool IsCompact(int id)
        {
            var distance = level[id];
            return Math.Abs(distance) >= CompactFromLevel && perLevel[distance] > CompactMinCount;
        }

        var highlightNodes = new HashSet<int>();
        var highlightLinks = new HashSet<(int, int)>();
        if (highlightRoutes)
        {
            foreach (var route in routeIds)
            {
                foreach (var id in route) highlightNodes.Add(id);
                for (var i = 1; i < route.Count; i++)
                {
                    var a = route[i - 1];
                    var b = route[i];
                    highlightLinks.Add(a < b ? (a, b) : (b, a));
                }
            }
        }

        var nodes = new List<CodexNodeVm>(ordered.Count);
        foreach (var id in ordered)
        {
            var digimon = _digimons[id];
            var isRoot = id == root.Id;
            nodes.Add(new CodexNodeVm
            {
                Id = digimon.Id,
                Name = digimon.Name,
                Generation = digimon.DisplayGeneration,
                Level = level[id],
                IsRoot = isRoot,
                IsCompactBase = IsCompact(id),
                Image = _images.GetThumbnail(digimon.Id, digimon.Name),
                OnRoute = highlightNodes.Contains(id),
                BorderKey = isRoot ? ResourceKeys.AccentColor : ResourceKeys.BorderColor,
                BorderWidth = isRoot ? 1.6 : 1,
                Tip = $"[{digimon.Id}] {digimon.Name} · {digimon.DisplayGeneration}\n点击看详情，双击把它设为图鉴中心",
            });
        }

        var links = new List<CodexLinkVm>();
        var seen = new HashSet<(int, int)>();
        foreach (var id in ordered)
        {
            foreach (var other in _digimons[id].Evolutions.Concat(_digimons[id].Devolutions))
            {
                if (!indexOf.ContainsKey(other)) continue;

                var a = level[id];
                var b = level[other];
                if (Math.Abs(a - b) > 1) continue;          // 隔层的关系不画

                var key = id < other ? (id, other) : (other, id);
                if (!seen.Add(key)) continue;

                string semantic;
                bool dashed;
                if (a == b)
                {
                    semantic = ResourceKeys.ModeChange;      // 同级·形态关系
                    dashed = true;
                }
                else if (_digimons[id].Evolutions.Contains(other))
                {
                    semantic = ResourceKeys.Evolve;          // 进化
                    dashed = false;
                }
                else
                {
                    semantic = ResourceKeys.Devolve;         // 退化
                    dashed = false;
                }

                links.Add(new CodexLinkVm
                {
                    From = indexOf[id],
                    To = indexOf[other],
                    Key = ResourceKeys.Resolve(semantic),
                    Dashed = dashed,
                    OnRoute = highlightLinks.Contains(key),
                });
            }
        }

        var graph = new CodexGraphVm
        {
            Nodes = nodes,
            Links = links,
            TruncatedNote = truncated ? $"节点太多，只展开了前 {_settings.MaxNodes} 只（调小深度，或双击另一只当中心）" : "",
        };

        var centerText = $"图鉴中心：[{root.Id}] {root.Name}";
        var summary = $"深度 {depth} · {nodes.Count} 只 · {links.Count} 条关系　|　"
            + "左边（橙线）＝能退化到的形态，右边（绿线）＝能进化成的形态；紫虚线＝同级／形态关系。"
            + (highlightRoutes ? "紫色「路线」标＝最近一次算出的路线经过这里。" : "（已关闭路线高亮）");

        return new CodexBuild(graph, centerText, summary);
    }

    private CodexEntryVm MakeEntry(Digimon digimon, int? centerId) => new()
    {
        Id = digimon.Id,
        Name = digimon.Name,
        IsCenter = digimon.Id == centerId,
        Image = _images.GetThumbnail(digimon.Id, digimon.Name),
        Tip = $"[{digimon.Id}] {digimon.Name} · {digimon.DisplayGeneration}\n点击：以它为中心看关系网",
    };

    /// <summary>层内排序：按更靠近中心那层邻居的平均位置排，再按编号兜底（少交叉）。</summary>
    private static List<int> OrderByBarycenter(
        List<int> order, Dictionary<int, int> level, IReadOnlyDictionary<int, List<int>> graph)
    {
        var result = new List<int>();
        var position = new Dictionary<int, int>();

        foreach (var group in order.GroupBy(id => Math.Abs(level[id])).OrderBy(group => group.Key))
        {
            var list = group.ToList();
            if (position.Count > 0) list = list.OrderBy(Bridge).ThenBy(id => id).ToList();

            for (var i = 0; i < list.Count; i++) position[list[i]] = i;
            result.AddRange(list);

            double Bridge(int id)
            {
                if (!graph.TryGetValue(id, out var neighbours)) return double.MaxValue;
                var known = neighbours.Where(position.ContainsKey).Select(node => (double)position[node]).ToList();
                return known.Count > 0 ? known.Average() : double.MaxValue;
            }
        }

        return result;
    }
}