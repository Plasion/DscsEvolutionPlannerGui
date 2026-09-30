using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 把「点击 / 双击」直接接到视图模型的命令上：XAML 里只写 Command 绑定，
/// 页面上不再出现 Click / MouseLeftButtonUp 之类的事件处理器。
/// 放在按钮上时按钮在外观上没有任何默认样式，视觉完全由内容模板决定。
/// </summary>
public static class AttachedCommand
{
    // ------------------------------------------------------------------ 单击
    public static readonly DependencyProperty ClickCommandProperty = DependencyProperty.RegisterAttached(
        "ClickCommand", typeof(ICommand), typeof(AttachedCommand),
        new PropertyMetadata(null, OnClickCommandChanged));

    public static void SetClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ClickCommandProperty, value);

    public static ICommand? GetClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ClickCommandProperty);

    private static void OnClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewMouseLeftButtonUp -= OnClick;
        if (e.NewValue is ICommand) element.PreviewMouseLeftButtonUp += OnClick;
    }

    private static void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject element) return;
        var command = GetClickCommand(element);
        if (command is null) return;

        var parameter = GetClickParameter(element) ?? (element as FrameworkElement)?.DataContext;
        if (command.CanExecute(parameter)) command.Execute(parameter);
    }

    public static readonly DependencyProperty ClickParameterProperty = DependencyProperty.RegisterAttached(
        "ClickParameter", typeof(object), typeof(AttachedCommand), new PropertyMetadata(null));

    public static void SetClickParameter(DependencyObject element, object? value) =>
        element.SetValue(ClickParameterProperty, value);

    public static object? GetClickParameter(DependencyObject element) =>
        element.GetValue(ClickParameterProperty);

    // ------------------------------------------------------------------ 双击
    public static readonly DependencyProperty DoubleClickCommandProperty = DependencyProperty.RegisterAttached(
        "DoubleClickCommand", typeof(ICommand), typeof(AttachedCommand),
        new PropertyMetadata(null, OnDoubleClickCommandChanged));

    public static void SetDoubleClickCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(DoubleClickCommandProperty, value);

    public static ICommand? GetDoubleClickCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(DoubleClickCommandProperty);

    private static void OnDoubleClickCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;
        control.PreviewMouseDoubleClick -= OnDoubleClick;
        if (e.NewValue is ICommand) control.PreviewMouseDoubleClick += OnDoubleClick;
    }

    private static void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject element) return;
        var command = GetDoubleClickCommand(element);
        if (command is null) return;

        var parameter = GetDoubleClickParameter(element) ?? (element as FrameworkElement)?.DataContext;
        if (!command.CanExecute(parameter)) return;

        command.Execute(parameter);
        e.Handled = true;   // 双击后不要再触发一次单击
    }

    public static readonly DependencyProperty DoubleClickParameterProperty = DependencyProperty.RegisterAttached(
        "DoubleClickParameter", typeof(object), typeof(AttachedCommand), new PropertyMetadata(null));

    public static void SetDoubleClickParameter(DependencyObject element, object? value) =>
        element.SetValue(DoubleClickParameterProperty, value);

    public static object? GetDoubleClickParameter(DependencyObject element) =>
        element.GetValue(DoubleClickParameterProperty);
}