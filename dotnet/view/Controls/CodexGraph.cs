using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DscsEvolutionPlanner.Core;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 图鉴（网状图）：节点按「距中心的层数」分列摆放（每列居中），连线由面板自己在节点下面画
/// （进化绿实线、退化橙实线、同级紫虚线；最近算出的路线经过的连线加粗）。
/// </summary>
public sealed class CodexGraph : Panel
{
    // 间距与留白都由 config.json 控制（Core/AppConfig.cs）
    private static double ColumnGap => AppConfig.Current.Codex.ColumnGap;
    private static double RowGap => AppConfig.Current.Codex.RowGap;

    /// <summary>图四周留的空白：让拖动范围比内容本身大一圈，不然刚好卡在边上。</summary>
    private static double PanPadding => AppConfig.Current.Codex.GraphPadding;

    private readonly List<Rect> _rects = new();
    private readonly Dictionary<(Color Color, double Width, bool Dashed), Pen> _pens = new();
    private Size _nodeSize;
    private Size _size;
    private Rect? _rootRect;

    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph), typeof(CodexGraphVm), typeof(CodexGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));

    public CodexGraphVm? Graph
    {
        get => (CodexGraphVm?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public static readonly DependencyProperty NodeTemplateProperty = DependencyProperty.Register(
        nameof(NodeTemplate), typeof(DataTemplate), typeof(CodexGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));

    public DataTemplate? NodeTemplate
    {
        get => (DataTemplate?)GetValue(NodeTemplateProperty);
        set => SetValue(NodeTemplateProperty, value);
    }

    /// <summary>中心节点的矩形（「回到中心」用），还没排完版时是 null。</summary>
    public Rect? RootRect => _rootRect;

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodexGraph)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        _rects.Clear();
        _rootRect = null;

        var graph = Graph;
        if (graph is null || NodeTemplate is null) return;

        foreach (var node in graph.Nodes)
            Children.Add(new ContentControl
            {
                Content = node,
                ContentTemplate = NodeTemplate,
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            });
    }

    // ------------------------------------------------------------------ 布局
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in Children)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        RefreshNodeSize();
        ComputeLayout(assignRoot: false);
        return _size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        RefreshNodeSize();
        ComputeLayout(assignRoot: true);
        for (var i = 0; i < Children.Count && i < _rects.Count; i++)
            Children[i].Arrange(_rects[i]);

        InvalidateVisual();   // 连线要跟着节点走
        return finalSize;
    }

    /// <summary>节点大小取最大那个（模板早就量过了，这里再确认一次）。</summary>
    private void RefreshNodeSize()
    {
        foreach (UIElement child in Children)
        {
            if (child.DesiredSize.Width > _nodeSize.Width) _nodeSize.Width = child.DesiredSize.Width;
            if (child.DesiredSize.Height > _nodeSize.Height) _nodeSize.Height = child.DesiredSize.Height;
        }
    }

    /// <summary>算每只节点的矩形：按 Level 分列（列内纵向居中），结果写回 _rects / _size / _rootRect。</summary>
    private void ComputeLayout(bool assignRoot)
    {
        _rects.Clear();
        _size = new Size(0, 0);
        if (assignRoot) _rootRect = null;

        var graph = Graph;
        if (graph is null || Children.Count == 0) return;

        var columns = new Dictionary<int, List<int>>();   // level → 节点下标
        for (var i = 0; i < Children.Count; i++)
        {
            var level = i < graph.Nodes.Count ? graph.Nodes[i].Level : 0;
            if (!columns.TryGetValue(level, out var list)) columns[level] = list = new List<int>();
            list.Add(i);
        }

        var maxColumnHeight = 0.0;
        foreach (var list in columns.Values)
            maxColumnHeight = Math.Max(maxColumnHeight,
                list.Count * _nodeSize.Height + (list.Count - 1) * RowGap);

        var minLevel = int.MaxValue;
        var maxLevel = int.MinValue;
        foreach (var level in columns.Keys)
        {
            minLevel = Math.Min(minLevel, level);
            maxLevel = Math.Max(maxLevel, level);
        }

        var columnCount = maxLevel - minLevel + 1;
        var totalWidth = columnCount * _nodeSize.Width + (columnCount - 1) * ColumnGap;
        for (var i = 0; i < Children.Count; i++) _rects.Add(default);

        foreach (var (level, list) in columns)
        {
            var columnHeight = list.Count * _nodeSize.Height + (list.Count - 1) * RowGap;
            var x = PanPadding + (level - minLevel) * (_nodeSize.Width + ColumnGap);   // 层数可以是负的，平移到 0
            var y = PanPadding + (maxColumnHeight - columnHeight) / 2;
            foreach (var index in list)
            {
                var rect = new Rect(x, y, _nodeSize.Width, _nodeSize.Height);
                _rects[index] = rect;
                if (assignRoot && index < graph.Nodes.Count && graph.Nodes[index].IsRoot) _rootRect = rect;
                y += _nodeSize.Height + RowGap;
            }
        }

        _size = new Size(totalWidth + PanPadding * 2, maxColumnHeight + PanPadding * 2);
    }

    // ------------------------------------------------------------------ 连线
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var graph = Graph;
        if (graph is null || _rects.Count == 0) return;

        foreach (var link in graph.Links)
        {
            if (link.From < 0 || link.To < 0 || link.From >= _rects.Count || link.To >= _rects.Count) continue;
            var from = _rects[link.From];
            var to = _rects[link.To];

            var start = BorderPoint(from, Center(to));
            var end = BorderPoint(to, Center(from));
            context.DrawLine(Pen(link), start, end);
        }
    }

    private Pen Pen(CodexLinkVm link)
    {
        var color = (Theme.Brush(link.Key) as SolidColorBrush)?.Color ?? Colors.Gray;
        var width = link.OnRoute ? 3.0 : 1.4;
        var key = (color, width, link.Dashed);
        if (_pens.TryGetValue(key, out var cached)) return cached;

        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (!link.OnRoute) brush.Opacity = 0.5;
        if (link.Dashed) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        _pens[key] = pen;
        return pen;
    }

    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    /// <summary>从矩形中心朝 toward 方向走到边框上的点（连线不压进节点里）。</summary>
    private static Point BorderPoint(Rect rect, Point toward)
    {
        var center = Center(rect);
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001) return center;

        var scaleX = Math.Abs(dx) < 0.001 ? double.PositiveInfinity : rect.Width / 2 / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.001 ? double.PositiveInfinity : rect.Height / 2 / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);
        return new Point(center.X + dx * scale, center.Y + dy * scale);
    }
}