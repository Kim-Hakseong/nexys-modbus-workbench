using Avalonia;
using Avalonia.Headless;
using Nmw.App;
using Nmw.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Nmw.App.Tests;

/// <summary>헤드리스 UI 테스트용 Avalonia 앱 빌더.</summary>
public static class TestAppBuilder
{
    /// <summary>헤드리스 플랫폼으로 앱을 구성한다.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
