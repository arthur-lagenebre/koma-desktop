using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Koma.Desktop;

/// <summary>
/// Says that a publication is being written, for as long as it takes.
/// </summary>
/// <remarks>
/// Writing copies every page into a new file and puts it in place of the old
/// one, so a save is not instant on a long album. A bar that runs says the
/// application is working where a form frozen for a second says nothing.
/// </remarks>
internal sealed class WritingNotice : StackPanel
{
    private readonly ProgressBar bar = new() { IsIndeterminate = true, Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch };

    public WritingNotice()
    {
        Spacing = 4;
        IsVisible = false;
        Children.Add(new TextBlock { Text = "Writing the publication…", Opacity = 0.75 });
        Children.Add(bar);
    }

    public void Show(bool writing)
    {
        IsVisible = writing;

        // A bar that keeps animating behind a closed form costs a frame a
        // tick for nothing.
        bar.IsIndeterminate = writing;
    }
}
