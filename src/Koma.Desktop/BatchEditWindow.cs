using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Koma.Core.Model;
using Koma.Core.Rendering;
using Koma.Core.Writing;

namespace Koma.Desktop;

/// <summary>
/// What an edit of several publications changes about each of them. A field
/// nobody ticked is a field nobody meant to change.
/// </summary>
/// <param name="NumberFrom">
/// The number the first publication takes, the others following it in the
/// order the shelf shows them; <see langword="null"/> numbers nothing.
/// </param>
public sealed record BatchEdit(
    string? Series,
    string? Total,
    int? NumberFrom,
    string? Language,
    ReadingDirection? Direction,
    AccessibilityEdit? Accessibility);

/// <summary>
/// Edits what several publications have in common: their series, their
/// language, how they are read.
/// </summary>
/// <remarks>
/// <para>
/// Ticking is what says a field is meant: a form where an empty box cleared a
/// title would make an edit of forty volumes a dangerous thing to open.
/// </para>
/// <para>
/// The title is not here. A title belongs to one publication, and the one
/// thing a batch must not do is give forty volumes the same name.
/// </para>
/// </remarks>
internal sealed class BatchEditWindow : Window
{
    private static readonly (string Label, ReadingDirection Direction)[] Directions =
    [
        ("Left to right", ReadingDirection.LeftToRight),
        ("Right to left", ReadingDirection.RightToLeft)
    ];

    private readonly CheckBox seriesTicked = new();
    private readonly TextBox series = new();
    private readonly TextBox total = new();

    private readonly CheckBox numberTicked = new();
    private readonly TextBox numberFrom = new() { Text = "1", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly CheckBox languageTicked = new();
    private readonly TextBox language = new();

    private readonly CheckBox directionTicked = new();
    private readonly ComboBox direction = new() { SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly CheckBox accessibilityTicked = new();
    private readonly CheckBox[] modes = Boxes.For(OpenVocabularies.AccessModes);
    private readonly CheckBox[] hazards = Boxes.For(OpenVocabularies.AccessibilityHazards);
    private readonly TextBox summary = new() { AcceptsReturn = true, Height = 52, TextWrapping = TextWrapping.Wrap };

    public BatchEditWindow(int publications)
    {
        Title = Text.Of("{0} publications — Edit together", publications);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        direction.ItemsSource = Directions.Select(d => Text.Of(d.Label)).ToArray();

        // Inside a panel, so Under names the panel and not this: the box says
        // Series, and so must the field it governs.
        AutomationProperties.SetName(series, Text.Of("Series"));

        seriesTicked.Content = Text.Of("Series");
        numberTicked.Content = Text.Of("Number them in the order they are shown, from");
        languageTicked.Content = Text.Of("Language (BCP 47, such as fr or en-GB)");
        directionTicked.Content = Text.Of("Reading direction");
        accessibilityTicked.Content = Text.Of("What they say about reading them");

        var save = new Button { Content = Text.Of("Save"), IsDefault = true };
        var cancel = new Button { Content = Text.Of("Cancel"), IsCancel = true };

        save.Click += (_, _) => Close(Plan());
        cancel.Click += (_, _) => Close(null);

        var fields = new StackPanel { Spacing = 8, Margin = new Thickness(16) };

        fields.Children.Add(new TextBlock
        {
            Text = Text.Of("Only the fields you tick are written; the rest are left as they are in each publication."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });

        fields.Children.Add(Under(seriesTicked, new StackPanel
        {
            Spacing = 4,
            Children = { series, Labelled(Text.Of("Volumes in the series"), total) }
        }));

        fields.Children.Add(Under(numberTicked, numberFrom));
        fields.Children.Add(Under(languageTicked, language));
        fields.Children.Add(Under(directionTicked, direction));
        fields.Children.Add(Under(accessibilityTicked, new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Labelled(Text.Of("How the publications are read"), Boxes.Row(modes)),
                Labelled(Text.Of("What they may do to a reader"), Boxes.Row(hazards)),
                Labelled(Text.Of("A sentence for a reader deciding whether they can read them"), summary)
            }
        }));

        fields.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, save }
        });

        Content = new ScrollViewer { Content = fields };
    }

    /// <summary>
    /// A field under its box, greyed until the box is ticked, and named after
    /// the box: here the box is what says what the field is for.
    /// </summary>
    private static StackPanel Under(CheckBox ticked, Control field)
    {
        field.IsEnabled = false;
        ticked.IsCheckedChanged += (_, _) => field.IsEnabled = ticked.IsChecked == true;

        if (ticked.Content is string label)
            AutomationProperties.SetName(field, label);

        return new StackPanel { Spacing = 4, Children = { ticked, field } };
    }

    /// <summary>
    /// A field under its label, the label being what assistive tools
    /// announce: a text box says its content, never what the content is for.
    /// </summary>
    private static StackPanel Labelled(string label, Control input)
    {
        AutomationProperties.SetName(input, label);

        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = 0.75 }, input } };
    }

    private BatchEdit Plan() => new(
        seriesTicked.IsChecked == true ? (series.Text ?? string.Empty).Trim() : null,
        seriesTicked.IsChecked == true ? (total.Text ?? string.Empty).Trim() : null,
        numberTicked.IsChecked == true && int.TryParse(numberFrom.Text, out int from) ? from : null,
        languageTicked.IsChecked == true ? (language.Text ?? string.Empty).Trim() : null,
        directionTicked.IsChecked == true ? Directions[Math.Max(direction.SelectedIndex, 0)].Direction : null,
        accessibilityTicked.IsChecked == true ? new AccessibilityEdit(Boxes.Ticked(modes), Boxes.Ticked(hazards), (summary.Text ?? string.Empty).Trim()) : null);
}
