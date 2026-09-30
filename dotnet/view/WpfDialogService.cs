using System;
using System.IO;
using System.Windows;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>文件对话框与提示框的具体实现（视图模型只依赖 IDialogService）。</summary>
public sealed class WpfDialogService : IDialogService
{
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