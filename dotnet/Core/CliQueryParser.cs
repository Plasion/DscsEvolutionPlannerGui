using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DscsEvolutionPlanner.Core;

public sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message) { }
}

/// <summary>
/// 把 planner.py 风格的命令行参数解析成 PlannerQuery（等价 parse_command 的参数部分，含全部错误文案）。
/// 无界面模式（--cli）和自动化对拍都用它。
/// </summary>
public static class CliQueryParser
{
    private static readonly HashSet<string> FlagTokens = new() { "-r", "-k", "-d", "-pm", "-ps" };

    public static PlannerQuery Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0) throw new CliUsageException("错误：参数太少了。输入?、-h或--help查看用法。");

        var query = new PlannerQuery { StartText = arguments[0] };
        var rest = arguments.Skip(1).ToList();

        // ---- 终点部分
        if (rest.Count == 1 && rest[0] == "-em")
        {
            throw new CliUsageException("错误：-em需要至少一个参数。输入?、-h或--help查看用法。");
        }

        if (rest.Count > 0 && rest[0] == "-em")
        {
            var cut = rest.FindIndex(1, token => FlagTokens.Contains(token));
            var endTokens = cut < 0 ? rest.Skip(1).ToList() : rest.GetRange(1, cut - 1);
            var tail = cut < 0 ? new List<string>() : rest.Skip(cut).ToList();
            if (endTokens.Count == 0)
                throw new CliUsageException("错误：-em需要至少一个参数。输入?、-h或--help查看用法。");

            query.Mode = EndpointMode.Multi;
            query.MultiEnds.AddRange(endTokens);
            rest = tail;
        }
        else if (rest.Count > 0 && rest[0] == "-a")
        {
            query.Mode = EndpointMode.Any;
            var hasConstraint = rest.Skip(1).Any(token => token is "-pm" or "-ps");
            if (!hasConstraint)
                throw new CliUsageException("错误：使用了-a但未使用-pm或-ps进行约束。输入?、-h或--help查看用法。");
            rest = rest.Skip(1).ToList();
        }
        else if (rest.Count > 0 && !FlagTokens.Contains(rest[0]))
        {
            query.EndText = rest[0];
            rest = rest.Skip(1).ToList();
        }

        // ---- 选项部分：先在 flag 处切段（与 planner.py 的切分方式一致）
        var flags = new List<List<string>>();
        var start = 0;
        for (var i = 0; i < rest.Count; i++)
        {
            if (!FlagTokens.Contains(rest[i])) continue;
            flags.Add(rest.GetRange(start, i - start));
            start = i;
        }
        if (rest.Count > 0) flags.Add(rest.Skip(start).ToList());

        foreach (var flag in flags)
        {
            if (flag.Count == 0) continue;
            switch (flag[0])
            {
                case "-r":
                    query.Reverse = true;
                    break;

                case "-k" when flag.Count < 2:
                    throw new CliUsageException("错误：-k需要一个参数。输入?、-h或--help查看用法。");
                case "-k":
                    if (!int.TryParse(flag[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var k))
                        throw new CliUsageException($"错误：提供给-k的参数「{flag[1]}」不是数字。");
                    if (k < 1) throw new CliUsageException($"错误：提供给-k的参数「{k}」太小了，应该至少为1。");
                    query.K = k;
                    break;

                case "-d" when flag.Count < 2:
                    throw new CliUsageException("错误：-d需要至少一个参数。输入?、-h或--help查看用法。");
                case "-d":
                    query.ExcludeGenerations.AddRange(flag.Skip(1));
                    break;

                case "-pm" when flag.Count < 2:
                    throw new CliUsageException("错误：-pm需要至少一个参数。输入?、-h或--help查看用法。");
                case "-pm":
                    query.ViaGroups.Add(flag.Skip(1).ToList());
                    break;

                case "-ps" when flag.Count < 2:
                    throw new CliUsageException("错误：-ps需要至少一个参数。输入?、-h或--help查看用法。");
                case "-ps":
                    query.Skills.AddRange(flag.Skip(1));
                    break;
            }
        }

        return query;
    }

    /// <summary>数据文件查找顺序：显式指定 → exe 同级 → 向上找 data.csv / py/data.csv。</summary>
    public static string? ResolveDataPath(string? explicitPath, string? remembered = null)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrEmpty(explicitPath)) candidates.Add(explicitPath);
        if (!string.IsNullOrEmpty(remembered) && remembered != explicitPath) candidates.Add(remembered);
        candidates.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "data.csv"));

        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && directory is not null; i++, directory = directory.Parent)
        {
            candidates.Add(System.IO.Path.Combine(directory.FullName, "data.csv"));
            candidates.Add(System.IO.Path.Combine(directory.FullName, "py", "data.csv"));
        }

        return candidates.FirstOrDefault(path => !string.IsNullOrEmpty(path) && System.IO.File.Exists(path));
    }
}
