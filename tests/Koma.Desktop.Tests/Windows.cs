using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Koma.Desktop.Tests;

/// <summary>
/// Reaching into a window the way a reader does: by the label over a field,
/// and by clicking what is there to be clicked.
/// </summary>
/// <remarks>
/// By label rather than by position in the visual tree, so that a field added
/// above another does not silently move what a test is looking at.
/// </remarks>
internal static class Windows
{
    /// <summary>The control under a label, which is how the forms are built.</summary>
    public static T Input<T>(this Window window, string label)
        where T : Control =>
        window.GetVisualDescendants()
            .OfType<StackPanel>()
            .Where(field => field.Children.Count == 2 && field.Children[0] is TextBlock caption && caption.Text == label)
            .Select(field => field.Children[1])
            .OfType<T>()
            .First();

    /// <summary>Clicks a button, as a pointer would.</summary>
    public static void Press(this Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Not named Button: inside this class that name would be this method
    // rather than the type, and Button.ClickEvent just above would not read.
    public static Button FindButton(this Visual visual, Func<Button, bool> matching) => visual.GetVisualDescendants().OfType<Button>().First(matching);
}