using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DscsEvolutionPlanner.Core;
using DscsEvolutionPlanner.ViewModel;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 进化链的「蛇形」排布：一行放不下就换行，但下一行反向——上一行最后一只与下一行第一只
/// 上下对齐，换行处画竖箭头；反向行内部的箭头做水平镜像（↗ → ↖）。
/// </summary>
public sealed class RouteChain : Panel
{
    /// <summary>箭头槽宽（靠它撑开卡片间距）。</summary>
    private const double ArrowWidth = 46;

    /// <summary>箭头槽高：换行处两行之间的空隙就是它。</summary>
    private const double ArrowHeight = 42;

    /// <summary>换行处箭头上下各留一点空隙。</summary>
    private const double RowGap = 8;

    private static readonly ScaleTransform Mirror = CreateMirror();

    /// <summary>箭头在一块行里的位置：行内正向 / 行内反向（要镜像）/ 换行处（竖着）。</summary>
    private enum ArrowPlacement { Inline, Mirrored, Wrap }

    private readonly List<RouteStepVm> _steps = new();
    private readonly List<Placed> _layout = new();

    private readonly struct Placed
    {
        public Placed(UIElement element, Rect rect, ArrowPlacement placement, int cardIndex)
        {
            Element = element;
            Rect = rect;
            Placement = placement;
            CardIndex = cardIndex;
        }

        public UIElement Element { get; }
        public Rect Rect { get; }
        public ArrowPlacement Placement { get; }
        public int CardIndex { get; }
    }

