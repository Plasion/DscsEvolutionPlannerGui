namespace DscsEvolutionPlanner.Core;

/// <summary>名字输入框下拉里的一条候选：名称 + 匹配度。</summary>
public sealed record NameSuggestion(int Id, string Name, string Generation, double Score)
{
    public string IdText => "#" + Id;

    /// <summary>右下/右侧显示的匹配度。</summary>
    public string ScoreText => $"{Score * 100:0}%";

    /// <summary>完全匹配（1.0）时给个徽标用。</summary>
    public bool IsExact => Score >= 0.999;

    public string Tip => $"[{Id}] {Name} · {Generation} · 匹配度 {ScoreText}";
}
