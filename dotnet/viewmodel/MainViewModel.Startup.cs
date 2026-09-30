using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DscsEvolutionPlanner.Core;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>壳的视图模型：启动流程、数据加载与查询表单。</summary>
public sealed partial class MainViewModel
{
    private string _startText = "";
    private string _endText = "";
    private string _multiEndText = "";
    private string _viaText = "";
    private int _k = 3;
    private bool _reverse;
    private bool _modeSingle = true;
    private bool _modeMulti;
    private bool _modeAny;
    private string _startHint = "";
    private string _startHintKey = ResourceKeys.FaintColor;
    private string _endHint = "";
    private string _endHintKey = ResourceKeys.FaintColor;

    // ------------------------------------------------------------------ 启动
    /// <summary>启动流程：读 data.csv → 刷新界面 → 预填 → 算一次。全程异步，界面不卡。</summary>
    public async Task InitializeAsync()
    {
        ShowStatus("正在加载数据…");
        DataStatus = "正在读取 data.csv…";
        DataStatusKey = ResourceKeys.FaintColor;
        await LoadCoreAsync();

        if (_facade is null)
        {
            ShowDataError(LastError ?? "加载数据失败");
            return;
        }

        StartText = _startup.StartText is { Length: > 0 } start ? start : "亚古兽";
        EndText = _startup.EndText is { Length: > 0 } end ? end : "战斗暴龙兽";
        await RunAsync();
    }

    /// <summary>上次失败的原因（也给截图日志看）。</summary>
    public string? LastError { get; private set; }

    private async Task LoadCoreAsync()
    {
        var path = _dataSource.Locate(_state.DataPath);
        if (path is null)
        {
            LastError = "找不到 data.csv。\n\n它应该放在 exe 同级目录，或仓库的 py/ 目录下（也可用 --data 指定）。";
            return;
        }

        _loading = true;
        RefreshCommands();
        try
        {
            var catalog = await Task.Run(() => _dataSource.Read(path));
            var images = new ImageStore { Directory = AssetsDirectory.Trim() };
            _facade = new PlannerFacade(catalog, images, _settings);

            _state.DataPath = path;
            _state.Save();
            LastError = null;
            ApplyCatalog();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            _loading = false;
            RefreshCommands();
        }
    }

    /// <summary>数据就绪后刷新界面：世代 / 技能列表、状态与提示。</summary>
    private void ApplyCatalog()
    {
        var facade = _facade!;

        Generations.Clear();
        foreach (var generation in facade.Generations)
            Generations.Add(new GenerationVm { Name = generation });

        Skills.Clear();
        foreach (var (name, level) in facade.Skills())
            Skills.Add(new SkillItemVm { Name = name, LevelText = level });
        ApplySkillPresets();
        RefreshVisibleSkills();

        var pinyin = facade.PinyinLoaded ? "内置拼音表" : "仅字符匹配（缺 Data/PinyinTable.json）";
        EnvText = $"{facade.DigimonCount} 只数码兽 · {pinyin}";
        DataStatus = $"✓ 已加载 {facade.DigimonCount} 只数码兽（{pinyin}）\n{facade.DataPath}";
        DataStatusKey = ResourceKeys.StartColor;
        DetailsHint = $"点击路线里的数码兽查看详情。图片目录：{AssetsDirectory}";
        SelectedDetail = null;
        ResetGuide();

        var warnings = new List<string>(facade.Warnings);
        if (facade.PinyinStale)
            warnings.Add("警告：Data/PinyinTable.json 与 data.csv 不同源，拼音匹配可能不准");
        ShowStatus(warnings.Count > 0 ? string.Join(" ｜ ", warnings) : "数据已就绪。",
            warnings.Count > 0 ? ResourceKeys.WarnColor : ResourceKeys.SubtleColor);
    }

    /// <summary>--skill 预选：名字与候选技能对齐后勾上。</summary>
    private void ApplySkillPresets()
    {
        if (_startup.SelectedSkills.Count == 0) return;
        foreach (var text in _startup.SelectedSkills)
        {
            var key = EvolutionPlanner.NormalizeSkillName(text);
            var hit = System.Linq.Enumerable.FirstOrDefault(Skills, skill => string.Equals(skill.Name, key, StringComparison.Ordinal));
            if (hit is not null) hit.IsChecked = true;
        }
    }

    private void ShowDataError(string message)
    {
        ShowStatus(message.Split('\n')[0], ResourceKeys.ErrorColor);
        DataStatus = "✗ " + message.Replace("\n", " ");
        DataStatusKey = ResourceKeys.ErrorColor;
        EmptyHint = "加载数据失败：\n" + message;
        RefreshRegion();
        _dialogs.Warn("无法加载数据", message);
    }

    /// <summary>重新加载数据：读盘 + 重建索引，再算一次。</summary>
    public async Task LoadAsync()
    {
        DataStatus = "正在读取 data.csv…";
        DataStatusKey = ResourceKeys.FaintColor;
        await LoadCoreAsync();

        if (_facade is null)
        {
            ShowDataError(LastError ?? "加载数据失败");
            return;
        }

        if (StartText.Trim().Length == 0 && EndText.Trim().Length == 0)
        {
            StartText = "亚古兽";
            EndText = "战斗暴龙兽";
        }
        await RunAsync();
    }