    // ------------------------------------------------------------------ 依赖属性
    public static readonly DependencyProperty StepsProperty = DependencyProperty.Register(
        nameof(Steps), typeof(IEnumerable<RouteStepVm>), typeof(RouteChain),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));

    public IEnumerable<RouteStepVm>? Steps
    {
        get => (IEnumerable<RouteStepVm>?)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public static readonly DependencyProperty CardTemplateProperty = DependencyProperty.Register(
        nameof(CardTemplate), typeof(DataTemplate), typeof(RouteChain),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputChanged));

    public DataTemplate? CardTemplate
    {
        get => (DataTemplate?)GetValue(CardTemplateProperty);
        set => SetValue(CardTemplateProperty, value);
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RouteChain)d).Rebuild();

    // ------------------------------------------------------------------ 生成子元素
    /// <summary>按「卡片、箭头、卡片、箭头…」的顺序铺好子元素，位置留给 Measure/Arrange 算。</summary>
    private void Rebuild()
    {
        Children.Clear();
        _steps.Clear();
        _layout.Clear();

        if (Steps is not null) _steps.AddRange(Steps);
        if (CardTemplate is null || _steps.Count == 0) return;

        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];

            // 第 0 只前面没有箭头，所以每个箭头排在它后面那张卡前面
            if (i > 0) Children.Add(MakeArrow(i));
            Children.Add(new ContentControl
            {
                Content = step,
                ContentTemplate = CardTemplate,
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            });
        }
    }

    /// <summary>第 i 只前面的那条箭头（方向由排布决定：行内正向 / 行内反向镜像 / 换行处竖着）。</summary>
    private TextBlock MakeArrow(int i)
    {
        var step = _steps[i];
        return new TextBlock
        {
            Text = step.ArrowGlyph,
            Foreground = Theme.Brush(step.ArrowKey),
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Width = ArrowWidth,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            SnapsToDevicePixels = true,
            ToolTip = $"{step.ArrowLabel}：{_steps[i - 1].Name} → {step.Name}",
        };
    }

    // ------------------------------------------------------------------ 量尺寸
    protected override Size MeasureOverride(Size availableSize)
    {
        _layout.Clear();
        if (Children.Count == 0) return new Size(0, 0);

        foreach (UIElement child in Children)
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var cardCount = (Children.Count + 1) / 2;
        var cardWidth = 0.0;
        for (var i = 0; i < cardCount; i++) cardWidth = Math.Max(cardWidth, Children[i * 2].DesiredSize.Width);

        var unit = cardWidth + ArrowWidth;
        var capacity = ComputeCapacity(availableSize.Width, unit);
        var limit = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;

        double y = 0, width = 0, height = 0, chainX = 0;
        var index = 0;
        for (var rowIndex = 0; index < cardCount; rowIndex++)
        {
            var count = Math.Min(capacity, cardCount - index);
            var reversed = rowIndex % 2 == 1;

            var rowHeight = 0.0;
            for (var j = 0; j < count; j++)
                rowHeight = Math.Max(rowHeight, Children[(index + j) * 2].DesiredSize.Height);

            // 换行：上面那条竖箭头占的高度 + 一点空隙
            if (rowIndex > 0) y += ArrowHeight + RowGap;
            var rowTop = y;

            // 行起点：第一行从左边开始，之后每一行都从「上一行最后一只」的 x 接着排（正向往右、反向往左）
            var startX = rowIndex == 0 ? 0 : chainX;
            var span = (count - 1) * unit + cardWidth;
            startX = reversed
                ? Math.Max(startX, Math.Min((count - 1) * unit, Math.Max(0, limit - cardWidth)))
                : Math.Min(Math.Max(startX, 0), Math.Max(0, limit - span));

            for (var j = 0; j < count; j++)
            {
                var cardIndex = index + j;
                var cardX = reversed ? startX - j * unit : startX + j * unit;
                _layout.Add(new Placed(Children[cardIndex * 2], new Rect(cardX, rowTop, cardWidth, rowHeight),
                    ArrowPlacement.Inline, cardIndex));

                width = Math.Max(width, cardX + cardWidth);
                height = Math.Max(height, rowTop + rowHeight);

                // 这张卡前面的那条箭头（第 0 只没有）
                if (cardIndex == 0) continue;
                var arrow = Children[cardIndex * 2 - 1];
                if (j > 0)
                {
                    // 行内箭头：正向行在左侧，反向行在右侧（两只卡中间）
                    var arrowX = reversed ? cardX + cardWidth : cardX - ArrowWidth;
                    _layout.Add(new Placed(arrow, new Rect(arrowX, rowTop + (rowHeight - ArrowHeight) / 2,
                        ArrowWidth, ArrowHeight), reversed ? ArrowPlacement.Mirrored : ArrowPlacement.Inline, cardIndex));
                }
                else
                {
                    // 换行箭头：竖着放在两行之间，对准上下那两只卡
                    _layout.Add(new Placed(arrow, new Rect(cardX + (cardWidth - ArrowWidth) / 2,
                        rowTop - ArrowHeight - RowGap / 2, ArrowWidth, ArrowHeight),
                        ArrowPlacement.Wrap, cardIndex));
                }
            }

            chainX = reversed ? startX - (count - 1) * unit : startX + (count - 1) * unit;
            index += count;
            y = rowTop + rowHeight;   // 下一行接着这一行往下排
        }

        return new Size(Math.Min(width, limit), height);
    }

    private static int ComputeCapacity(double available, double unit)
    {
        if (unit <= 0) return 1;
        if (double.IsInfinity(available) || double.IsNaN(available) || available <= 0) return int.MaxValue;
        return Math.Max(1, (int)Math.Floor((available + ArrowWidth) / unit));
    }

    // ------------------------------------------------------------------ 摆位置
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var placed in _layout)
        {
            placed.Element.Arrange(placed.Rect);
            if (placed.Element is not TextBlock arrow) continue;

            // 箭头方向随位置变化：行内反向要镜像，换行处改成竖箭头
            switch (placed.Placement)
            {
                case ArrowPlacement.Wrap:
                    SetArrow(arrow, WrapGlyph(_steps[placed.CardIndex].ArrowGlyph), mirrored: false);
                    break;
                case ArrowPlacement.Mirrored:
                    SetArrow(arrow, _steps[placed.CardIndex].ArrowGlyph, mirrored: true);
                    break;
                default:
                    SetArrow(arrow, _steps[placed.CardIndex].ArrowGlyph, mirrored: false);
                    break;
            }
        }

        return finalSize;
    }

    /// <summary>只在真的变了的时候改，避免反复触发重新布局。</summary>
    private static void SetArrow(TextBlock arrow, string text, bool mirrored)
    {
        if (!string.Equals(arrow.Text, text, StringComparison.Ordinal)) arrow.Text = text;
        var transform = mirrored ? Mirror : null;
        if (!ReferenceEquals(arrow.RenderTransform, transform)) arrow.RenderTransform = transform;
    }

    /// <summary>换行处的竖箭头：形态转换（⇄）用 ⇅，其余用 ↓。</summary>
    private static string WrapGlyph(string glyph) => glyph == "⇄" ? "⇅" : "↓";

    private static ScaleTransform CreateMirror()
    {
        var mirror = new ScaleTransform(-1, 1);
        mirror.Freeze();
        return mirror;
    }
}