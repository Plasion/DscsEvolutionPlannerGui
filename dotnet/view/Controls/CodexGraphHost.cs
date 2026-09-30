using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DscsEvolutionPlanner.Core;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 关系网宿主：内部是一个带滚动条的 CodexGraph，负责缩放（按钮 / Ctrl + 滚轮）、拖动平移、
/// 换中心后把新中心滚到视口正中。视图只需要绑定 <see cref="Graph"/>、<see cref="Zoom"/> 与两个命令。
/// </summary>
public sealed class CodexGraphHost : Control
{
    private ScrollViewer _scroll = null!;
    private CodexGraph _panel = null!;

    private bool _panning;
    private Point _panOrigin;
    private double _panX;
    private double _panY;
    private MainViewModel? _hooked;

    static CodexGraphHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CodexGraphHost), new FrameworkPropertyMetadata(typeof(CodexGraphHost)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_scroll is not null)
        {
            _scroll.PreviewMouseWheel -= OnWheel;
            _scroll.PreviewMouseLeftButtonDown -= OnPanStart;
            _scroll.PreviewMouseMove -= OnPanMove;
            _scroll.PreviewMouseLeftButtonUp -= OnPanEnd;
            _scroll.ScrollChanged -= OnScrollChanged;
        }

        _scroll = (ScrollViewer)GetTemplateChild("PART_Scroll")!;
        _panel = (CodexGraph)GetTemplateChild("PART_Graph")!;

        _scroll.PreviewMouseWheel += OnWheel;
        _scroll.PreviewMouseLeftButtonDown += OnPanStart;
        _scroll.PreviewMouseMove += OnPanMove;
        _scroll.PreviewMouseLeftButtonUp += OnPanEnd;
        _scroll.ScrollChanged += OnScrollChanged;

        ApplyGraph();
        ApplyZoom();
    }

    // ------------------------------------------------------------------ 依赖属性
    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph), typeof(CodexGraphVm), typeof(CodexGraphHost),
        new FrameworkPropertyMetadata(null, OnGraphChanged));

    /// <summary>要展示的关系网（视图模型给的快照）。</summary>
    public CodexGraphVm? Graph
    {
        get => (CodexGraphVm?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public static readonly DependencyProperty NodeTemplateProperty = DependencyProperty.Register(
        nameof(NodeTemplate), typeof(DataTemplate), typeof(CodexGraphHost),
        new PropertyMetadata(null, OnGraphChanged));

    public DataTemplate? NodeTemplate
    {
        get => (DataTemplate?)GetValue(NodeTemplateProperty);
        set => SetValue(NodeTemplateProperty, value);
    }

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CodexGraphHost),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomChanged));

    /// <summary>整体缩放（0.4–2.0）。按钮改它，Ctrl + 滚轮也改它。</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (CodexGraphHost)d;
        host.ApplyGraph();
        host.CenterOnRoot();
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodexGraphHost)d).ApplyZoom();

    private static readonly DependencyPropertyKey ViewportOffsetKey = DependencyProperty.RegisterReadOnly(
        nameof(ViewportOffset), typeof(Point), typeof(CodexGraphHost), new PropertyMetadata(new Point(0, 0)));

    /// <summary>当前视口左上角在内容里的位置（只读，供外部读取/绑定）。</summary>
    public static readonly DependencyProperty ViewportOffsetProperty = ViewportOffsetKey.DependencyProperty;

    public Point ViewportOffset => (Point)GetValue(ViewportOffsetProperty);

    // ------------------------------------------------------------------ 内容与缩放
    private void ApplyGraph()
    {
        if (_panel is null) return;
        _panel.Graph = Graph;
        _panel.NodeTemplate = NodeTemplate;
        _panel.UpdateLayout();
    }

    private void ApplyZoom()
    {
        if (_panel is null) return;
        _panel.LayoutTransform = Math.Abs(Zoom - 1.0) < 0.001 ? null : new ScaleTransform(Zoom, Zoom);
    }

    /// <summary>把中心节点挪到视口正中。</summary>
    public void CenterOnRoot()
    {
        if (_scroll is null || _panel is null) return;
        _scroll.UpdateLayout();
        _panel.UpdateLayout();
        if (_panel.RootRect is not { } rect) return;

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        _scroll.ScrollToHorizontalOffset(centerX - _scroll.ViewportWidth / 2);
        _scroll.ScrollToVerticalOffset(centerY - _scroll.ViewportHeight / 2);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // 记住视口位置：切页回来仍是原位（控件本身不会被重建），也让绑定的地方读得到
        SetValue(ViewportOffsetKey, new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset));
    }

    // ------------------------------------------------------------------ 滚轮缩放
    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;   // 不按 Ctrl 就是普通滚动

        var step = AppConfig.Current.Codex.ZoomWheelStep;
        var anchor = e.GetPosition(_scroll);
        var before = new Point(_scroll.HorizontalOffset + anchor.X, _scroll.VerticalOffset + anchor.Y);

        var old = Zoom;
        Zoom = Math.Clamp(e.Delta > 0 ? Zoom * step : Zoom / step,
            AppConfig.Current.Codex.MinZoom, AppConfig.Current.Codex.MaxZoom);
        if (Math.Abs(Zoom - old) < 0.001) return;

        e.Handled = true;
        _scroll.UpdateLayout();

        var ratio = Zoom / old;
        _scroll.ScrollToHorizontalOffset(before.X * ratio - anchor.X);
        _scroll.ScrollToVerticalOffset(before.Y * ratio - anchor.Y);
    }

    // ------------------------------------------------------------------ 拖动平移
    private void OnPanStart(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        if (e.ChangedButton != MouseButton.Left) return;

        _panOrigin = e.GetPosition(_scroll);
        _panX = _scroll.HorizontalOffset;
        _panY = _scroll.VerticalOffset;
    }

    private void OnPanMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var position = e.GetPosition(_scroll);
        var dx = position.X - _panOrigin.X;
        var dy = position.Y - _panOrigin.Y;

        if (!_panning)
        {
            var threshold = AppConfig.Current.Codex.PanThreshold;
            if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold) return;

            _panning = true;
            _scroll.CaptureMouse();   // 抓住鼠标，节点就不会误触发「点击看详情」
            Mouse.OverrideCursor = Cursors.SizeAll;
        }

        _scroll.ScrollToHorizontalOffset(_panX - dx);
        _scroll.ScrollToVerticalOffset(_panY - dy);
    }

    private void OnPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;

        _panning = false;
        _scroll.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
    }

    // ------------------------------------------------------------------ 与视图模型接线
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != DataContextProperty) return;

        if (_hooked is not null) _hooked.RecenterRequested -= CenterOnRoot;
        _hooked = DataContext as MainViewModel;
        if (_hooked is not null) _hooked.RecenterRequested += CenterOnRoot;
    }
}