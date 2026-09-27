using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Koma.Desktop.Tests;

/// <summary>
/// The report of an import or of a publication, which is read and copied.
/// </summary>
[Collection(DrawnSuites.Name)]
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

        ListBox report = window.GetVisualDescendants().OfType<ListBox>().First();
        string[] shown = [.. report.ItemsSource!.OfType<string>()];

        Assert.Contains("un.cbz", shown);
        Assert.Contains("deux.cbz", shown);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "2 converted, 0 not converted.");
    }

    [AvaloniaFact]
    public void ShowsWhatItWasGivenWithoutLettingItBeEdited()
    {
        var window = new ReportWindow("A report", $"schema-invalid:manifest — …{Environment.NewLine}front-cover-missing — …");
        window.Show();

        // A list, so a screen reader reads a fault at a time where a
        // multiline box would read one long paragraph.
        ListBox report = window.GetVisualDescendants().OfType<ListBox>().First();

        Assert.Equal(2, report.ItemCount);
        Assert.Contains(report.ItemsSource!.OfType<string>(), line => line.Contains("front-cover-missing", StringComparison.Ordinal));
    }
}
