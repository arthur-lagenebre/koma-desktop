using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Koma.Desktop;

/// <summary>
/// A report to read and copy: what an import converted, assumed, gave up or
/// refused.
/// </summary>
/// <remarks>
/// A window of its own rather than the status line, because a conversion
/// says more than a line holds, and because what it says — a page taken for
/// the cover, a language assumed — is worth reading before the publication
/// is trusted, and worth copying into a bug report when it is wrong.
/// </remarks>
internal sealed class ReportWindow : Window
{
    public ReportWindow(string title, string report)
    {
        Title = title;
        Width = 820;
        Height = 560;

        Content = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            Margin = new Thickness(8)
        };
    }
}
