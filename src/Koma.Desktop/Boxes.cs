using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Koma.Desktop;

/// <summary>
/// A vocabulary as a row of boxes: several tokens can be true at once, which
/// is what §7.13 asks of access modes and hazards.
/// </summary>
internal static class Boxes
{
    public static CheckBox[] For(IReadOnlyList<string> tokens) => [.. tokens.Select(token => new CheckBox { Content = token })];

    /// <summary>A row that wraps when the window is too narrow for it.</summary>
    public static WrapPanel Row(CheckBox[] boxes)
    {
        var panel = new WrapPanel();

        foreach (CheckBox box in boxes)
        {
            box.Margin = new Thickness(0, 0, 12, 0);
            panel.Children.Add(box);
        }

        return panel;
    }

    public static string[] Ticked(CheckBox[] boxes) => [.. boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!)];

    public static void Tick(CheckBox[] boxes, IReadOnlyList<string>? ticked)
    {
        foreach (CheckBox box in boxes)
            box.IsChecked = ticked?.Contains((string)box.Content!, StringComparer.Ordinal) == true;
    }
}
