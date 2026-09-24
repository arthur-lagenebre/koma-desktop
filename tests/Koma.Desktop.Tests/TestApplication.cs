using Avalonia;
using Avalonia.Headless;
using Koma.Desktop;
using Koma.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApplication))]

namespace Koma.Desktop.Tests;

/// <summary>
/// The application the headless tests run, which is the application itself:
/// its styles and its controls, drawn to nothing instead of to a screen.
/// </summary>
public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
