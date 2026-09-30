using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DscsEvolutionPlanner.Core;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>壳的视图模型：详情栏、路线结果、图鉴关系网与截图验收。</summary>
public sealed partial class MainViewModel
{
    private DigimonDetailVm? _selectedDetail;
    private string _detailsHint = "";
    private string _emptyHint = "";
    private bool _textViewVisible;
    private string _textOutput = "";
    private bool _isGuideMode;
    private int _codexDepth = 2;
    private bool _guideHighlightRoutes = true;
    private string _guideCenterText = "";
    private string _guideSummary = "";
    private CodexGraphVm? _guideGraph;
    private string _guideFilter = "";
    private IReadOnlyList<CodexEntryVm> _guideEntries = Array.Empty<CodexEntryVm>();
    private string _guideListStatus = "";
    private bool _guideBusy;
    private string _guideBusyText = "正在生成图鉴…";
    private double _zoom = 1.0;
    private bool _guideBuilt;
    private bool GuideBuilt { get => _guideBuilt; set => _guideBuilt = value; }
    private int? _guideCenterId;
    private string _codexLoadText = "";
    private Task? _warmTask;
    private string _assetsDirectory = "";
    private string _statusText = "";
    private string _statusKey = ResourceKeys.SubtleColor;
    private string _envText = "";

    /// <summary>可选的图鉴深度（数据来自 config.json）。</summary>
    public List<int> DepthChoices { get; } = new List<int>();

    // ============================ 详情栏 ============================
    public DigimonDetailVm? SelectedDetail
    {
        get => _selectedDetail;
        private set { if (Set(ref _selectedDetail, value)) Raise(nameof(HasDetail)); }
    }

    public bool HasDetail => _selectedDetail is not null;

    public string DetailsHint { get => _detailsHint; set => Set(ref _detailsHint, value ?? ""); }

    /// <summary>选中某只数码兽：刷新右侧详情栏。路线卡片、关系网节点、图鉴卡片都走它。</summary>
    public void ShowDetail(int id)
    {
        if (_facade is null || !_facade.HasDigimon(id)) return;
        _lastDetailId = id;
        SelectedDetail = _facade.Detail(id);
    }

    // ============================ 路线区 / 文本视图 ============================
    public ObservableCollection<RouteVm> Routes { get; } = new();

    public string EmptyHint { get => _emptyHint; set => Set(ref _emptyHint, value ?? ""); }

    public bool TextViewVisible
    {
        get => _textViewVisible;
        private set
        {
            if (!Set(ref _textViewVisible, value)) return;
            Raise(nameof(ViewButtonText));
            RefreshRegion();
        }
    }

    public string ViewButtonText => _textViewVisible ? "图形视图" : "文本视图";

    public string TextOutput { get => _textOutput; private set => Set(ref _textOutput, value ?? ""); }

    public bool ShowRouteList => Routes.Count > 0 && !_textViewVisible;
    public bool ShowEmptyHint => Routes.Count == 0 && !_textViewVisible;
    public bool ShowTextOutput => _textViewVisible;

    public void RefreshRegion()
    {
        Raise(nameof(ShowRouteList));
        Raise(nameof(ShowEmptyHint));
        Raise(nameof(ShowTextOutput));
    }

    private void ToggleTextView()
    {
        TextViewVisible = !TextViewVisible;
        if (TextViewVisible && TextOutput.Length == 0)
            TextOutput = "（还没有结果：先在左侧点「计算路线」）";
    }

    private void Reset()
    {
        StartText = "";
        EndText = "";
        MultiEndText = "";
        ViaText = "";
        ModeSingle = true;
        Reverse = false;
        K = _settings.DefaultK;
        foreach (var generation in Generations) generation.IsChecked = false;
        ClearSkills();
        Routes.Clear();
        SelectedDetail = null;
        EmptyHint = "填写起终点后点击「计算路线」。";
        RefreshRegion();
        ShowStatus("已重置。");
    }

    // ============================ 视图模式（路线 / 图鉴） ============================
    public bool IsGuideMode
    {
        get => _isGuideMode;
        private set
        {
            if (!Set(ref _isGuideMode, value)) return;
            Raise(nameof(IsRouteMode));
            Raise(nameof(ModeName));
        }
    }

    public bool IsRouteMode => !_isGuideMode;

    /// <summary>当前页名：ContentControl 的触发器按它换页。</summary>
    public string ModeName => _isGuideMode ? GuideModeName : RouteModeName;

    /// <summary>是否直接进图鉴页（--codex）。</summary>
    public bool StartInGuide => _startup.DirectGuide;

