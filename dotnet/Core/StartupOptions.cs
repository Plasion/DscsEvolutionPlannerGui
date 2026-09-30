using System;
using System.Collections.Generic;

namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 一次启动的输入：命令行开关与上次保存的用户状态合成后的结果。
/// 由 AppBootstrapper 构造一次，之后只读。
/// </summary>
public sealed class StartupOptions
{
    /// <summary>--data 指定的数据文件（null＝按查找顺序找）。</summary>
    public string? DataFile { get; init; }

    /// <summary>--assets 或上次用的图片目录。</summary>
    public string? ImageDirectory { get; init; }

    /// <summary>上次保存的窗口尺寸（null＝用 config.json 的默认值）。</summary>
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }

    /// <summary>上次保存的栏宽。</summary>
    public double? LeftColumnWidth { get; init; }
    public double? DetailsColumnWidth { get; init; }

    /// <summary>上次用的路径条数。</summary>
    public int? K { get; init; }

    /// <summary>--start / --end：预填起终点。</summary>
    public string? StartText { get; init; }
    public string? EndText { get; init; }

    /// <summary>--skill：启动时预选的继承技（可多个）。</summary>
    public IReadOnlyList<string> SelectedSkills { get; init; } = Array.Empty<string>();

    /// <summary>--codex：截图验收时直接进图鉴页。</summary>
    public bool DirectGuide { get; init; }

    /// <summary>--depth：图鉴的展开深度。</summary>
    public int? CodexDepth { get; init; }

    /// <summary>--codex-filter：截图验收时预填图鉴筛选框。</summary>
    public string? CodexFilter { get; init; }
}