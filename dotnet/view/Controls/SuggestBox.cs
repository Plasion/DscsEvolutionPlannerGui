using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DscsEvolutionPlanner.Core;

namespace DscsEvolutionPlanner.View;

/// <summary>
/// 名字输入框（起点 / 终点共用）：输入框 + 候选下拉（名称 + 匹配度）。
/// 候选由视图模型通过 <see cref="SuggestionProvider"/> 提供；键盘与鼠标交互都由本控件自己处理，
/// 所以页面上没有事件处理器，也没有 x:Name 引用。
/// </summary>
public sealed class SuggestBox : Control
{
    private static readonly IReadOnlyList<NameSuggestion> None = Array.Empty<NameSuggestion>();

    private TextBox _input = null!;
    private Popup _popup = null!;
    private ListBox _list = null!;
    private bool _inner;

    static SuggestBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SuggestBox), new FrameworkPropertyMetadata(typeof(SuggestBox)));
    }

    public SuggestBox()
    {
        Focusable = false;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_input is not null)
        {
            _input.TextChanged -= OnTextChanged;
            _input.PreviewKeyDown -= OnKeyDown;
            _input.LostKeyboardFocus -= OnLostFocus;
        }

        _input = (TextBox)GetTemplateChild("PART_Input")!;
        _popup = (Popup)GetTemplateChild("PART_Popup")!;
        _list = (ListBox)GetTemplateChild("PART_List")!;

        _input.TextChanged += OnTextChanged;
        _input.PreviewKeyDown += OnKeyDown;
        _input.LostKeyboardFocus += OnLostFocus;
        _list.PreviewMouseLeftButtonUp += OnPick;
        _popup.Closed += (_, _) => _list.SelectedIndex = -1;

        PushText(Text);
    }

    /// <summary>焦点离开就收起候选（Tab 跑去下一个输入框时不留一个悬着的下拉）。</summary>
    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _popup.IsOpen = false;

    // ------------------------------------------------------------------ 依赖属性
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SuggestBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextPropertyChanged));

    /// <summary>输入框里的文本（双向绑定到视图模型）。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SuggestBox)d).PushText((string?)e.NewValue);

    public static readonly DependencyProperty SuggestionProviderProperty = DependencyProperty.Register(
        nameof(SuggestionProvider), typeof(Func<string, IReadOnlyList<NameSuggestion>>), typeof(SuggestBox),
        new PropertyMetadata(null));

    /// <summary>候选来源（由视图绑定到视图模型的方法）。</summary>
    public Func<string, IReadOnlyList<NameSuggestion>>? SuggestionProvider
    {
        get => (Func<string, IReadOnlyList<NameSuggestion>>?)GetValue(SuggestionProviderProperty);
        set => SetValue(SuggestionProviderProperty, value);
    }

    private static readonly DependencyPropertyKey SuggestionsKey = DependencyProperty.RegisterReadOnly(
        nameof(Suggestions), typeof(IReadOnlyList<NameSuggestion>), typeof(SuggestBox),
        new PropertyMetadata(None));

    public static readonly DependencyProperty SuggestionsProperty = SuggestionsKey.DependencyProperty;

    /// <summary>当前候选列表（模板里的下拉绑定它）。</summary>
    public IReadOnlyList<NameSuggestion> Suggestions => (IReadOnlyList<NameSuggestion>)GetValue(SuggestionsProperty);

    // ------------------------------------------------------------------ 文本与候选
    /// <summary>
    /// 把视图模型的值推进输入框（只搬文本，不再回调视图模型，避免来回打架）。
    /// 这种情况多半是程序性赋值（预填、重置、换了候选），所以顺手把候选收起。
    /// </summary>
    private void PushText(string? text)
    {
        if (_input is null) return;
        var value = text ?? "";
        if (string.Equals(_input.Text, value, StringComparison.Ordinal)) return;

        _inner = true;
        _input.Text = value;
        _input.CaretIndex = value.Length;
        _inner = false;
        _popup.IsOpen = false;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_inner) return;

        // 一律以输入框自己的文本为准：TwoWay 绑定的回写在 TextChanged 之后
        var text = _input.Text ?? "";
        Text = text;
        RefreshSuggestions(text, open: true);
    }

    private void RefreshSuggestions(string text, bool open)
    {
        var items = string.IsNullOrWhiteSpace(text)
            ? None
            : SuggestionProvider?.Invoke(text) ?? None;

        SetValue(SuggestionsKey, items);
        _list.SelectedIndex = items.Count > 0 ? 0 : -1;
        _popup.IsOpen = items.Count > 0 && open && _input.IsKeyboardFocusWithin;
    }

    // ------------------------------------------------------------------ 键盘 / 鼠标
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when _popup.IsOpen:
                _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.Items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up when _popup.IsOpen:
                _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter when _popup.IsOpen && _list.SelectedItem is NameSuggestion enter:
                Accept(enter);
                e.Handled = true;
                break;
            case Key.Escape when _popup.IsOpen:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        if (_list.SelectedItem is NameSuggestion pick) Accept(pick);
    }

    /// <summary>选中候选：把名字填进输入框并收起候选（文本变化会照常同步给视图模型）。</summary>
    private void Accept(NameSuggestion pick)
    {
        _popup.IsOpen = false;
        Text = pick.Name;   // TwoWay 绑定会回写输入框，PushText 顺手把候选保持收起
    }

    /// <summary>截图验收用：按当前文本展开候选并返回弹层内容。</summary>
    public FrameworkElement? OpenPopup()
    {
        RefreshSuggestions(Text, open: true);
        _popup.IsOpen = true;
        return _popup.Child as FrameworkElement;
    }

    public void ClosePopup() => _popup.IsOpen = false;
}