    // ------------------------------------------------------------------ 查询表单
    public string StartText
    {
        get => _startText;
        set { if (Set(ref _startText, value ?? "")) UpdateHint(value, start: true); }
    }

    public string EndText
    {
        get => _endText;
        set { if (Set(ref _endText, value ?? "")) UpdateHint(value, start: false); }
    }

    public string MultiEndText { get => _multiEndText; set => Set(ref _multiEndText, value ?? ""); }

    public string ViaText { get => _viaText; set => Set(ref _viaText, value ?? ""); }

    public int K { get => _k; set => Set(ref _k, _settings.ClampK(value)); }

    public bool Reverse { get => _reverse; set => Set(ref _reverse, value); }

    public bool ModeSingle
    {
        get => _modeSingle;
        set { if (Set(ref _modeSingle, value) && value) RefreshMode(); }
    }

    public bool ModeMulti
    {
        get => _modeMulti;
        set { if (Set(ref _modeMulti, value) && value) RefreshMode(); }
    }

    public bool ModeAny
    {
        get => _modeAny;
        set { if (Set(ref _modeAny, value) && value) RefreshMode(); }
    }

    public bool IsEndEnabled => _modeSingle;
    public bool IsMultiEndEnabled => _modeMulti;

    private void RefreshMode()
    {
        Raise(nameof(IsEndEnabled));
        Raise(nameof(IsMultiEndEnabled));
    }

    /// <summary>起点 / 终点候选（候选下拉控件通过它取数据）。</summary>
    public IReadOnlyList<NameSuggestion> SuggestStart(string? text) =>
        _facade?.Suggest(text) ?? Array.Empty<NameSuggestion>();

    public IReadOnlyList<NameSuggestion> SuggestEnd(string? text) => SuggestStart(text);

    // ------------------------------------------------------------------ 即时解析提示
    public string StartHint { get => _startHint; private set => Set(ref _startHint, value); }
    public string StartHintKey { get => _startHintKey; private set => Set(ref _startHintKey, value); }
    public string EndHint { get => _endHint; private set => Set(ref _endHint, value); }
    public string EndHintKey { get => _endHintKey; private set => Set(ref _endHintKey, value); }

    private void UpdateHint(string? text, bool start)
    {
        var (hint, key) = _facade is null ? ("", ResourceKeys.FaintColor) : _facade.Describe(text);
        if (start) { StartHint = hint; StartHintKey = key; }
        else { EndHint = hint; EndHintKey = key; }
    }

    // ------------------------------------------------------------------ 列表数据
    public System.Collections.ObjectModel.ObservableCollection<GenerationVm> Generations { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<SkillItemVm> Skills { get; } = new();

    private IReadOnlyList<SkillItemVm> _visibleSkills = Array.Empty<SkillItemVm>();
    public IReadOnlyList<SkillItemVm> VisibleSkills { get => _visibleSkills; private set => Set(ref _visibleSkills, value); }

    private string _skillFilter = "";
    public string SkillFilter
    {
        get => _skillFilter;
        set { if (Set(ref _skillFilter, value ?? "")) RefreshVisibleSkills(); }
    }

    /// <summary>弹层上的汇总文字（已选技能名，或占位提示）。</summary>
    public string SkillSummary
    {
        get
        {
            var selected = SelectedSkills();
            if (selected.Count == 0) return "点这里选择继承技…";
            if (selected.Count <= 2) return "已选：" + string.Join("、", selected);
            return $"已选 {selected.Count} 项：" + string.Join("、", System.Linq.Enumerable.Take(selected, 2)) + " 等";
        }
    }

    public IReadOnlyList<string> SelectedSkills()
    {
        var list = new List<string>();
        foreach (var skill in Skills)
            if (skill.IsChecked) list.Add(skill.Name);
        return list;
    }

    private bool _skillsHooked;

    private void RefreshVisibleSkills()
    {
        if (!_skillsHooked)
        {
            foreach (var skill in Skills) skill.PropertyChanged += OnSkillChanged;
            _skillsHooked = true;
        }

        var keyword = SkillFilter.Trim();
        VisibleSkills = keyword.Length == 0
            ? System.Linq.Enumerable.ToList(Skills)
            : System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(Skills, skill => skill.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        Raise(nameof(SkillSummary));
    }

    private void OnSkillChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SkillItemVm.IsChecked)) return;
        Raise(nameof(SkillSummary));
        Raise(nameof(HasSelectedSkills));
    }

    public bool HasSelectedSkills => SelectedSkills().Count > 0;

    private void SelectAllVisibleSkills()
    {
        foreach (var skill in VisibleSkills) skill.IsChecked = true;
    }

    private void ClearSkills()
    {
        foreach (var skill in Skills) skill.IsChecked = false;
    }
}