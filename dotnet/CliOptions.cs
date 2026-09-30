using System;
using System.Collections.Generic;
using System.Linq;
using DscsEvolutionPlanner.Core;

namespace DscsEvolutionPlanner;

/// <summary>命令行开关（也给 --screenshot 验收用）。</summary>
public sealed class CliOptions
{
    public bool ShowHelp;
    public string? ScreenshotPath;
    public string? LogPath;
    public string? DataPath;
    public string? AssetsPath;
    public string? OutPath;
    public List<string> CliArguments { get; } = new();
    public List<string> Skills { get; } = new();
    public string? StartText;
    public string? EndText;
    public int? K;
    public bool CodexMode;
    public int? CodexDepth;
    public string? CodexFilter;
    public double? WindowWidth;
    public double? WindowHeight;

    public const string HelpText = """
        数码兽进化路线规划器（WPF 界面）

        用法：DscsEvolutionPlanner.exe [选项]
          --data <csv>        指定 data.csv（默认：exe 同级 → 向上找 py/data.csv）
          --assets <dir>      指定图片目录（{编号}.png）
          --start <名字>      启动时预填起点
          --end <名字>        启动时预填终点
          --skill <技能名>    启动时预选继承技（可重复：--skill A --skill B）
          --k <n>             启动时预填路径条数
          --window 1600x1000  指定初始窗口尺寸
          --codex             截图时直接进「图鉴」模式
          --depth <1-4>       图鉴的展开深度（默认 2）
          --codex-filter <文本> 截图时预填图鉴左侧的筛选框
          --screenshot <png>  离屏渲染一张界面截图后退出（验收用）
          --cli <参数...>     无界面执行一次查询（参数写法同 planner.py）
          --out <file>        --cli 的输出写到文件（默认写标准输出）
          --log <file>        把诊断日志写到文件
          -h, --help          显示本帮助

        算法全部在程序内部（Core/EvolutionPlanner.cs），运行时不需要 Python；
        只读一份 data.csv（数据）与 Data/PinyinTable.json（拼音表）。
        """;

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        Log.Write($"解析参数：{string.Join(' ', args)}");
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            Log.Write($"  参数 [{i}] = {arg}");
            switch (arg)
            {
                case "-h":
                case "--help":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "--data":
                    options.DataPath = Next();
                    break;
                case "--out":
                    options.OutPath = Next();
                    break;
                case "--cli":
                {
                    // 后面全部是查询参数；允许在其中用 --out <file> 指定输出文件
                    var queryArgs = args.Skip(i + 1).ToList();
                    var outIndex = queryArgs.LastIndexOf("--out");
                    if (outIndex >= 0 && outIndex + 1 < queryArgs.Count)
                    {
                        options.OutPath = queryArgs[outIndex + 1];
                        queryArgs.RemoveRange(outIndex, 2);
                    }
                    options.CliArguments.AddRange(queryArgs);
                    Log.Write($"  --cli 收到 {options.CliArguments.Count} 个查询参数，OutPath={options.OutPath ?? "<标准输出>"}");
                    i = args.Length;
                    break;
                }
                case "--assets":
                    options.AssetsPath = Next();
                    break;
                case "--start":
                    options.StartText = Next();
                    break;
                case "--skill":
                    if (Next() is { Length: > 0 } skill) options.Skills.Add(skill);
                    break;
                case "--end":
                    options.EndText = Next();
                    break;
                case "--k":
                    if (int.TryParse(Next(), out var k)) options.K = k;
                    break;
                case "--codex":
                    options.CodexMode = true;
                    break;
                case "--depth":
                    if (int.TryParse(Next(), out var depth)) options.CodexDepth = depth;
                    break;
                case "--codex-filter":
                    options.CodexFilter = Next();
                    break;
                case "--screenshot":
                    options.ScreenshotPath = Next();
                    if (options.ScreenshotPath is { Length: > 0 } shot) Log.Path = shot + ".log";
                    break;
                case "--log":
                    options.LogPath = Next();
                    if (options.LogPath is { Length: > 0 } log) Log.Path = log;
                    break;
                case "--window":
                    if (Next() is { } size)
                    {
                        var parts = size.Split('x', 'X');
                        if (parts.Length == 2 &&
                            double.TryParse(parts[0], out var w) &&
                            double.TryParse(parts[1], out var h))
                        {
                            options.WindowWidth = w;
                            options.WindowHeight = h;
                        }
                    }
                    break;
            }
        }
        return options;
    }
}
