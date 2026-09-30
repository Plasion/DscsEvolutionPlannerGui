namespace DscsEvolutionPlanner.ViewModel;

/// <summary>「排除世代」列表里的一项。</summary>
public sealed class GenerationVm : NotifyBase
{
    public string Name { get; init; } = "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }
}

/// <summary>继承技列表里的一项（多选下拉用）。</summary>
public sealed class SkillItemVm : NotifyBase
{
    public string Name { get; init; } = "";

    /// <summary>全部数码兽里最早能学到这个技能的等级（例如 "最早 Lv5"）。</summary>
    public string LevelText { get; init; } = "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }
}