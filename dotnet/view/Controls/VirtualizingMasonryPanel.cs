using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 虚拟化的「瀑布流」面板（图鉴列表用）：卡片按列往下流，只生成视口内的容器。
/// 视图通过 <see cref="ScrollToIndex"/> 指定「把第几张滚进视口」，不必从外部拿控件引用。
/// </summary>
public sealed class VirtualizingMasonryPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>卡片之间的间距。</summary>
    private const double Gap = 8;

    private Size _itemSize;
    private int _columns = 1;
    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public static readonly DependencyProperty ScrollToIndexProperty = DependencyProperty.Register(
        nameof(ScrollToIndex), typeof(int), typeof(VirtualizingMasonryPanel),
        new FrameworkPropertyMetadata(-1, OnScrollToIndexChanged));

    /// <summary>把这一项滚到视口中间；-1 表示不动。</summary>
    public int ScrollToIndex
    {
        get => (int)GetValue(ScrollToIndexProperty);
        set => SetValue(ScrollToIndexProperty, value);
    }

    private static void OnScrollToIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (VirtualizingMasonryPanel)d;
        if ((int)e.NewValue >= 0) panel.ScrollIndexIntoView((int)e.NewValue);
    }

    // ------------------------------------------------------------------ 对外接口
    /// <summary>列数（摆放后才有意义）。</summary>
    public int Columns => _columns;

    /// <summary>把第 index 项滚进视口。</summary>
    public void ScrollIndexIntoView(int index) => BringIndexIntoView(index);

    protected override void BringIndexIntoView(int index)
    {
        if (_columns <= 0 || _itemSize.Height <= 0) return;
        var row = index / _columns;
        var top = row * (_itemSize.Height + Gap);
        SetVerticalOffset(top - Math.Max(0, (_viewport.Height - _itemSize.Height) / 2));
    }

    // ------------------------------------------------------------------ 测量 / 摆放
    protected override Size MeasureOverride(Size availableSize)
    {
        var count = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

        _viewport = new Size(
            double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

        if (count == 0)
        {
            CleanUpItems(0, -1);
            _extent = new Size(0, 0);
            NotifyScrollOwner();
            return _viewport;
        }

        EnsureItemSize();
        if (_itemSize.Width <= 0 || _itemSize.Height <= 0) return _viewport;

        _columns = Math.Max(1, (int)Math.Floor((_viewport.Width + Gap) / (_itemSize.Width + Gap)));
        var rows = (int)Math.Ceiling(count / (double)_columns);
        _extent = new Size(_viewport.Width, rows * _itemSize.Height + Math.Max(0, rows - 1) * Gap);
        _offset.Y = Clamp(_offset.Y, 0, Math.Max(0, _extent.Height - _viewport.Height));

        var firstRow = Math.Max(0, (int)Math.Floor(_offset.Y / (_itemSize.Height + Gap)));
        var lastRow = Math.Min(rows - 1, (int)Math.Floor((_offset.Y + _viewport.Height) / (_itemSize.Height + Gap)) + 1);
        var first = firstRow * _columns;
        var last = Math.Min(count - 1, (lastRow + 1) * _columns - 1);

        CleanUpItems(first, last);
        GenerateItems(first, last);
        NotifyScrollOwner();

        if (Environment.GetEnvironmentVariable("DSH_CODEX_DEBUG") == "1")
            DscsEvolutionPlanner.Core.Log.Write($"[masonry] viewport={_viewport} item={_itemSize} cols={_columns} rows={rows} extent={_extent} children={InternalChildren.Count}");

        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        var children = InternalChildren;
        var stepX = _viewport.Width / Math.Max(1, _columns);
        var stepY = _itemSize.Height + Gap;

        for (var i = 0; i < children.Count; i++)
        {
            var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0) continue;

            var column = index % _columns;
            var row = index / _columns;
            var x = column * stepX + (stepX - _itemSize.Width) / 2;   // 列内居中，右边不留参差
            var y = row * stepY - _offset.Y;
            children[i].Arrange(new Rect(x, y, _itemSize.Width, _itemSize.Height));
        }

        return finalSize;
    }

    /// <summary>用一个样本量出卡片尺寸（模板统一，其余卡片同尺寸）。</summary>
    private void EnsureItemSize()
    {
        if (_itemSize.Width > 0 && _itemSize.Height > 0) return;

        var generator = ItemContainerGenerator;
        var position = generator.GeneratorPositionFromIndex(0);
        using (generator.StartAt(position, GeneratorDirection.Forward, true))
        {
            if (generator.GenerateNext(out var isNew) is not UIElement child) return;
            if (isNew) AddInternalChild(child);
            generator.PrepareItemContainer(child);
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _itemSize = child.DesiredSize;
        }
    }

    private void CleanUpItems(int minIndex, int maxIndex)
    {
        var children = InternalChildren;
        var generator = ItemContainerGenerator;

        for (var i = children.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);
            if (itemIndex >= minIndex && itemIndex <= maxIndex) continue;

            generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
    }

    private void GenerateItems(int first, int last)
    {
        var generator = ItemContainerGenerator;
        var position = generator.GeneratorPositionFromIndex(first);
        var childIndex = position.Offset == 0 ? position.Index : position.Index + 1;

        using (generator.StartAt(position, GeneratorDirection.Forward, true))
        {
            for (var index = first; index <= last; index++, childIndex++)
            {
                if (generator.GenerateNext(out var isNewlyRealized) is not UIElement child) break;

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(_itemSize.Width, double.PositiveInfinity));
            }
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            RemoveInternalChildRange(0, InternalChildren.Count);
        InvalidateMeasure();
    }

    // ------------------------------------------------------------------ IScrollInfo
    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(_offset.Y - 32);
    public void LineDown() => SetVerticalOffset(_offset.Y + 32);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - 96);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + 96);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        var next = Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (Math.Abs(next - _offset.Y) < 0.01) return;

        _offset.Y = next;
        InvalidateMeasure();
        NotifyScrollOwner();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            if (!ReferenceEquals(InternalChildren[i], visual)) continue;

            var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            BringIndexIntoView(Math.Max(0, index));
            return rectangle;
        }
        return rectangle;
    }

    private static double Clamp(double value, double min, double max) =>
        value < min ? min : value > max ? max : value;

    private void NotifyScrollOwner() => ScrollOwner?.InvalidateScrollInfo();
}