    // ============================ 图鉴（关系网） ============================
    public int CodexDepth
    {
        get => _codexDepth;
        set
        {
            if (!Set(ref _codexDepth, _settings.ClampDepth(value))) return;
            if (IsGuideMode) _ = BuildGuideAsync();
        }
    }

    public bool GuideHighlightRoutes
    {
        get => _guideHighlightRoutes;
        set
        {
            if (!Set(ref _guideHighlightRoutes, value)) return;
            if (IsGuideMode) _ = BuildGuideAsync();
        }
    }

    public string GuideCenterText { get => _guideCenterText; private set => Set(ref _guideCenterText, value ?? ""); }
    public string GuideSummary { get => _guideSummary; private set => Set(ref _guideSummary, value ?? ""); }
    public CodexGraphVm? GuideGraph { get => _guideGraph; private set => Set(ref _guideGraph, value); }

    public string GuideFilter
    {
        get => _guideFilter;
        set { if (Set(ref _guideFilter, value ?? "")) RefreshGuideList(); }
    }

    public IReadOnlyList<CodexEntryVm> GuideEntries { get => _guideEntries; private set => Set(ref _guideEntries, value); }
    public string GuideListStatus { get => _guideListStatus; private set => Set(ref _guideListStatus, value ?? ""); }
    public bool GuideBusy { get => _guideBusy; private set => Set(ref _guideBusy, value); }
    public string GuideBusyText { get => _guideBusyText; private set => Set(ref _guideBusyText, value ?? ""); }

    /// <summary>当前中心卡的序号：瀑布流面板据此把那张卡滚进视口。</summary>
    public int GuideScrollIndex
    {
        get
        {
            for (var i = 0; i < _guideEntries.Count; i++)
                if (_guideEntries[i].Id == _guideCenterId) return i;
            return -1;
        }
    }

    public double Zoom
    {
        get => _zoom;
        set { if (Set(ref _zoom, _settings.ClampZoom(value))) Raise(nameof(ZoomText)); }
    }

    public string ZoomText => $"{_zoom * 100:0}%";

    private void ZoomBy(double factor) => Zoom *= factor;

    private void ResetGuide()
    {
        _guideBuilt = false;
        _guideCenterId = null;
        GuideGraph = null;
        GuideEntries = Array.Empty<CodexEntryVm>();
        GuideListStatus = "";
    }

    // ============================ 启动时的图鉴预热 ============================
    /// <summary>状态栏右下角那行静默进度（空＝不显示）。</summary>
    public string CodexLoadText
    {
        get => _codexLoadText;
        private set { if (Set(ref _codexLoadText, value ?? "")) Raise(nameof(HasCodexLoad)); }
    }

    public bool HasCodexLoad => _codexLoadText.Length > 0;

    /// <summary>开软件时就把图鉴挂到后台加载：不挡界面，进度只静默写在状态栏右下角。</summary>
    public void StartGuideWarmUp() => _warmTask ??= WarmUpGuideAsync();

    /// <summary>要图鉴数据的地方都先等这一趟；已经跑完时是瞬间返回。</summary>
    private Task EnsureGuideWarmUpAsync()
    {
        StartGuideWarmUp();
        return _warmTask!;
    }

    /// <summary>
    /// 后台把整份图鉴卡片分块建出来（真正耗时的是按宽度解码 341 张缩略图），
    /// 于是第一次进图鉴页不用再等一串解码；进度只更新状态栏，不弹窗也不盖遮罩。
    /// </summary>
    private async Task WarmUpGuideAsync()
    {
        if (_facade is null) return;

        var started = Environment.TickCount64;
        try
        {
            var presenter = _facade.Codex;
            var total = presenter.Count;
            const int chunk = 24;   // 一块就是一次进度的粒度

            for (var start = 0; start < total; start += chunk)
            {
                var from = start;
                await Task.Run(() => presenter.BuildEntryRange(from, chunk, _guideCenterId));
                CodexLoadText = $"图鉴加载 {Math.Min(start + chunk, total)}/{total}";
                await YieldUi();
            }

            Log.Write($"[codex] 后台预热完成 耗时 {Environment.TickCount64 - started} ms（{total} 只）");
        }
        catch (Exception ex)
        {
            Log.Write($"[codex] 预热失败：{ex.Message}");
        }
        finally
        {
            CodexLoadText = "";
        }
    }

