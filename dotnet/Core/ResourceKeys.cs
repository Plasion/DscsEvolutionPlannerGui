namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 主题资源键：核心与视图模型只产出「用哪个颜色」的语义键，真正解析成画刷由视图层的转换器完成，
/// 这样算法与视图模型都不必引用 WPF 资源字典。键名与 App.xaml 里定义的画刷同名。
/// </summary>
public static class ResourceKeys
{
    public const string TextColor = "TextBrush";
    public const string SubtleColor = "SubtleBrush";
    public const string FaintColor = "FaintBrush";
    public const string AccentColor = "AccentBrush";
    public const string StartColor = "StartBrush";
    public const string EndColor = "EndBrush";
    public const string ViaColor = "ViaBrush";
    public const string WarnColor = "WarnBrush";
    public const string ErrorColor = "ErrorBrush";
    public const string UnknownColor = "ArrowUnknownBrush";
    public const string BorderColor = "BorderBrush2";

    /// <summary>路线卡片上「起 / 终 / 途」的角色语义（Presenter 内部使用）。</summary>
    public const string Start = "起";
    public const string End = "终";
    public const string Via = "途";

    /// <summary>关系网连线语义（Presenter 内部使用）。</summary>
    public const string Evolve = "进化";
    public const string Devolve = "退化";
    public const string ModeChange = "形态";

    /// <summary>一个颜色键集合：任何没给出的语义都回落到「未知色」。</summary>
    public static string Resolve(string semantic) => semantic switch
    {
        Start or Evolve => StartColor,
        End or Devolve => EndColor,
        Via or ModeChange => ViaColor,
        TextColor or SubtleColor or FaintColor or AccentColor or StartColor or EndColor or ViaColor
            or WarnColor or ErrorColor or UnknownColor or BorderColor => semantic,
        _ => UnknownColor,
    };
}