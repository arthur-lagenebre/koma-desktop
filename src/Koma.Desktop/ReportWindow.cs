using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Koma.Desktop;

/// <summary>
/// A report to read and copy: what an import converted, assumed, gave up or
/// refused, written as it happens.
/// </summary>
/// <remarks>
/// A window of its own rather than the status line, because a conversion
/// says more than a line holds, and because what it says — a page taken for
/// the cover, a language assumed — is worth reading before the publication
/// is trusted, and worth copying into a bug report when it is wrong.
/// </remarks>
internal sealed class ReportWindow : Window
{
    private readonly TextBlock heading = new() { Margin = new Thickness(8, 8, 8, 0), Opacity = 0.75 };
    private readonly ProgressBar bar = new() { Height = 4, Margin = new Thickness(8, 6, 8, 0), IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox body = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"),
        Margin = new Thickness(8)
    };

    public ReportWindow(string title, string report)
    {
        Title = title;
        Width = 820;
        Height = 560;

        heading.IsVisible = false;
        body.Text = report;

        AutomationProperties.SetName(body, title);

        var panel = new DockPanel();

        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(heading);
        panel.Children.Add(bar);
        panel.Children.Add(body);

        Content = panel;
    }

    /// <summary>
    /// Says where the work is, above the lines it has produced so far.
    /// </summary>
    /// <remarks>
    /// A count rather than a bar alone: converting eighty volumes takes long
    /// enough that "how many are left" is the question being asked.
    /// </remarks>
    public void Working(string what, int done, int total)
    {
        heading.IsVisible = true;
        heading.Text = what;
        bar.IsVisible = true;
        bar.Maximum = Math.Max(total, 1);
        bar.Value = done;
    }

    /// <summary>Says the work is over, leaving what it produced to be read.</summary>
    public void Done(string what)
    {
        heading.IsVisible = true;
        heading.Text = what;
        bar.IsVisible = false;
    }

    /// <summary>Adds what has just happened, and keeps it in view.</summary>
    public void Append(string lines)
    {
        body.Text += lines;

        // The end is where the new lines are, and where a reader watching a
        // long import is looking.
        body.CaretIndex = body.Text?.Length ?? 0;
    }
}
