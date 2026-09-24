using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Koma.Desktop;

/// <summary>
/// Asks before something that cannot be taken back.
/// </summary>
/// <remarks>
/// Only for what is irreversible. A question asked about everything is a
/// question nobody reads, and the rest of this application either writes what
/// was asked for or refuses and says why.
/// </remarks>
internal static class Confirm
{
    public static async Task<bool> Ask(Window owner, string question)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var yes = new Button { Content = Text.Of("Yes, take it out") };
        var no = new Button { Content = Text.Of("Cancel"), IsCancel = true, IsDefault = true };

        var window = new Window
        {
            Title = owner.Title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        yes.Click += (_, _) => window.Close(true);
        no.Click += (_, _) => window.Close(false);

        window.Content = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes }
                }
            }
        };

        return await window.ShowDialog<bool>(owner);
    }
}