    /// <summary>切到图鉴页：启动时那趟预热通常已经建好了，直接摆出来就是瞬间的事。</summary>
    public async Task ShowGuideAsync()
    {
        if (IsGuideMode) return;
        IsGuideMode = true;
        if (_guideBuilt) return;

        GuideBusyText = "正在生成图鉴…";
        GuideBusy = true;
        await YieldUi();

        var started = Environment.TickCount64;
        await EnsureGuideWarmUpAsync();
        RefreshGuideList();
        await BuildGuideAsync(useStart: true);
        _guideBuilt = true;
        GuideBusy = false;

        Log.Write($"[codex] 首次进入图鉴耗时 {Environment.TickCount64 - started} ms（节点 {GuideGraph?.Nodes.Count ?? 0}）");
    }

    /// <summary>换个中心：重建关系网、把它显示在详情栏，并让视图把中心滚回视口。</summary>
    public async Task SetCenterAsync(int id)
    {
        _guideCenterId = id;
        ShowDetail(id);

        GuideBusyText = $"正在展开 [{id}]…";
        GuideBusy = true;
        await YieldUi();

        await EnsureGuideWarmUpAsync();
        await BuildGuideAsync();
        GuideBusy = false;
        GuideBuilt = true;
        RecenterRequested?.Invoke();
    }

    /// <summary>按关键字筛出卡片（匹配规则与门槛见 Core/CodexPresenter）。</summary>
    public void RefreshGuideList()
    {
        if (_facade is null) return;

        var keyword = GuideFilter.Trim();
        var entries = _facade.Codex.BuildEntries(keyword, _guideCenterId);
        GuideEntries = entries;
        GuideListStatus = keyword.Length == 0
            ? $"共 {entries.Count} 只（按编号排）"
            : $"筛选到 {entries.Count} / {_facade.DigimonCount} 只（匹配度 ≥ {_settings.CodexFilterMinScore:P0}）";
        Raise(nameof(GuideScrollIndex));
    }

    /// <summary>定中心（起点 → 当前中心 → 上次选中 → 第一只），找不到返回 null。</summary>
    private int? ResolveGuideCenter(bool useStart)
    {
        if (_facade is null) return null;

        int? center = useStart ? _facade.ResolveName(StartText).Id : null;
        center ??= _guideCenterId;
        center ??= _lastDetailId;
        if (center is null || !_facade.HasDigimon(center.Value)) center = _facade.FirstId();
        return center is not null && _facade.HasDigimon(center.Value) ? center : null;
    }

    /// <summary>定好中心后交给核心层建图。</summary>
    private async Task BuildGuideAsync(bool useStart = false)
    {
        if (_facade is null)
        {
            GuideSummary = "还没有数据：找不到可用的 data.csv。";
            return;
        }

        var center = ResolveGuideCenter(useStart);
        if (center is null)
        {
            GuideSummary = "找不到可以当中心的数码兽。";
            return;
        }

        _guideCenterId = center;
        var routeIds = _facade.RouteIds(Routes.ToList());
        var depth = CodexDepth;
        var highlight = GuideHighlightRoutes;

        var build = await Task.Run(() => _facade.Codex.Build(center.Value, depth, highlight, routeIds));

        GuideCenterText = build.CenterText;
        GuideSummary = build.Summary;
        GuideGraph = build.Graph;
        foreach (var entry in GuideEntries) entry.IsCenter = entry.Id == _guideCenterId;
        Raise(nameof(GuideScrollIndex));
    }

    // ============================ 执行查询 ============================
    /// <summary>界面上填的选项 → 一次查询（约束语义在 Core/EvolutionPlanner.cs）。</summary>
    public PlannerQuery? BuildQuery(out string? error)
    {
        error = null;
        var query = new PlannerQuery
        {
            StartText = StartText.Trim(),
            K = K,
            Reverse = Reverse,
        };

        if (query.StartText.Length == 0)
        {
            error = "请先填写起点数码兽。";
            return null;
        }

        if (ModeMulti)
        {
            query.Mode = EndpointMode.Multi;
            query.MultiEnds.AddRange(RoutePresenter.SplitTokens(MultiEndText));
            if (query.MultiEnds.Count == 0)
            {
                error = "多终点模式至少要填一个终点。";
                return null;
            }
        }
        else if (ModeAny)
        {
            query.Mode = EndpointMode.Any;
        }
        else
        {
            query.EndText = EndText.Trim();
            if (query.EndText.Length == 0)
            {
                error = "请先填写终点数码兽（或改选多终点 / 任意终点模式）。";
                return null;
            }
        }

        foreach (var generation in Generations)
            if (generation.IsChecked) query.ExcludeGenerations.Add(generation.Name);

        foreach (var line in ViaText.Split('\n'))
        {
            var names = RoutePresenter.SplitTokens(line);
            if (names.Count > 0) query.ViaGroups.Add(names);
        }

        query.Skills.AddRange(SelectedSkills());

        if (query.Mode == EndpointMode.Any && query.ViaGroups.Count == 0 && query.Skills.Count == 0)
        {
            error = "「任意终点」必须配合「途经数码兽」或「需收集继承技」使用。";
            return null;
        }

        return query;
    }

