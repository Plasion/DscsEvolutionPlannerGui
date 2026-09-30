using System;
using System.IO;
using System.Windows;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>文件对话框与提示框的具体实现（视图模型只依赖 IDialogService）。</summary>
public sealed class WpfDialogService : IDialogService
{
    public string? PickFolder(string title, string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickDataFile(string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 data.csv",
            Filter = "CSV 数据文件|*.csv|所有文件|*.*",
        };
        if (!string.IsNullOrWhiteSpace(initial) && File.Exists(initial)) dialog.FileName = initial;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveText(string title, string suggestedName, string text)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = "文本文件|*.txt|所有文件|*.*",
            FileName = suggestedName,
        };
        if (dialog.ShowDialog() != true) return null;

        try
        {
            File.WriteAllText(dialog.FileName, text);
            return dialog.FileName;
        }
        catch (Exception ex)
        {
            Warn("导出失败", ex.Message);
            return null;
        }
    }

    public void Warn(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}