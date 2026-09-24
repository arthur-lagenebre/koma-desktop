using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Koma.Desktop.Tests;

/// <summary>
/// The report of an import or of a publication, which is read and copied.
/// </summary>
public sealed class ReportWindowTests
{
    [AvaloniaFact]
    public void ShowsWhatItWasGivenWithoutLettingItBeEdited()
    {
        var window = new ReportWindow("A report", $"schema-invalid:manifest — …{Environment.NewLine}front-cover-missing — …");
        window.Show();

        TextBox report = window.GetVisualDescendants().OfType<TextBox>().First();

        Assert.Contains("front-cover-missing", report.Text, StringComparison.Ordinal);
        Assert.True(report.IsReadOnly);
    }
}
