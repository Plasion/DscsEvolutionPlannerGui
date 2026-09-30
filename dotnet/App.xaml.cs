using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using DscsEvolutionPlanner.Core;
using DscsEvolutionPlanner.View;

namespace DscsEvolutionPlanner;

/// <summary>
/// 应用入口：无界面模式（--help / --cli）直接跑完就退；有界面时装配 ShellView 并可选离屏截图。
/// 界面资源的定义在 App.xaml（画刷与全部样式），三个视图共用它。
/// </summary>
public partial class App : Application
{
    private System.Windows.Window? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = CliOptions.Parse(e.Args);
        AppConfig.Load();   // 放在参数解析之后：配置的加载结果会写进 --screenshot/--log 的文件

        if (options.ShowHelp)
        {
            MessageBox.Show(CliOptions.HelpText, "数码兽进化路线规划器", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (options.CliArguments.Count > 0)
        {
            RunCli(options);
            Shutdown();
            return;
        }

        var (startup, _) = AppBootstrapper.Prepare(options);
        var shell = new ShellView(options, startup);
        _shell = shell;

        if (!string.IsNullOrEmpty(options.ScreenshotPath))
        {
            // 验收模式：离屏渲染界面后直接退出；渲染出错时把堆栈写进 <png>.log
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = CaptureThenShutdownAsync(shell, options.ScreenshotPath);
            return;
        }

        MainWindow = shell;
        shell.Show();
    }

    private async System.Threading.Tasks.Task CaptureThenShutdownAsync(ShellView shell, string path)
    {
        try
        {
            await shell.CaptureToPngAsync(path);
        }
        catch (Exception ex)
        {
            Log.Write($"截图失败：{ex}");
        }
        Shutdown();
    }

    /// <summary>无界面模式：与 planner.py 相同的参数写法，输出也保持一致的文本。</summary>
    private static void RunCli(CliOptions options)
    {
        var lines = new List<string>();
        Log.Write("CLI 模式：开始");
        try
        {
            var path = CliQueryParser.ResolveDataPath(options.DataPath);
            Log.Write($"CLI 模式：数据文件 = {path ?? "<未找到>"}");
            if (path is null)
            {
                lines.Add("错误：找不到 data.csv。");
            }
            else
            {
                var catalog = DigimonCatalog.Load(path);
                lines.AddRange(catalog.Warnings);
                var query = CliQueryParser.Parse(options.CliArguments);
                var result = new EvolutionPlanner(catalog).Search(query);
                Log.Write($"CLI 模式：查到 {result.Routes.Count} 条路线，Ok={result.Ok}");
                lines.AddRange(result.TextLines);
            }
        }
        catch (CliUsageException ex)
        {
            lines.Add(ex.Message);
        }
        catch (Exception ex)
        {
            lines.Add("错误：" + ex.Message);
        }

        var text = string.Join(Environment.NewLine, lines);
        Log.Write($"CLI 模式：输出 {lines.Count} 行，OutPath={options.OutPath ?? "<标准输出>"}");
        if (!string.IsNullOrEmpty(options.OutPath))
        {
            File.WriteAllText(options.OutPath, text + Environment.NewLine, new UTF8Encoding(false));
        }
        else
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(text);
        }
    }
}