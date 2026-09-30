using System;

namespace DscsEvolutionPlanner.Core;

/// <summary>轻量诊断日志：默认关闭（Path 为空时啥也不做），--screenshot 时会写到 &lt;png&gt;.log。</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string? Path { get; set; }

    public static void Write(string message)
    {
        var path = Path;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            lock (Gate)
                System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}", System.Text.Encoding.UTF8);
        }
        catch (Exception) { /* 日志失败不影响运行 */ }
    }
}
