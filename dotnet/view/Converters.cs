using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 把视图模型给的「主题资源键」解析成画刷：核心与视图模型都不需要认识 WPF 资源字典，
/// 颜色只在视图层落地。
/// </summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrEmpty(key) && parameter is string fallback) key = fallback;
        if (string.IsNullOrEmpty(key)) return Brushes.Transparent;

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// 角色色 → 徽标底色 / 描边：视图模型只给角色资源键，这里按透明度派生出淡色，
/// 布局外的转换器 Parameter 会被忽视，所以两种派生各用一个转换器实例。
/// </summary>
public sealed class RoleBadgeBrushConverter : IValueConverter
{
    /// <summary>底色 / 描边各自的透明度（ARGB 的高两位）。</summary>
    public string Alpha { get; set; } = "26";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key) return Brushes.Transparent;
        if (Application.Current?.TryFindResource(key) is not SolidColorBrush source) return Brushes.Transparent;

        var alpha = byte.TryParse(Alpha, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (byte)0x26;
        var color = source.Color;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>
/// 布尔 → 可见性，可选取反：参数写 "invert" 表示 true 时折叠。
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}