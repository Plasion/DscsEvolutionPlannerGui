using System;

namespace DscsEvolutionPlanner.Core;

/// <summary>本机可调参数的只读快照。视图模型读它，不直接碰 config.json。</summary>
public sealed class AppSettings
{
    public int DefaultK { get; init; } = 3;
    public int MaxK { get; init; } = 50;
    public int SuggestLimit { get; init; } = 24;
    public int CodexFilterLimit { get; init; } = 400;
    public double CodexFilterMinScore { get; init; } = 0.7;
    public double ExactScore { get; init; } = 0.999;
    public double GoodScore { get; init; } = 0.6;

    public int DefaultDepth { get; init; } = 2;
    public int MaxDepth { get; init; } = 4;
    public int MaxNodes { get; init; } = 160;
    public double ThumbnailWidth { get; init; } = 160;
    public double ColumnGap { get; init; } = 76;
    public double RowGap { get; init; } = 14;
    public double GraphPadding { get; init; } = 360;
    public double MinZoom { get; init; } = 0.4;
    public double MaxZoom { get; init; } = 2.0;
    public double ZoomButtonStep { get; init; } = 1.15;
    public double ZoomWheelStep { get; init; } = 1.12;
    public int DoubleClickMs { get; init; } = 400;

    public double WindowWidth { get; init; } = 1480;
    public double WindowHeight { get; init; } = 930;
    public double MinWindowWidth { get; init; } = 1120;
    public double MinWindowHeight { get; init; } = 720;
    public double LeftColumnWidth { get; init; } = 392;
    public double MinLeftColumnWidth { get; init; } = 200;
    public double DetailsColumnWidth { get; init; } = 380;
    public double MinDetailsColumnWidth { get; init; } = 300;

    /// <summary>从 config.json 的当前值取一份快照。</summary>
    public static AppSettings Current()
    {
        var config = AppConfig.Current;
        var search = config.Search;
        var codex = config.Codex;
        var layout = config.Layout;

        return new AppSettings
        {
            DefaultK = search.DefaultK,
            MaxK = search.MaxK,
            SuggestLimit = search.SuggestLimit,
            CodexFilterLimit = search.CodexFilterLimit,
            CodexFilterMinScore = search.CodexFilterMinScore,
            ExactScore = search.ExactScore,
            GoodScore = search.GoodScore,

            DefaultDepth = codex.DefaultDepth,
            MaxDepth = codex.MaxDepth,
            MaxNodes = codex.MaxNodes,
            ThumbnailWidth = codex.ThumbnailWidth,
            ColumnGap = codex.ColumnGap,
            RowGap = codex.RowGap,
            GraphPadding = codex.GraphPadding,
            MinZoom = codex.MinZoom,
            MaxZoom = codex.MaxZoom,
            ZoomButtonStep = codex.ZoomButtonStep,
            ZoomWheelStep = codex.ZoomWheelStep,
            DoubleClickMs = codex.DoubleClickMs,

            WindowWidth = layout.WindowWidth,
            WindowHeight = layout.WindowHeight,
            MinWindowWidth = layout.MinWindowWidth,
            MinWindowHeight = layout.MinWindowHeight,
            LeftColumnWidth = layout.LeftColumnWidth,
            MinLeftColumnWidth = layout.MinLeftColumnWidth,
            DetailsColumnWidth = layout.DetailsColumnWidth,
            MinDetailsColumnWidth = layout.MinDetailsColumnWidth,
        };
    }

    public int ClampK(int value) => Math.Clamp(value, 1, Math.Max(1, MaxK));

    public int ClampDepth(int value) => Math.Clamp(value, 1, Math.Max(1, MaxDepth));

    public double ClampZoom(double value) => Math.Clamp(value, MinZoom, Math.Max(MinZoom, MaxZoom));
}