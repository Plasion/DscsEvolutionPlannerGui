using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DscsEvolutionPlanner.Core;

namespace DscsEvolutionPlanner.ViewModel;

/// <summary>
/// 壳的视图模型：界面上的全部状态 + 全部交互（按钮、卡片点击、缩放、窗口动作…）。
/// 它只通过 PlannerFacade 与核心层交互，视图只通过绑定与它交互。
/// </summary>
public sealed partial class MainViewModel : NotifyBase
{
    public const string RouteModeName = "Route";
    public const string GuideModeName = "Guide";

    private readonly AppSettings _settings;
    private readonly StartupOptions _startup;
    private readonly UserState _state;
    private readonly IDialogService _dialogs;
    private readonly DataSource _dataSource;

    private PlannerFacade? _facade;
    private bool _loading;
    private int? _lastDetailId;

    public MainViewModel(
        AppSettings settings,
        StartupOptions startup,
        UserState state,
        IDialogService dialogs,
        DataSource dataSource)
    {
        _settings = settings;
        _startup = startup;
        _state = state;
        _dialogs = dialogs;
        _dataSource = dataSource;

        // 窗口与栏宽：命令行 → 上次保存 → config.json
        WindowMinWidth = settings.MinWindowWidth;
        WindowMinHeight = settings.MinWindowHeight;
        WindowWidth = Math.Max(startup.WindowWidth ?? settings.WindowWidth, settings.MinWindowWidth);
        WindowHeight = Math.Max(startup.WindowHeight ?? settings.WindowHeight, settings.MinWindowHeight);
        LeftColumnWidth = Math.Max(startup.LeftColumnWidth ?? settings.LeftColumnWidth, settings.MinLeftColumnWidth);
        ColumnMinWidth = settings.MinLeftColumnWidth;
        DetailsColumnWidth = Math.Max(startup.DetailsColumnWidth ?? settings.DetailsColumnWidth, settings.MinDetailsColumnWidth);

        AssetsDirectory = startup.ImageDirectory ?? ImageStore.DefaultDirectory();
        K = settings.ClampK(startup.K ?? settings.DefaultK);
        ModeSingle = true;

        for (var depth = 1; depth <= settings.MaxDepth; depth++) DepthChoices.Add(depth);
        CodexDepth = settings.ClampDepth(settings.DefaultDepth);
        if (startup.ZoomPercent is { } zoomPercent) Zoom = zoomPercent / 100.0;

        MinimizeCommand = new Command(() => WindowActions?.Minimize());
        MaximizeCommand = new Command(() => WindowActions?.ToggleMaximize());
        CloseCommand = new Command(() => WindowActions?.Close());

        RunCommand = new Command(() => _ = RunAsync(), () => !_loading);
        ResetCommand = new Command(Reset);
        ToggleTextViewCommand = new Command(ToggleTextView);
        ExportCommand = new Command(Export);

        SelectStepCommand = new Command<int>(ShowDetail);
        ShowRoutesCommand = new Command(() => IsGuideMode = false);
        ShowGuideCommand = new Command(() => _ = ShowGuideAsync());
        ZoomInCommand = new Command(() => ZoomBy(_settings.ZoomButtonStep));
        ZoomOutCommand = new Command(() => ZoomBy(1 / _settings.ZoomButtonStep));
        ZoomResetCommand = new Command(() => Zoom = 1.0);
        RecenterCommand = new Command(() => RecenterRequested?.Invoke());
        ActivateEntryCommand = new Command<int>(id => _ = SetCenterAsync(id));
        ActivateNodeCommand = new Command<int>(ShowDetail);
        ActivateNodeDoubleCommand = new Command<int>(id => _ = SetCenterAsync(id));
        SelectAllVisibleSkillsCommand = new Command(SelectAllVisibleSkills);
        ClearSkillsCommand = new Command(ClearSkills);
    }

    /// <summary>窗口动作由 ShellView 接上（视图模型不引用 Window）。</summary>
    public IWindowActions? WindowActions { get; set; }

    /// <summary>图鉴工具条上的「回到中心」：由 CodexGraphHost 完成实际滚动。</summary>
    public event Action? RecenterRequested;

    // ============================ 窗口 / 栏宽 ============================
    public double WindowMinWidth { get; }
    public double WindowMinHeight { get; }
    public double WindowWidth { get; }
    public double WindowHeight { get; }
    public double LeftColumnWidth { get; }
    public double ColumnMinWidth { get; }
    public double DetailsColumnWidth { get; }

    /// <summary>关窗前把窗口尺寸与栏宽写回用户状态。</summary>
    public void SaveLayout(double windowWidth, double windowHeight, double leftWidth, double detailsWidth)
    {
        _state.WindowWidth = windowWidth;
        _state.WindowHeight = windowHeight;
        _state.LeftWidth = leftWidth;
        _state.DetailsWidth = detailsWidth;
        SaveLayout();
    }

    private void SaveLayout()
    {
        _state.AssetsDirectory = AssetsDirectory;
        _state.LastK = K;
        _state.Save();
    }

    private void RefreshCommands() => RunCommand.RaiseCanExecuteChanged();

    /// <summary>让出一次 UI 优先级，好让遮罩 / 状态栏先画出来。</summary>
    private static Task YieldUi() =>
        Application.Current?.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task
        ?? Task.CompletedTask;

    // ============================ 命令 ============================
    public Command RunCommand { get; }
    public Command ResetCommand { get; }
    public Command ToggleTextViewCommand { get; }
    public Command ExportCommand { get; }
    public Command MinimizeCommand { get; }
    public Command MaximizeCommand { get; }
    public Command CloseCommand { get; }
    public Command ShowRoutesCommand { get; }
    public Command ShowGuideCommand { get; }
    public Command ZoomInCommand { get; }
    public Command ZoomOutCommand { get; }
    public Command ZoomResetCommand { get; }
    public Command RecenterCommand { get; }
    public Command SelectAllVisibleSkillsCommand { get; }
    public Command ClearSkillsCommand { get; }
    public Command<int> SelectStepCommand { get; }
    public Command<int> ActivateEntryCommand { get; }
    public Command<int> ActivateNodeCommand { get; }
    public Command<int> ActivateNodeDoubleCommand { get; }
}