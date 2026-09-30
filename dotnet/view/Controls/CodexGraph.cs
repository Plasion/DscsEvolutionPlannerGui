using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DscsEvolutionPlanner.Core;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 图鉴（网状图）：节点按「距中心的层数」分列，连线由面板自己在节点下面画
/// （进化绿、退化橙、同级紫虚线；最近算出的路线经过的连线加粗）。
///
/// 节点是**横向条**（头像在左、名字与「#编号 · 世代」在右），比竖版卡片矮很多，一层占的高度大约是竖版的三分之一。
/// 第 2 级及以后、并且那一层数量超过 6 只的，平时收成一条窄条（小头像 + 编号 + 名字）并淡出。
///
/// 悬停 / 点选**不放大**：被指的节点、与它直接相连的那些节点（含窄条）一起点亮，连线变实线。
///
/// 布局围绕「关联的卡片尽量靠得近、线尽量不压到卡片上」：
/// 列内按邻居重心反复重排（少交叉）→ 每个节点往邻居的平均高度靠
/// （PAVA 保序最小二乘拟合，支持逐节点高度）→ 每条连线在卡片边缘有自己的落点。
/// 连线一律三次贝塞尔、水平出入（节点编辑器那种 S 形）；同列的形态关系绕到列外侧。
/// </summary>
public sealed class CodexGraph : Panel
{
    // 间距与留白都由 config.json 控制（Core/AppConfig.cs）
    private static double ColumnGap => AppConfig.Current.Codex.ColumnGap;
    private static double RowGap => AppConfig.Current.Codex.RowGap;
    private static double PanPadding => AppConfig.Current.Codex.GraphPadding;

    // 卡片尺寸：IllustratedGuideView.xaml 里两个节点模板写死的就是这一组数，布局直接用，
    // 不再依赖测量结果（免得换个模板就把列对不齐）。改模板记得同步改这里。
    private const double NodeWidth = 210;
    private const double FullNodeHeight = 66;
    private const double CompactNodeHeight = 34;

    /// <summary>列内排序与高度松弛各来回扫几轮（再多收益很小）。</summary>
    private const int OrderPasses = 4;
    private const int RelaxPasses = 6;

    /// <summary>连线落点在卡片上下边缘留出的空白。</summary>
    private const double PortInset = 10;

    /// <summary>控制点最少外推多少像素。</summary>
    private const double MinCurveOffset = 24;

    /// <summary>第 2 级及更靠外节点的淡出程度（收成窄条的也一样淡）。</summary>
    private const double DimOpacity = 0.38;

    private readonly Dictionary<(Color Color, double Width, bool Dashed, bool Solid), Pen> _pens = new();
    private Rect[] _rects = Array.Empty<Rect>();
    private Size _size;
    private double _middleX;   // 内容水平中心：同列连线往远离它的一侧鼓
    private int _rootIndex = -1;

    /// <summary>鼠标正停在哪个节点上（-1＝没有）。</summary>
    private int _hoverIndex = -1;

    /// <summary>点选钉住的节点（-1＝没有）：它会一直高亮，直到点空白处或点别的节点。</summary>
    private int _pinnedIndex = -1;

    /// <summary>真正的节点数（Children 就是全部节点）。</summary>
    private int NodeCount => Children.Count;

    /// <summary>悬停 / 点选时一起点亮的邻居节点（连线也一起变实线）。</summary>
    private readonly HashSet<int> _hoverLit = new();
    private readonly HashSet<int> _pinnedLit = new();

    // 按下 → 抬起之间挪动超过这个距离就算「拖动平移」，不当作点了一下
    private static double ClickSlack => AppConfig.Current.Codex.PanThreshold;
    private bool _pressed;
    private Point _pressPoint;
    private Pen? _pinRing;

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

    /// <summary>收成窄条的那些节点用的模板（CodexNodeVm.IsCompactBase 为真时）。</summary>
    public static readonly DependencyProperty CompactTemplateProperty = DependencyProperty.Register(
        nameof(CompactTemplate), typeof(DataTemplate), typeof(CodexGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));

    public DataTemplate? CompactTemplate
    {
        get => (DataTemplate?)GetValue(CompactTemplateProperty);
        set => SetValue(CompactTemplateProperty, value);
    }

