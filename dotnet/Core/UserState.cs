using System;
using System.IO;
using System.Text.Json;

namespace DscsEvolutionPlanner.Core;

/// <summary>界面用的小配置（上次的数据文件、图片目录、窗口与栏宽），存在 exe 同目录 app.settings.json。</summary>
public sealed class UserState
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string? DataPath { get; set; }
    public string? AssetsDirectory { get; set; }
    public int? LastK { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? LeftWidth { get; set; }
    public double? MiddleWidth { get; set; }
    public double? DetailsWidth { get; set; }

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "app.settings.json");

    public static UserState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserState>(File.ReadAllText(FilePath)) ?? new UserState();
        }
        catch (Exception) { /* 配置损坏就回落到默认值 */ }
        return new UserState();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, WriteOptions));
        }
        catch (Exception) { /* 写不进去也不该影响使用 */ }
    }
}