    public async Task RunAsync()
    {
        if (_facade is null)
        {
            ShowStatus("数据尚未加载：找不到可用的 data.csv。", ResourceKeys.ErrorColor);
            return;
        }

        var query = BuildQuery(out var validationError);
        if (query is null)
        {
            ShowStatus(validationError!, ResourceKeys.ErrorColor);
            return;
        }

        _loading = true;
        RefreshCommands();
        try
        {
            var wanted = SelectedSkills();
            var facade = _facade;
            var result = await Task.Run(() => facade.Search(query, wanted));

            TextOutput = string.Join(Environment.NewLine, result.TextLines);
            Routes.Clear();
            foreach (var route in result.Routes) Routes.Add(route);

            SelectedDetail = null;
            RefreshRegion();

            // 图鉴里靠这些路线做高亮，算完刷新一次（不重置用户拖动的位置）
            if (IsGuideMode && GuideGraph is not null) await BuildGuideAsync();

            _state.LastK = K;
            _state.Save();

            if (!result.Ok)
            {
                var message = result.Error ?? "找不到可用进化路线";
                ShowStatus(message, ResourceKeys.ErrorColor);
                EmptyHint = message;
                return;
            }

            var steps = string.Join(" / ", result.Routes.Select(route => route.Steps.Count));
            var summary = $"共 {result.Routes.Count} 条路线 · 节点数 {steps} · {result.ElapsedMs:0} ms";
            var warnings = result.Messages.Where(message => message.StartsWith("警告")).ToList();
            ShowStatus(warnings.Count > 0 ? $"{summary} · {string.Join(" ｜ ", warnings)}" : summary,
                warnings.Count > 0 ? ResourceKeys.WarnColor : ResourceKeys.SubtleColor);
            EmptyHint = "填写起终点后点击「计算路线」。";
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, ResourceKeys.ErrorColor);
        }
        finally
        {
            _loading = false;
            RefreshCommands();
        }
    }

    // ============================ 图片目录 / 导出 ============================
    public string AssetsDirectory
    {
        get => _assetsDirectory;
        set
        {
            if (!Set(ref _assetsDirectory, value ?? "")) return;
            _facade?.UseImageDirectory(_assetsDirectory.Trim());
            DetailsHint = $"点击路线里的数码兽查看详情。图片目录：{_assetsDirectory.Trim()}";
            _state.AssetsDirectory = _assetsDirectory.Trim();
        }
    }

    private void Export()
    {
        if (TextOutput.Length == 0)
        {
            ShowStatus("还没有可导出的结果。", ResourceKeys.WarnColor);
            return;
        }

        var path = _dialogs.SaveText("导出进化路线", "进化路线.txt", TextOutput);
        if (path is not null) ShowStatus($"已导出到 {path}", ResourceKeys.StartColor);
    }

    // ============================ 状态栏 / 顶栏 ============================
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value ?? ""); }
    public string StatusKey { get => _statusKey; private set => Set(ref _statusKey, value); }
    public string EnvText { get => _envText; private set => Set(ref _envText, value ?? ""); }

    public void ShowStatus(string text, string key = ResourceKeys.SubtleColor)
    {
        StatusText = text.Replace("\n", " ");
        StatusKey = key;
    }

    // ============================ 截图验收 ============================
    /// <summary>--screenshot 用的状态准备：把界面摆成要被拍的样子。</summary>
    public async Task PrepareForCaptureAsync()
    {
        ShowStatus("正在加载数据…");
        await LoadCoreAsync();
        if (_facade is null)
        {
            ShowStatus(LastError ?? "加载数据失败", ResourceKeys.ErrorColor);
            return;
        }

        StartText = _startup.StartText is { Length: > 0 } start ? start : "亚古兽";
        EndText = _startup.EndText is { Length: > 0 } end ? end : "战斗暴龙兽";
        await RunAsync();

        if (_startup.CodexDepth is { } depth) CodexDepth = depth;

        if (_startup.DirectGuide)
        {
            await ShowGuideAsync();
            if (_startup.CodexFilter is { Length: > 0 } filter) GuideFilter = filter;
            if (_guideCenterId is { } center) ShowDetail(center);
        }
        else if (Routes.Count > 0 && Routes[0].Steps.Count > 1)
        {
            ShowDetail(Routes[0].Steps[^1].Id);
        }
    }
}