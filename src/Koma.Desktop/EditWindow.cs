using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Koma.Core.Model;
using Koma.Core.Rendering;
using Koma.Core.Writing;

namespace Koma.Desktop;

/// <summary>
/// Edits the fields a conversion most often has to guess: the title, the
/// language, the reading direction, the series, and what the publication says
/// about reading it.
/// </summary>
/// <remarks>
/// <para>
/// Only what changed is sent, so that saving an untouched form writes
/// nothing and leaves the release identity of §7.2.1 where it was.
/// </para>
/// <para>
/// The file is written from here, off the interface thread, so that a
/// refusal — an empty title, a language that is not a tag, a disk that is
/// full — is shown beside the field that caused it, with the form still
/// filled in, rather than after the window has gone.
/// </para>
/// </remarks>
internal sealed class EditWindow : Window
{
    private readonly string path;
    private readonly MetadataEdit current;
    private readonly TextBox title = new();
    private readonly TextBox language = new();
    private readonly ComboBox direction = new() { ItemsSource = new[] { "Left to right", "Right to left" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox series = new();
    private readonly TextBox position = new();
    private readonly TextBox total = new();
    private readonly CheckBox[] modes = [.. OpenVocabularies.AccessModes.Select(m => new CheckBox { Content = m })];
    private readonly CheckBox[] hazards = [.. OpenVocabularies.AccessibilityHazards.Select(h => new CheckBox { Content = h })];
    private readonly TextBox summary = new() { AcceptsReturn = true, Height = 60, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button save = new() { Content = "Save", IsDefault = true };
    private readonly StackPanel fields = new() { Spacing = 6, Margin = new Thickness(16) };
    private readonly WritingNotice writing = new();

    public EditWindow(string path, MetadataEdit current)
    {
        this.path = path;
        this.current = current;

        Title = $"{Path.GetFileName(path)} — Edit metadata";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        title.Text = current.Title;
        language.Text = current.Language;
        direction.SelectedIndex = current.Direction == ReadingDirection.RightToLeft ? 1 : 0;
        series.Text = current.Series?.Name;

        // A volume of a series that carries no number is offered the one its
        // file name starts with, which is where a collection often keeps it.
        position.Text = current.Series?.Position ?? (current.Series is null ? null : FromFileName(path));
        total.Text = current.Series?.Total;

        foreach (CheckBox box in modes)
            box.IsChecked = current.Accessibility?.AccessModes.Contains((string)box.Content!, StringComparer.Ordinal) == true;

        foreach (CheckBox box in hazards)
            box.IsChecked = current.Accessibility?.Hazards.Contains((string)box.Content!, StringComparer.Ordinal) == true;

        summary.Text = current.Accessibility?.Summary;

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        save.Click += OnSave;

        fields.Children.Add(Field("Title", title));
        fields.Children.Add(Field("Language (BCP 47, such as fr or en-GB)", language));
        fields.Children.Add(Field("Reading direction", direction));
        fields.Children.Add(Field("Series", series));
        fields.Children.Add(Field("Number in the series", position));
        fields.Children.Add(Field("Volumes in the series", total));
        fields.Children.Add(Field("How the publication is read", Boxes(modes)));
        fields.Children.Add(Field("What it may do to a reader", Boxes(hazards)));
        fields.Children.Add(Field("A sentence for a reader deciding whether they can read it", summary));
        fields.Children.Add(writing);
        fields.Children.Add(problem);
        fields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } });

        Content = fields;
    }

    private static StackPanel Field(string label, Control input) => new() { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = 0.75 }, input } };

    private async void OnSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MetadataEdit edit = Changes();

        if (edit == new MetadataEdit())
        {
            Close(false);
            return;
        }

        problem.Text = string.Empty;
        Busy(true);

        try
        {
            await Task.Run(() => PublicationEditor.EditMetadata(path, edit, DateTimeOffset.UtcNow));
            Close(true);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem.Text = refused.Message;
            Busy(false);
        }
    }

    /// <summary>
    /// The fields that differ from what the publication says, and only those.
    /// </summary>
    /// <remarks>
    /// An emptied series field is left alone rather than taken for a removal:
    /// removing a series is not something this form offers yet, and doing it
    /// by accident would lose the numbering with it.
    /// </remarks>
    private MetadataEdit Changes()
    {
        string newTitle = (title.Text ?? string.Empty).Trim();
        string newLanguage = (language.Text ?? string.Empty).Trim();
        ReadingDirection newDirection = direction.SelectedIndex == 1 ? ReadingDirection.RightToLeft : ReadingDirection.LeftToRight;
        SeriesEdit? newSeries = string.IsNullOrWhiteSpace(series.Text) ? null : new SeriesEdit(series.Text.Trim(), Blank(position.Text), Blank(total.Text));

        var newAccessibility = new AccessibilityEdit(Checked(modes), Checked(hazards), (summary.Text ?? string.Empty).Trim());

        return new MetadataEdit(
            newTitle == current.Title ? null : newTitle,
            newLanguage == current.Language ? null : newLanguage,
            newDirection == current.Direction ? null : newDirection,
            newSeries is null || newSeries == current.Series ? null : newSeries,
            Same(newAccessibility, current.Accessibility) ? null : newAccessibility);
    }

    private static string[] Checked(CheckBox[] boxes) => [.. boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!)];

    /// <remarks>
    /// Compared by their contents: the record holds lists, which compare by
    /// reference, and an untouched form would otherwise look like a change.
    /// </remarks>
    private static bool Same(AccessibilityEdit left, AccessibilityEdit? right) =>
        right is not null
        && left.AccessModes.SequenceEqual(right.AccessModes, StringComparer.Ordinal)
        && left.Hazards.SequenceEqual(right.Hazards, StringComparer.Ordinal)
        && (left.Summary ?? string.Empty) == (right.Summary ?? string.Empty);

    /// <summary>A row of boxes, wrapped when the window is too narrow for them.</summary>
    private static WrapPanel Boxes(CheckBox[] boxes)
    {
        var panel = new WrapPanel();

        foreach (CheckBox box in boxes)
        {
            box.Margin = new Thickness(0, 0, 12, 0);
            panel.Children.Add(box);
        }

        return panel;
    }

    /// <summary>
    /// Shows that the package is being written, and takes the form out of
    /// reach while it is.
    /// </summary>
    /// <remarks>
    /// Writing a publication copies every page of it into a new file and puts
    /// that file in place of the old one. On an album of two hundred pages
    /// that is long enough to wonder whether anything is happening, and long
    /// enough for a second save to start over the first.
    /// </remarks>
    private void Busy(bool writing)
    {
        this.writing.Show(writing);
        fields.IsEnabled = !writing;
        Cursor = new Cursor(writing ? StandardCursorType.Wait : StandardCursorType.Arrow);
    }

    /// <summary>The digits a file name starts with, which a collection numbers its volumes by.</summary>
    private static string? FromFileName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).TrimStart();
        int digits = 0;

        while (digits < name.Length && char.IsAsciiDigit(name[digits]))
            digits++;

        return digits is > 0 and <= 4 ? name[..digits].TrimStart('0') is { Length: > 0 } number ? number : "0" : null;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
