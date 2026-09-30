using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using DscsEvolutionPlanner.Core;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>属性表的一行：属性名 + Lv.1 + Lv.99 + 差值。</summary>
public sealed class StatRowVm
{
    public string Label { get; init; } = "";
    public string Lv1 { get; init; } = "—";
    public string Lv99 { get; init; } = "—";
    public string Delta { get; init; } = "";
}

/// <summary>详情栏里的一行继承技：技能名 + 这只数码兽学会它的等级。</summary>
public sealed class SkillRowVm
{
    public string Name { get; init; } = "";

    /// <summary>学会等级，形如 "Lv12"；data.csv 里没写等级时是 "Lv?"。</summary>
    public string LevelText { get; init; } = "";

    public string LevelTip { get; init; } = "";

    /// <summary>等级缺失时用淡色，有等级用紫色（主题资源键）。</summary>
    public string LevelKey { get; init; } = ResourceKeys.FaintColor;
}

/// <summary>右侧详情栏的内容（点中卡片后由核心层生成）。</summary>
public sealed class DigimonDetailVm
{
    private static readonly string[] StatOrder = { "HP", "SP", "ATK", "INT", "DEF", "SPD" };

    public string Title { get; init; } = "";
    public string AliasText { get; init; } = "";
    public bool HasAlias => AliasText.Length > 0;

    public string TraitsText { get; init; } = "";
    public string SlotText { get; init; } = "";

    public string SsName { get; init; } = "";
    public string SsDesc { get; init; } = "";
    public bool HasSs => SsName.Length > 0;

    public string MoveName { get; init; } = "";
    public string MoveDesc { get; init; } = "";
    public bool HasMove => MoveName.Length > 0;

    public List<StatRowVm> Stats { get; init; } = new();
    public bool HasStats => Stats.Count > 0;

    /// <summary>继承技（一行一条，界面里右侧显示等级）。</summary>
    public List<SkillRowVm> Skills { get; init; } = new();
    public bool HasSkills => Skills.Count > 0;

    /// <summary>有技能缺等级时才显示的脚注（说明 "Lv?" 是什么意思）。</summary>
    public string SkillNote { get; init; } = "";
    public bool HasSkillNote => SkillNote.Length > 0;

    public BitmapSource? Image { get; init; }
    public bool HasImage => Image is not null;

    public static DigimonDetailVm Create(Digimon info, BitmapSource? image)
    {
        var traits = new List<string> { info.DisplayGeneration };
        if (info.Race.Length > 0) traits.Add(info.Race);
        if (info.Attribute.Length > 0) traits.Add(info.Attribute);

        var slots = new List<string>();
        if (info.Growth.Length > 0) slots.Add($"成长 {info.Growth}");
        slots.Add($"装备凹槽 {info.Slot}");
        slots.Add($"消耗容量 {info.Capacity}");

        var stats = new List<StatRowVm>();
        foreach (var key in StatOrder)
        {
            var hasLv1 = info.Lv1.TryGetValue(key, out var lv1) && !string.IsNullOrEmpty(lv1);
            var hasLv99 = info.Lv99.TryGetValue(key, out var lv99) && !string.IsNullOrEmpty(lv99);
            if (!hasLv1 && !hasLv99) continue;

            var delta = "";
            if (hasLv1 && hasLv99 &&
                int.TryParse(lv1, out var low) && int.TryParse(lv99, out var high))
                delta = high >= low ? $"+{high - low}" : (high - low).ToString();

            stats.Add(new StatRowVm
            {
                Label = key,
                Lv1 = hasLv1 ? lv1! : "—",
                Lv99 = hasLv99 ? lv99! : "—",
                Delta = delta,
            });
        }

        var skillRows = info.InheritSkills.Select(skill => new SkillRowVm
        {
            Name = skill.Name,
            LevelText = skill.Level is { } level ? $"Lv{level}" : "Lv?",
            LevelTip = skill.Level is { } known
                ? $"{skill.Name}：这只数码兽在 Lv{known} 学会它"
                : $"{skill.Name}：data.csv 没写这只数码兽学会它的等级",
            LevelKey = skill.Level is null ? ResourceKeys.FaintColor : ResourceKeys.ViaColor,
        }).ToList();

        return new DigimonDetailVm
        {
            Title = $"[{info.Id}] {info.Name}" + (string.IsNullOrEmpty(info.Alias) ? "" : $"（{info.Alias}）"),
            AliasText = string.IsNullOrEmpty(info.Alias) ? "" : $"别名：{info.Alias}",
            TraitsText = string.Join(" · ", traits),
            SlotText = string.Join(" · ", slots),
            SsName = info.SsName,
            SsDesc = info.SsDesc,
            MoveName = info.MoveName,
            MoveDesc = info.MoveDesc,
            Stats = stats,
            Skills = skillRows,
            SkillNote = skillRows.Any(row => row.LevelText == "Lv?")
                ? $"「Lv?」= data.csv 里没有写这只数码兽学会它的等级（{skillRows.Count(row => row.LevelText == "Lv?")}/{skillRows.Count} 条）"
                : "",
            Image = image,
        };
    }
}