    /// <summary>中心节点的矩形（「回到中心」用），还没排完版时是 null。</summary>
    public Rect? RootRect { get; private set; }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CodexGraph)d).Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        _rects = Array.Empty<Rect>();
        RootRect = null;
        _rootIndex = -1;
        _hoverIndex = -1;
        _pinnedIndex = -1;
        _hoverLit.Clear();
        _pinnedLit.Clear();

        var graph = Graph;
        if (graph is null || NodeTemplate is null) return;

        var compactTemplate = CompactTemplate ?? NodeTemplate;
        foreach (var node in graph.Nodes)
            Children.Add(MakeNode(node, node.IsCompactBase ? compactTemplate : NodeTemplate));
    }

    private static ContentControl MakeNode(CodexNodeVm node, DataTemplate template) => new()
    {
        Content = node,
        ContentTemplate = template,
        Focusable = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    // ------------------------------------------------------------------ 布局
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in Children)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        ComputeLayout();
        return _size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        ComputeLayout();
        var count = NodeCount;
        for (var i = 0; i < count && i < _rects.Length; i++)
            Children[i].Arrange(_rects[i]);

        RootRect = _rootIndex >= 0 && _rootIndex < _rects.Length ? _rects[_rootIndex] : null;

        BuildCurves();
        RefreshEmphasis();
        InvalidateVisual();   // 连线要跟着节点走
        return finalSize;
    }

    private void ComputeLayout()
    {
        _rects = Array.Empty<Rect>();
        _size = new Size(0, 0);

        var graph = Graph;
        if (graph is null || NodeCount == 0) return;

        if (_rootIndex < 0) _rootIndex = RootIndex(graph);
        _rects = LayOut(graph, out _size);
    }

    private static double HeightOf(CodexGraphVm graph, int index) =>
        index < graph.Nodes.Count && graph.Nodes[index].IsCompactBase ? CompactNodeHeight : FullNodeHeight;

    /// <summary>
    /// 真正算坐标的地方：分列 → 列内按邻居重心重排 → 每个节点往邻居的平均高度靠（PAVA，逐节点高度）
    /// → 水平分列 + 垂直居中。只算「平时的样子」，浮层不参与。
    /// </summary>
    private Rect[] LayOut(CodexGraphVm graph, out Size size)
    {
        var count = NodeCount;
        var rects = new Rect[count];

        var height = new double[count];
        for (var i = 0; i < count; i++) height[i] = HeightOf(graph, i);

        var levelOf = new int[count];
        var levels = new List<int>();
        var columns = new Dictionary<int, List<int>>();
        for (var i = 0; i < count; i++)
        {
            var level = i < graph.Nodes.Count ? graph.Nodes[i].Level : 0;
            levelOf[i] = level;
            if (!columns.TryGetValue(level, out var list))
            {
                columns[level] = list = new List<int>();
                levels.Add(level);
            }
            list.Add(i);
        }
        levels.Sort();

        // 只把「相邻列之间」的连线算进邻接：正是画线的那一套；同列的形态关系不参与排布
        var neighbours = new List<int>[count];
        for (var i = 0; i < count; i++) neighbours[i] = new List<int>();
        foreach (var link in graph.Links)
        {
            if (link.From < 0 || link.To < 0 || link.From >= count || link.To >= count) continue;
            if (link.From == link.To || levelOf[link.From] == levelOf[link.To]) continue;
            neighbours[link.From].Add(link.To);
            neighbours[link.To].Add(link.From);
        }

        // ---- 列内排序：按「已处理那一侧的邻居平均行号」重排 ----
        var row = new int[count];
        foreach (var level in levels)
        {
            var list = columns[level];
            for (var r = 0; r < list.Count; r++) row[list[r]] = r;
        }

        var key = new double[count];
        for (var pass = 0; pass < OrderPasses; pass++)
        {
            var forward = pass % 2 == 0;
            for (var step = 0; step < levels.Count; step++)
            {
                var level = levels[forward ? step : levels.Count - 1 - step];
                var list = columns[level];
                if (list.Count < 2) continue;

                foreach (var index in list)
                {
                    var total = 0.0;
                    var used = 0;
                    foreach (var other in neighbours[index])
                    {
                        var before = forward ? levelOf[other] < level : levelOf[other] > level;
                        if (!before) continue;
                        total += row[other];
                        used++;
                    }
                    key[index] = used > 0 ? total / used : row[index];
                }

                list.Sort((a, b) =>
                {
                    var diff = key[a].CompareTo(key[b]);
                    return diff != 0 ? diff : row[a].CompareTo(row[b]);   // 打平就保持原顺序，结果稳定
                });
                for (var r = 0; r < list.Count; r++) row[list[r]] = r;
            }
        }

        // ---- 纵向：先按行号铺开，再让每个节点往邻居的平均高度靠 ----
        var top = new double[count];
        foreach (var level in levels)
        {
            var list = columns[level];
            var y = 0.0;
            foreach (var index in list)
            {
                top[index] = y;
                y += height[index] + RowGap;
            }
        }

        var wanted = new double[count];      // 列内按位置下标，给 FitColumn 用
        var heights = new double[count];     // 同上
        for (var pass = 0; pass < RelaxPasses; pass++)
        {
            var forward = pass % 2 == 0;
            for (var step = 0; step < levels.Count; step++)
            {
                var level = levels[forward ? step : levels.Count - 1 - step];
                var list = columns[level];
                if (list.Count == 0) continue;

                for (var r = 0; r < list.Count; r++)
                {
                    var index = list[r];
                    heights[r] = height[index];

                    var total = 0.0;
                    var used = 0;
                    foreach (var other in neighbours[index])
                    {
                        total += top[other];
                        used++;
                    }
                    wanted[r] = used > 0 ? total / used : top[index];
                }

                FitColumn(wanted, heights, list.Count, list, top);
            }
        }

        // ---- 分列 + 垂直居中 ----
        var columnCount = levels[levels.Count - 1] - levels[0] + 1;
        var totalWidth = columnCount * NodeWidth + (columnCount - 1) * ColumnGap;
        _middleX = PanPadding + totalWidth / 2;

        var minTop = double.MaxValue;
        var maxBottom = double.MinValue;
        foreach (var level in levels)
        {
            foreach (var index in columns[level])
            {
                minTop = Math.Min(minTop, top[index]);
                maxBottom = Math.Max(maxBottom, top[index] + height[index]);
            }
        }

        var contentHeight = Math.Max(maxBottom - minTop, 0);
        var offsetY = PanPadding - minTop + (contentHeight - (maxBottom - minTop)) / 2;

        foreach (var level in levels)
        {
            var x = PanPadding + (level - levels[0]) * (NodeWidth + ColumnGap);   // 层数可以是负的，平移到 0
            foreach (var index in columns[level])
                rects[index] = new Rect(x, top[index] + offsetY, NodeWidth, height[index]);
        }

        size = new Size(totalWidth + PanPadding * 2, contentHeight + PanPadding * 2);
        return rects;
    }

    /// <summary>
    /// 在「保持列内顺序、相邻至少隔 RowGap」的前提下，找最贴近 wanted 的一组高度。
    /// 把问题化成 z[i] = wanted[i] - offset[i] 的保序最小二乘拟合（PAVA）：
    /// 比「从上往下硬压」稳，整列不会被单侧约束带上偏。offset 用逐节点高度累加，所以
    /// 完整卡片和窄条可以混在一列里。
    /// </summary>
    private void FitColumn(double[] wanted, double[] heights, int count, List<int> order, double[] top)
    {
        var offset = new double[count];
        for (var i = 1; i < count; i++) offset[i] = offset[i - 1] + heights[i - 1] + RowGap;

        var value = new double[count];
        var items = new int[count];
        var blocks = 0;

        for (var i = 0; i < count; i++)
        {
            value[blocks] = wanted[i] - offset[i];
            items[blocks] = 1;
            blocks++;

            // 违反了保序就并成一块，取平均
            while (blocks > 1 && value[blocks - 2] > value[blocks - 1])
            {
                value[blocks - 2] = (value[blocks - 2] + value[blocks - 1]) / 2;
                items[blocks - 2] += items[blocks - 1];
                blocks--;
            }
        }

        var cursor = 0;
        for (var b = 0; b < blocks; b++)
        {
            for (var k = 0; k < items[b]; k++)
            {
                top[order[cursor]] = value[b] + offset[cursor];
                cursor++;
            }
        }
    }

    // ------------------------------------------------------------------ 悬停 / 点选 / 浮层
    // 用隧道的 Preview 事件：节点里的按钮无论怎么处理冒泡，都不会把悬停判丢掉。
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        SetHover(HitTestIndex(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _pressed = true;
        _pressPoint = e.GetPosition(this);
    }

    /// <summary>
    /// 抬起时才判定「点了一下」：点在卡片上就把它钉住并一直高亮，点在空白处就取消。
    /// 拖动平移（按下后挪动超过阈值）不算点击，不会把拖过的卡片钉住。
    /// 这里不改 Handled —— 卡片自己的单击 / 双击命令还要继续走。
    /// </summary>
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!_pressed) return;
        _pressed = false;

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _pressPoint.X) > ClickSlack || Math.Abs(position.Y - _pressPoint.Y) > ClickSlack)
            return;

        SetPin(HitTestIndex(position));
    }

    private void SetHover(int index)
    {
        if (index == _hoverIndex) return;
        _hoverIndex = index;
        LightNeighbours(index, _hoverLit);

        RefreshEmphasis();
        InvalidateVisual();
    }

    /// <summary>点选：钉住的节点一直亮着，直到点空白处或点别的节点。</summary>
    private void SetPin(int index)
    {
        if (index == _pinnedIndex) return;
        _pinnedIndex = index;
        LightNeighbours(index, _pinnedLit);

        RefreshEmphasis();
        InvalidateVisual();
    }

    /// <summary>把某个节点的直接邻居收进集合（用它自己那张表，和另一个互不影响）。</summary>
    private void LightNeighbours(int index, HashSet<int> into)
    {
        into.Clear();
        if (index < 0 || Graph is not { } graph) return;

        foreach (var link in graph.Links)
        {
            if (link.From == index) into.Add(link.To);
            else if (link.To == index) into.Add(link.From);
        }
    }

    /// <summary>命中判定：节点矩形本身（没有浮层，放大已经不做了）。</summary>
    private int HitTestIndex(Point position)
    {
        for (var i = 0; i < _rects.Length; i++)
            if (_rects[i].Contains(position)) return i;
        return -1;
    }

    /// <summary>
    /// 中心与第一层是亮的，第 2 级往后淡出（收成窄条的也一样淡，它们只是远处的背景）；
    /// 悬停节点、点选钉住的节点，以及它们各自的邻居都点亮。
    /// </summary>
    private void RefreshEmphasis()
    {
        var graph = Graph;
        if (graph is null) return;

        var count = NodeCount;
        for (var i = 0; i < count && i < graph.Nodes.Count; i++)
        {
            var bright = Math.Abs(graph.Nodes[i].Level) <= 1
                         || i == _hoverIndex || _hoverLit.Contains(i)
                         || i == _pinnedIndex || _pinnedLit.Contains(i);
            Children[i].Opacity = bright ? 1.0 : DimOpacity;
        }
    }

    // ------------------------------------------------------------------ 连线几何（排版时算好，悬停只换画笔）
    private Geometry?[] _curves = Array.Empty<Geometry?>();

    private void BuildCurves()
    {
        var graph = Graph;
        var links = graph?.Links;
        if (graph is null || links is null || links.Count == 0 || _rects.Length == 0)
        {
            _curves = Array.Empty<Geometry?>();
            return;
        }

        var curves = new Geometry?[links.Count];
        var ends = ComputeEnds(graph);
        for (var i = 0; i < links.Count; i++)
        {
            if (ends[i] is not { } link) continue;
            curves[i] = BuildCurve(link);
        }

        _curves = curves;
    }

    /// <summary>一条连线两端的落点；Bow 不为 0 表示同列连线，要往侧面鼓出去绕开卡片。</summary>
    private readonly record struct LinkEnds(Point Start, Point End, double Bow);

    private LinkEnds?[] ComputeEnds(CodexGraphVm graph)
    {
        var links = graph.Links;
        var ends = new LinkEnds?[links.Count];
        var groups = new Dictionary<(int Node, int Side), List<int>>();
        var sideOf = new (int From, int To)[links.Count];

        void Remember(int node, int side, int link)
        {
            if (!groups.TryGetValue((node, side), out var list)) groups[(node, side)] = list = new List<int>();
            list.Add(link);
        }

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (link.From < 0 || link.To < 0 || link.From >= _rects.Length || link.To >= _rects.Length) continue;
            if (link.From == link.To) continue;

            var a = Center(_rects[link.From]);
            var b = Center(_rects[link.To]);
            int fromSide, toSide;
            if (Math.Abs(b.X - a.X) < 1)
            {
                // 同一列（形态关系）：绕到远离图中心的那一侧——既不压中间卡片，也不会挤到另一列
                var outward = a.X >= _middleX ? 1 : -1;
                fromSide = outward;
                toSide = outward;
            }
            else
            {
                fromSide = b.X > a.X ? 1 : -1;
                toSide = -fromSide;
            }

            sideOf[i] = (fromSide, toSide);
            Remember(link.From, fromSide, i);
            Remember(link.To, toSide, i);
        }

        // 同一个节点同一侧的连线按对端高度排开，均分到这条边上（不会全挤在一个点）
        var placed = new Dictionary<(int Node, int Side), Point>();
        foreach (var ((node, side), list) in groups)
        {
            var rect = _rects[node];
            var x = side > 0 ? rect.Right : rect.Left;
            var from = rect.Y + PortInset;
            var to = rect.Bottom - PortInset;

            list.Sort((p, q) => CentreY(links[p], node).CompareTo(CentreY(links[q], node)));
            for (var r = 0; r < list.Count; r++)
            {
                var y = list.Count == 1
                    ? rect.Y + rect.Height / 2
                    : from + (to - from) * r / (list.Count - 1);
                placed[(node, side)] = new Point(x, y);
            }
        }

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (link.From < 0 || link.To < 0 || link.From >= _rects.Length || link.To >= _rects.Length) continue;
            if (link.From == link.To) continue;

            var (fromSide, toSide) = sideOf[i];
            var start = placed[(link.From, fromSide)];
            var end = placed[(link.To, toSide)];

            var bow = 0.0;
            if (Math.Abs(Center(_rects[link.To]).X - Center(_rects[link.From]).X) < 1)
            {
                // 三次贝塞尔的最大侧向偏移是 0.75×控制点外推量；只是往外让开一点点，
                // 别越过列间空隙挤到下一列去
                var reach = Math.Min(36, Math.Max(18, ColumnGap - 10));
                bow = reach / 0.75 * (fromSide > 0 ? 1 : -1);
            }

            ends[i] = new LinkEnds(start, end, bow);
        }

        return ends;
    }

    private double CentreY(CodexLinkVm link, int node) =>
        Center(_rects[link.From == node ? link.To : link.From]).Y;

    /// <summary>
    /// 三次贝塞尔：横向连接一律水平出入（节点编辑器那种 S 形），
    /// 所以线的横向范围严格落在两列之间，不会扫过别的卡片。
    /// </summary>
    private static Geometry BuildCurve(LinkEnds ends)
    {
        var start = ends.Start;
        var end = ends.End;
        Point first, second;

        if (Math.Abs(ends.Bow) > 0.01)
        {
            first = new Point(start.X + ends.Bow, start.Y);
            second = new Point(end.X + ends.Bow, end.Y);
        }
        else
        {
            var dx = end.X - start.X;
            var offset = Math.Max(Math.Abs(dx) * 0.5, MinCurveOffset) * (dx >= 0 ? 1 : -1);
            first = new Point(start.X + offset, start.Y);
            second = new Point(end.X - offset, end.Y);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.BezierTo(first, second, end, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ------------------------------------------------------------------ 画线
    // 只有「从中心出发」的连线画实线；鼠标停着 / 点选钉住的节点，与它相连的连线都是实线
    // （节点本身的明暗见 RefreshEmphasis）。钉住的节点外面套一圈，一眼能看出选中了谁。
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        var graph = Graph;
        if (graph is not null && _curves.Length > 0)
        {
            for (var i = 0; i < graph.Links.Count && i < _curves.Length; i++)
            {
                if (_curves[i] is not { } curve) continue;

                var link = graph.Links[i];
                var solid = link.From == _rootIndex || link.To == _rootIndex
                            || _hoverIndex >= 0 && (link.From == _hoverIndex || link.To == _hoverIndex)
                            || _pinnedIndex >= 0 && (link.From == _pinnedIndex || link.To == _pinnedIndex);
                context.DrawGeometry(null, Pen(link, solid), curve);
            }
        }

        if (_pinnedIndex < 0 || _pinnedIndex >= _rects.Length) return;

        // 钉住的那个套一圈强调色描边
        var ring = _rects[_pinnedIndex];
        ring.Inflate(4, 4);
        context.DrawRoundedRectangle(null, PinRing(), ring, 12, 12);
    }

    /// <summary>点选节点外面那圈强调色描边（缓存，只建一次）。</summary>
    private Pen PinRing()
    {
        if (_pinRing is not null) return _pinRing;

        var brush = Theme.Brush(ResourceKeys.AccentColor) as SolidColorBrush ?? new SolidColorBrush(Colors.SteelBlue);
        _pinRing = new Pen(brush, 2);
        _pinRing.Freeze();
        return _pinRing;
    }

    private static int RootIndex(CodexGraphVm graph)
    {
        for (var i = 0; i < graph.Nodes.Count; i++)
            if (graph.Nodes[i].IsRoot) return i;
        return -1;
    }

    private Pen Pen(CodexLinkVm link, bool solid)
    {
        var color = (Theme.Brush(link.Key) as SolidColorBrush)?.Color ?? Colors.Gray;
        var width = link.OnRoute ? 3.0 : solid ? 1.5 : 1.3;
        var key = (color, width, link.Dashed, solid);
        if (_pens.TryGetValue(key, out var cached)) return cached;

        var brush = new SolidColorBrush(color) { Opacity = solid ? (link.OnRoute ? 1.0 : 0.75) : 0.22 };
        var pen = new Pen(brush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        if (link.Dashed) pen.DashStyle = DashStyles.Dash;
        pen.Freeze();
        _pens[key] = pen;
        return pen;
    }

    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
}
