using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
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

        if (startup.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>壳的视图模型（App 截图时要用）。</summary>
    public MainViewModel Shell { get; }

    // ------------------------------------------------------------------ 最大化时对齐显示器工作区
    // 自绘标题栏（WindowStyle=None）的窗口被最大化时，Windows 按「整块屏幕」给尺寸，
    // 窗口因此比工作区高出任务栏那一条；任务栏又在最上层，正好把最下面的状态栏盖掉。
    // 这里自己回答 WM_GETMINMAXINFO，把最大化尺寸限制在窗口所在显示器的工作区里。
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WindowProcedure);
    }

    private static IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;

        // 置为已处理：默认那套按整块屏幕算，正是状态栏被盖住的原因
        ApplyWorkAreaMaximizedSize(hwnd, lParam);
        handled = true;
        return IntPtr.Zero;
    }

    private static void ApplyWorkAreaMaximizedSize(IntPtr hwnd, IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;

        var screen = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref screen)) return;

        info.MaxPosition.X = screen.Work.Left - screen.Monitor.Left;
        info.MaxPosition.Y = screen.Work.Top - screen.Monitor.Top;
        info.MaxSize.X = screen.Work.Right - screen.Work.Left;
        info.MaxSize.Y = screen.Work.Bottom - screen.Work.Top;
        Marshal.StructureToPtr(info, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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