using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nmw.App.ViewModels;
using Nmw.App.Views;
using Xunit;

namespace Nmw.App.Tests;

/// <summary>도움말 창 스모크: 시작 시 자동 표시 + 닫은 뒤 재표시.</summary>
public sealed class HelpWindowSmokeTests
{
    private static HelpWindow? FindHelpWindow(MainWindow owner) =>
        owner.OwnedWindows.OfType<HelpWindow>().FirstOrDefault();

    [AvaloniaFact]
    public void HelpWindow_ShownAutomaticallyOnStartup()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        var help = FindHelpWindow(window);
        Assert.NotNull(help);
        Assert.True(help.IsVisible);
        Assert.Equal("간단 사용설명서", help.Title);

        window.Close();
    }

    [AvaloniaFact]
    public void HelpWindow_CanBeClosedAndMainKeepsRunning()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        var help = FindHelpWindow(window);
        Assert.NotNull(help);
        help.Close();
        Assert.Null(FindHelpWindow(window));
        Assert.True(window.IsVisible); // 도움말을 닫아도 메인은 유지

        window.Close();
    }
}
