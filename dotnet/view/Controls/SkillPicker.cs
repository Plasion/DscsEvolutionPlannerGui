using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 多选下拉框（「需沿途收集的继承技」）：按钮外观 + 点开是「筛选 + 勾选列表 + 全选筛选结果 / 清空」的弹层。
/// 弹层开关由本控件自己处理（按钮的点击），弹层里的数据与命令全部来自本控件的依赖属性——
/// 这样它不依赖 DataContext 继承，换页、换宿主都不会失效。视图模型只提供数据与动作。
/// </summary>
public sealed class SkillPicker : Control
{
    /// <summary>刚关掉的时间点：弹层因为 StaysOpen=False 被这一次点击关掉时不要再打开，否则会「关不掉」。</summary>
    private DateTime _lastClosed = DateTime.MinValue;

    static SkillPicker()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SkillPicker), new FrameworkPropertyMetadata(typeof(SkillPicker)));
    }

    public SkillPicker()
    {
        Focusable = false;
    }

    // ============================ 依赖属性 ============================
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(SkillPicker),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

    /// <summary>弹层是否展开（模板里的 Popup 绑定它）。</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) ((SkillPicker)d)._lastClosed = DateTime.Now;
    }

    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
        nameof(Summary), typeof(string), typeof(SkillPicker), new PropertyMetadata(""));

    /// <summary>按钮上的汇总文字（已选技能名，或占位提示）。</summary>
    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SkillPicker), new PropertyMetadata(null));

    /// <summary>弹层里显示（并已按关键字筛选过）的继承技。</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(
        nameof(FilterText), typeof(string), typeof(SkillPicker),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>弹层顶部输入框里的筛选关键字。</summary>
    public string FilterText
    {
        get => (string)GetValue(FilterTextProperty);
        set => SetValue(FilterTextProperty, value);
    }

    public static readonly DependencyProperty SelectAllCommandProperty = DependencyProperty.Register(
        nameof(SelectAllCommand), typeof(ICommand), typeof(SkillPicker), new PropertyMetadata(null));

    /// <summary>「全选筛选结果」。</summary>
    public ICommand? SelectAllCommand
    {
        get => (ICommand?)GetValue(SelectAllCommandProperty);
        set => SetValue(SelectAllCommandProperty, value);
    }

    public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(
        nameof(ClearCommand), typeof(ICommand), typeof(SkillPicker), new PropertyMetadata(null));

    /// <summary>「清空」。</summary>
    public ICommand? ClearCommand
    {
        get => (ICommand?)GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    // ============================ 交互 ============================
    /// <summary>点按钮：开 / 关弹层（刚被这次点击关掉时不再重开）。</summary>
    private void ToggleOpen()
    {
        if (IsOpen)
        {
            IsOpen = false;
            return;
        }

        if ((DateTime.Now - _lastClosed).TotalMilliseconds < 200) return;
        IsOpen = true;
    }

    public void ClosePopup() => IsOpen = false;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_Field") is ButtonBase field)
        {
            field.Click -= OnFieldClick;
            field.Click += OnFieldClick;
        }
    }

    private void OnFieldClick(object sender, RoutedEventArgs e) => ToggleOpen();

    /// <summary>截图验收用：展开弹层并返回其内容。</summary>
    public FrameworkElement? OpenPopup()
    {
        IsOpen = true;
        UpdateLayout();
        return GetTemplateChild("PART_Popup") is Popup popup ? popup.Child as FrameworkElement : null;
    }
}
