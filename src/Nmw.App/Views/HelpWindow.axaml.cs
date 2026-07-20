using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nmw.App.Views;

/// <summary>간단 사용설명서 창. 앱 시작 시 자동 표시되고, [도움말] 버튼으로 언제든 다시 연다.</summary>
public sealed partial class HelpWindow : Window
{
    /// <summary>도움말 창을 만든다.</summary>
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
