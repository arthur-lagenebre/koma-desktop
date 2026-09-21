using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Koma.Core.Rendering;
using Koma.Core.Writing;

namespace Koma.Desktop;

/// <summary>
/// Edits the fields a conversion most often has to guess: the title, the
/// language, the reading direction and the series.
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
    private readonly TextBlock problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button save = new() { Content = "Save", IsDefault = true };

    public EditWindow(string path, MetadataEdit current)
    {
        this.path = path;
        this.current = current;

        Title = $"{Path.GetFileName(path)} — Edit metadata";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        title.Text = current.Title;
        language.Text = current.Language;
        direction.SelectedIndex = current.Direction == ReadingDirection.RightToLeft ? 1 : 0;
        series.Text = current.Series?.Name;
        position.Text = current.Series?.Position;
        total.Text = current.Series?.Total;

        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        save.Click += OnSave;

        var fields = new StackPanel { Spacing = 6, Margin = new Thickness(16) };

        fields.Children.Add(Field("Title", title));
        fields.Children.Add(Field("Language (BCP 47, such as fr or en-GB)", language));
        fields.Children.Add(Field("Reading direction", direction));
        fields.Children.Add(Field("Series", series));
        fields.Children.Add(Field("Number in the series", position));
        fields.Children.Add(Field("Volumes in the series", total));
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

        save.IsEnabled = false;
        problem.Text = string.Empty;

        try
        {
            await Task.Run(() => PublicationEditor.EditMetadata(path, edit, DateTimeOffset.UtcNow));
            Close(true);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem.Text = refused.Message;
            save.IsEnabled = true;
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

        return new MetadataEdit(
            newTitle == current.Title ? null : newTitle,
            newLanguage == current.Language ? null : newLanguage,
            newDirection == current.Direction ? null : newDirection,
            newSeries is null || newSeries == current.Series ? null : newSeries);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
