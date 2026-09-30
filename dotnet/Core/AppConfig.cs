using System;
using System.IO;
using System.Text.Json;

namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 可调参数（阈值、上限、默认尺寸）：读 config.json，缺文件或缺字段就用这里的默认值。
/// UserState（上次的路径、栏宽）在 <see cref="UserState"/> 里，两者分开。
/// </summary>
public sealed class AppConfig
{
    public SearchSection Search { get; set; } = new();
    public CodexSection Codex { get; set; } = new();
    public LayoutSection Layout { get; set; } = new();

    /// <summary>进程内共用实例，启动时 Load() 一次。</summary>
    public static AppConfig Current { get; private set; } = new();

    /// <summary>实际读到的配置文件；null＝没找到（全部用默认值）。</summary>
    public static string? FilePath { get; private set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,   // 配置文件里可以写 // 注释
        AllowTrailingCommas = true,
    };

    public static void Load()
    {
        FilePath = Find();
        if (FilePath is null)
        {
            Log.Write("[config] 没找到 config.json，全部用内置默认值");
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
            Log.Write($"[config] 已加载 {FilePath}");
        }
        catch (Exception ex)
        {
            Current = new AppConfig();
            Log.Write($"[config] {FilePath} 解析失败，改用默认值：{ex.Message}");
        }
    }

    /// <summary>从 exe 目录往上最多找 6 层（开发时改 dotnet/config.json 不必先构建）。</summary>
    private static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config.json");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>搜索与输入匹配。</summary>
    public sealed class SearchSection
    {
        /// <summary>「重置」后的路径条数，也是首次使用的默认值。</summary>
        public int DefaultK { get; set; } = 3;

        /// <summary>路径条数上限（界面输入会夹到 1..MaxK）。</summary>
        public int MaxK { get; set; } = 50;

        /// <summary>起点 / 终点候选下拉最多几条。</summary>
        public int SuggestLimit { get; set; } = 24;

        /// <summary>图鉴筛选最多取多少候选（要全部命中项，所以给得宽）。</summary>
        public int CodexFilterLimit { get; set; } = 400;

        /// <summary>图鉴筛选的匹配度门槛：低于它的不显示；0＝不过滤。</summary>
        public double CodexFilterMinScore { get; set; } = 0.7;

        /// <summary>提示里算「完全匹配」的分数。</summary>
        public double ExactScore { get; set; } = 0.999;

        /// <summary>提示里算「相似度」（而不是「弱匹配」）的分数。</summary>
        public double GoodScore { get; set; } = 0.6;
    }

    /// <summary>图鉴（关系网 + 列表）。</summary>
    public sealed class CodexSection
    {
        public int DefaultDepth { get; set; } = 2;
        public int MaxDepth { get; set; } = 4;

        /// <summary>关系网节点上限，超出就截断并在图上提示。</summary>
        public int MaxNodes { get; set; } = 160;

        public double MinZoom { get; set; } = 0.4;
        public double MaxZoom { get; set; } = 2.0;
        public double ZoomButtonStep { get; set; } = 1.15;
        public double ZoomWheelStep { get; set; } = 1.12;

        /// <summary>拖动平移的触发阈值（像素）。</summary>
        public double PanThreshold { get; set; } = 5;

        /// <summary>节点双击判定窗口（毫秒）。</summary>
        public int DoubleClickMs { get; set; } = 400;

        /// <summary>关系网四周留白（同时也是可拖动范围）。</summary>
        public double GraphPadding { get; set; } = 360;

        public double ColumnGap { get; set; } = 44;
        public double RowGap { get; set; } = 8;

        /// <summary>缩略图解码宽度。</summary>
        public int ThumbnailWidth { get; set; } = 160;
    }

    /// <summary>窗口与栏宽。</summary>
    public sealed class LayoutSection
    {
        public double WindowWidth { get; set; } = 1480;
        public double WindowHeight { get; set; } = 930;
        public double MinWindowWidth { get; set; } = 1120;
        public double MinWindowHeight { get; set; } = 720;

        public double LeftColumnWidth { get; set; } = 392;
        public double MinLeftColumnWidth { get; set; } = 200;
        public double DetailsColumnWidth { get; set; } = 380;
        public double MinDetailsColumnWidth { get; set; } = 300;
    }
}
