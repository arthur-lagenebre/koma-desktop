using Avalonia;

namespace Koma.Desktop;

internal static class Program
{
    // Nothing that touches Avalonia or a synchronisation context may run
    // before AppMain: the platform is not initialised until then.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also what the XAML previewer calls, so it stays public and static.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
