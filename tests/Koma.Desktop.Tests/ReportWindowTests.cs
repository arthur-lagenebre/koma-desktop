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
    public void GrowsAsTheWorkGoesAndKeepsTheEndInView()
    {
        // An import writes its report as it happens: eighty volumes are worth
        // watching, and a page taken for a cover is worth seeing when it is.
        var window = new ReportWindow("An import", string.Empty);
        window.Show();

        window.Working("Importing… 1 / 2", 1, 2);
        window.Append($"un.cbz{Environment.NewLine}");
        window.Working("Importing… 2 / 2", 2, 2);
        window.Append($"deux.cbz{Environment.NewLine}");
        window.Done("2 converted, 0 not converted.");

        TextBox report = window.GetVisualDescendants().OfType<TextBox>().First();

        Assert.Contains("un.cbz", report.Text, StringComparison.Ordinal);
        Assert.Contains("deux.cbz", report.Text, StringComparison.Ordinal);
        Assert.Equal(report.Text!.Length, report.CaretIndex);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2 converted, 0 not converted.");
    }

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
