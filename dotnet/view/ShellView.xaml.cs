using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DscsEvolutionPlanner.Core;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 底层壳：自绘标题栏 + 页面宿主 + 详情栏 + 状态栏。
/// 它只做三件事：把自己的窗口动作接到视图模型、启动时让视图模型把数据准备好、离屏截图（验收用）。
/// 所有交互都在视图模型里，这里不写业务逻辑，也不从 XAML 里取控件。
/// </summary>
public partial class ShellView : Window
{
    private readonly CliOptions _options;
    private bool _capturing;

    public ShellView(CliOptions options, StartupOptions startup)
    {
        _options = options;

        var (settings, state) = (AppSettings.Current(), UserState.Load());
        Shell = new MainViewModel(settings, startup, state, new WpfDialogService(), new DataSource(startup));
        DataContext = Shell;

        InitializeComponent();
    }

    /// <summary>壳的视图模型（App 截图时要用）。</summary>
    public MainViewModel Shell { get; }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // 窗口动作：视图模型发出请求，这里执行（视图模型不引用 Window）
        Shell.WindowActions = new WindowActions(this);

        if (_capturing) return;
        await Shell.InitializeAsync();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_capturing) return;

        Shell.SaveLayout(Width, Height, Shell.LeftColumnWidth, Shell.DetailsColumnWidth);
        Shell.WindowActions = null;
    }


    /// <summary>
    /// --screenshot：离屏渲染界面后退出（附带起点候选与继承技弹层各一张）。
    /// 数据准备在视图模型里，这里只负责推进布局与保存图片。
    /// </summary>
    public async Task CaptureToPngAsync(string path)
    {
        _capturing = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000;
        Top = -20000;
        ShowInTaskbar = false;

        Shell.WindowActions = new WindowActions(this);
        Show();
        Pump();

        await Shell.PrepareForCaptureAsync();
        Pump();

        if (Content is FrameworkElement root) SaveVisual(root, path);
        Hide();
        Close();
    }

    /// <summary>WPF 版的 DoEvents：让排队中的布局/渲染先干完。</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void SaveVisual(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        if (width <= 0 || height <= 0) return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>窗口动作的最小实现：只调 Window 自己的成员。</summary>
    private sealed class WindowActions : IWindowActions
    {
        private readonly Window _window;

        public WindowActions(Window window) => _window = window;

        public void Minimize() => _window.WindowState = WindowState.Minimized;

        public void ToggleMaximize() =>
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        public void Close() => _window.Close();
    }
}