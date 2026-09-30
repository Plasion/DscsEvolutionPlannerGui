using System.Windows;
using System.Windows.Media;

namespace DscsEvolutionPlanner.Core;

/// <summary>
/// 取 App.xaml 里定义的画刷：只给「按资源键自绘」的控件用（关系网连线、路线箭头）。
/// 视图模型的着色一律走视图层的 BrushKeyConverter，核心层不参与界面决策。
/// </summary>
public static class Theme
{
    public static Brush Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}