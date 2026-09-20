using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Koma.Desktop;

internal sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // A file named on the command line is what opening a .koma file
            // from the shell sends.
            if (desktop.Args is [var path, ..])
                window.OpenPath(path);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
