using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Koma.Core.Rendering;
using Koma.Core.Writing;

namespace Koma.Desktop;

/// <summary>
/// The pages of a publication, and what the manifest says about the one
/// chosen: its roles, its span, its side of the spread, and what a reader who
/// cannot see it is told.
/// </summary>
/// <remarks>
/// One page is saved at a time, and each save rewrites the package, which
/// costs a copy of its entries and not a copy of its pages. The window says
/// whether anything was saved, so that the reader behind it knows to reopen
/// the publication.
/// </remarks>
internal sealed class PagesWindow : Window
{
    private static readonly (string Label, SpreadPosition Position)[] Positions =
    [
        ("Wherever it falls", SpreadPosition.Auto),
        ("Left of the spread", SpreadPosition.Left),
        ("Right of the spread", SpreadPosition.Right),
        ("Alone, centred", SpreadPosition.Center)
    ];

    private readonly string path;
    private readonly ListBox pages = new() { Width = 220 };
    private readonly TextBox roles = new();
    private readonly CheckBox span = new() { Content = "Drawn across the whole spread (§8.5)" };
    private readonly ComboBox position = new() { ItemsSource = Positions.Select(p => p.Label).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox alternative = new() { AcceptsReturn = true, Height = 72, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox decorative = new() { Content = "Decorative: carries nothing to describe (§8.7)" };
    private readonly TextBlock problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button save = new() { Content = "Save this page", IsDefault = true };

    private string[] items = [];
    private bool filling;

    public PagesWindow(string path, string? current)
    {
        this.path = path;

        Title = $"{Path.GetFileName(path)} — Pages";
        Width = 760;
        Height = 520;

        pages.SelectionChanged += (_, _) => Fill();
        save.Click += OnSave;

        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close(Saved);

        var form = new StackPanel { Spacing = 8, Margin = new Thickness(16, 0, 0, 0) };

        form.Children.Add(Field("Roles, separated by spaces (§8.4)", roles));
        form.Children.Add(span);
        form.Children.Add(Field("Place in the spread (§8.8)", position));
        form.Children.Add(Field("Alternative text (§8.7)", alternative));
        form.Children.Add(decorative);
        form.Children.Add(problem);
        form.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { close, save } });

        Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), Children = { pages, form } };

        Load(current);
    }

    /// <summary>Whether anything was written, which the reader behind needs to know.</summary>
    public bool Saved { get; private set; }

    private static StackPanel Field(string label, Control input) => new() { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = 0.75 }, input } };

    private void Load(string? current)
    {
        IReadOnlyList<(string Item, string Roles)> listed;

        try
        {
            listed = PublicationEditor.Pages(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            problem.Text = e.Message;
            save.IsEnabled = false;
            return;
        }

        items = [.. listed.Select(p => p.Item)];
        pages.ItemsSource = listed.Select((p, i) => $"{i + 1}.  {p.Item}   {p.Roles}").ToArray();
        pages.SelectedIndex = Math.Max(0, Array.IndexOf(items, current));
    }

    /// <summary>Puts what the manifest says about the chosen page into the form.</summary>
    private void Fill()
    {
        if (pages.SelectedIndex < 0 || pages.SelectedIndex >= items.Length)
            return;

        PageEdit page;

        try
        {
            page = PublicationEditor.CurrentPage(path, items[pages.SelectedIndex]);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            problem.Text = e.Message;
            return;
        }

        // The form is filled, not edited: nothing here is a reader's choice.
        filling = true;
        roles.Text = string.Join(' ', page.Roles ?? []);
        span.IsChecked = page.PageSpan == 2;
        position.SelectedIndex = Array.FindIndex(Positions, p => p.Position == (page.SpreadPosition ?? SpreadPosition.Auto));
        alternative.Text = page.AlternativeText;
        decorative.IsChecked = page.Decorative == true;
        problem.Text = string.Empty;
        filling = false;
    }

    private async void OnSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (filling || pages.SelectedIndex < 0)
            return;

        string item = items[pages.SelectedIndex];
        var edit = new PageEdit(
            [.. (roles.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            span.IsChecked == true ? 2 : 1,
            Positions[Math.Max(position.SelectedIndex, 0)].Position,
            (alternative.Text ?? string.Empty).Trim(),
            decorative.IsChecked == true);

        save.IsEnabled = false;
        problem.Text = string.Empty;

        try
        {
            await Task.Run(() => PublicationEditor.EditPage(path, item, edit, DateTimeOffset.UtcNow));
            Saved = true;
            Load(item);
        }
        catch (Exception refused) when (refused is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            problem.Text = refused.Message;
        }

        save.IsEnabled = true;
    }
}
