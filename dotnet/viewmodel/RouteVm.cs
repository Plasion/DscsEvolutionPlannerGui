using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>卡片上的一枚「可提供的继承技」标（技能名 + 学会等级）。</summary>
public sealed class SkillChipVm
{
    public string Text { get; init; } = "";
    public string Tip { get; init; } = "";
}

/// <summary>卡片右上角的小标签（起 / 终 / 途）。颜色由视图按 Key 解析主题资源。</summary>
public sealed class BadgeVm
{
    public string Text { get; init; } = "";

    /// <summary>主题资源键（起 / 终 / 途）。</summary>
    public string Key { get; init; } = "";
}

/// <summary>路线里的一张卡片（位置由 RouteChain 算，这里只有内容）。</summary>
public sealed class RouteStepVm
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Generation { get; init; } = "";
    public string IdText => "#" + Id;

    /// <summary>卡片上那行「#18 · 成长期」（世代缺了就只留编号）。</summary>
    public string MetaText => Generation.Length > 0 ? $"{IdText} · {Generation}" : IdText;

    public BitmapSource? Image { get; init; }
    public bool HasImage => Image is not null;

    // 与上一张卡片的关系（箭头由 RouteChain 摆放，换行时换成竖箭头）
    public string ArrowGlyph { get; init; } = "";
    public string ArrowLabel { get; init; } = "";

    /// <summary>箭头颜色（主题资源键）。</summary>
    public string ArrowKey { get; init; } = "";

    /// <summary>这只数码兽能提供的、本次查询里选中的继承技（技能名 + 学会等级）。</summary>
    public List<SkillChipVm> Skills { get; init; } = new();
    public bool HasSkills => Skills.Count > 0;

    // 起 / 终 / 途
    public List<BadgeVm> Badges { get; init; } = new();

    /// <summary>卡片顶上那条角色色条（null＝不显示）。</summary>
    public string? RoleBarKey { get; init; }
    public bool HasRoleBar => RoleBarKey is { Length: > 0 };
}

/// <summary>一条进化路线。</summary>
public sealed class RouteVm
{
    public int Index { get; init; }
    public string Title => $"进化路线 {Index}";
    public string Subtitle { get; init; } = "";
    public List<RouteStepVm> Steps { get; init; } = new();

    /// <summary>沿途收集选中的继承技的摘要（没选技能时为空）。</summary>
    public string GoalText { get; init; } = "";
    public bool HasGoal => GoalText.Length > 0;
}