using Avalonia;
using Avalonia.Automation;
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
    private readonly ComboBox direction = new() { ItemsSource = new[] { Text.Of("Left to right"), Text.Of("Right to left") }, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox series = new();
    private readonly TextBox position = new();
    private readonly TextBox total = new();
    private readonly TextBox publisher = new();
    private readonly TextBox imprint = new();
    private readonly TextBox place = new();
    private readonly TextBox edition = new();
    private readonly TextBox trimWidth = new() { Width = 120 };
    private readonly TextBox trimHeight = new() { Width = 120 };
    private readonly TextBox copyright = new();
    private readonly TextBox license = new();
    private readonly TextBox statement = new() { AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox[] modes = Boxes.For(OpenVocabularies.AccessModes);
    private readonly CheckBox[] hazards = Boxes.For(OpenVocabularies.AccessibilityHazards);
    private readonly TextBox summary = new() { AcceptsReturn = true, Height = 60, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock problem = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button save = new() { Content = Text.Of("Save"), IsDefault = true };

    private Rows people = null!;
    private Rows subjects = null!;
    private Rows descriptions = null!;
    private Rows entities = null!;
    private Rows warnings = null!;
    private Rows links = null!;
    private Rows dates = null!;
    private readonly StackPanel fields = new() { Spacing = 6, Margin = new Thickness(16) };
    private readonly WritingNotice writing = new();

    public EditWindow(string path, MetadataEdit current)
    {
        this.path = path;
        this.current = current;

        Title = Text.Of("{0} — Edit metadata", Path.GetFileName(path));
        // Room for the tabs, and a height of its own: a form that scrolls its
        // save button out of reach is a form that cannot save.
        Width = 760;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        title.Text = current.Title;
        language.Text = current.Language;
        direction.SelectedIndex = current.Direction == ReadingDirection.RightToLeft ? 1 : 0;
        series.Text = current.Series?.Name;

        // A volume of a series that carries no number is offered the one its
        // file name starts with, which is where a collection often keeps it.
        position.Text = current.Series?.Position ?? (current.Series is null ? null : FromFileName(path));
        total.Text = current.Series?.Total;

        Boxes.Tick(modes, current.Accessibility?.AccessModes);
        Boxes.Tick(hazards, current.Accessibility?.Hazards);

        summary.Text = current.Accessibility?.Summary;

        var cancel = new Button { Content = Text.Of("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        save.Click += OnSave;

        publisher.Text = current.Publisher;
        imprint.Text = current.Imprint;
        place.Text = current.Place;
        edition.Text = current.Edition;
        trimWidth.Text = current.PhysicalFormat?.Width;
        trimHeight.Text = current.PhysicalFormat?.Height;

        var publication = new StackPanel { Spacing = 6, Margin = new Thickness(12) };

        publication.Children.Add(Field(Text.Of("Title"), title));
        publication.Children.Add(Field(Text.Of("Language (BCP 47, such as fr or en-GB)"), language));
        publication.Children.Add(Field(Text.Of("Reading direction"), direction));
        publication.Children.Add(Field(Text.Of("Publisher"), publisher));
        publication.Children.Add(Field(Text.Of("Imprint"), imprint));
        publication.Children.Add(Field(Text.Of("Place of publication"), place));
        publication.Children.Add(Field(Text.Of("Edition"), edition));
        publication.Children.Add(Dated(current));
        publication.Children.Add(Field(Text.Of("Trimmed width, in millimetres"), trimWidth));
        publication.Children.Add(Field(Text.Of("Trimmed height, in millimetres"), trimHeight));
        publication.Children.Add(Field(Text.Of("Series"), series));
        publication.Children.Add(Field(Text.Of("Number in the series"), position));
        publication.Children.Add(Field(Text.Of("Volumes in the series"), total));
        publication.Children.Add(Field(Text.Of("How the publication is read"), Boxes.Row(modes)));
        publication.Children.Add(Field(Text.Of("What it may do to a reader"), Boxes.Row(hazards)));
        publication.Children.Add(Field(Text.Of("A sentence for a reader deciding whether they can read it"), summary));

        // Tabs rather than one long form: §7 carries more than a window holds
        // at once, and a form of ten sections is a form nobody opens. The
        // save button stays outside them, an edit being saved whole.
        fields.Children.Add(new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = Text.Of("Publication"), Content = new ScrollViewer { Content = publication } },
                new TabItem { Header = Text.Of("People"), Content = new ScrollViewer { Content = People(current) } },
                new TabItem { Header = Text.Of("Subjects"), Content = new ScrollViewer { Content = Subjects(current) } },
                new TabItem { Header = Text.Of("Descriptions"), Content = new ScrollViewer { Content = Descriptions(current) } },
                new TabItem { Header = Text.Of("Story"), Content = new ScrollViewer { Content = Story(current) } },
                new TabItem { Header = Text.Of("Warnings and links"), Content = new ScrollViewer { Content = WarningsAndLinks(current) } },
                new TabItem { Header = Text.Of("Rights"), Content = new ScrollViewer { Content = Rights(current) } }
            }
        });

        fields.Children.Add(writing);
        fields.Children.Add(problem);
        fields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } });

        Content = fields;
    }

    /// <summary>Who the publication is the work of (§7.6), one line each.</summary>
    private StackPanel People(MetadataEdit current)
    {
        people = MetadataRows.People();
        MetadataRows.Fill(people, current.Contributors ?? []);

        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock { Text = Text.Of("The roles of §7.6: writer, artist, colorist, translator, editor…"), Opacity = 0.6, TextWrapping = TextWrapping.Wrap },
                people
            }
        };
    }

    /// <summary>What the publication is about (§7.9): a genre, a theme, a keyword.</summary>
    private StackPanel Subjects(MetadataEdit current)
    {
        subjects = MetadataRows.Subjects();
        MetadataRows.Fill(subjects, current.Subjects ?? []);

        return new StackPanel { Spacing = 8, Margin = new Thickness(12), Children = { subjects } };
    }

    /// <summary>Who and what the story is about (§7.10).</summary>
    private StackPanel Story(MetadataEdit current)
    {
        entities = MetadataRows.Entities();
        MetadataRows.Fill(entities, current.Entities ?? []);

        return new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock { Text = Text.Of("The characters, teams and places of the story, which is a different list from the people who made it."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap },
                entities
            }
        };
    }

    /// <summary>What a reader is warned about (§7.14), and where else to look (§7.15).</summary>
    private StackPanel WarningsAndLinks(MetadataEdit current)
    {
        warnings = MetadataRows.Warnings();
        links = MetadataRows.Links();

        MetadataRows.Fill(warnings, current.Warnings ?? []);
        MetadataRows.Fill(links, current.Links ?? []);

        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(12),
            Children =
            {
                warnings,
                new TextBlock { Text = Text.Of("The ratings a conversion wrote are kept as they are; only the warnings are edited here."), Opacity = 0.6, TextWrapping = TextWrapping.Wrap },
                links
            }
        };
    }

    /// <summary>What may be done with the publication (§7.16).</summary>
    private StackPanel Rights(MetadataEdit current)
    {
        copyright.Text = current.Rights?.Copyright;
        license.Text = current.Rights?.License;
        statement.Text = current.Rights?.Statement;

        return new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(12),
            Children =
            {
                Field(Text.Of("Copyright"), copyright),
                Field(Text.Of("Licence, by its identifier"), license),
                Field(Text.Of("Anything else worth saying"), statement)
            }
        };
    }

    /// <summary>When things happened to the publication (§7.8).</summary>
    private StackPanel Dated(MetadataEdit current)
    {
        dates = MetadataRows.Dates();
        MetadataRows.Fill(dates, current.Dates ?? []);

        return new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = Text.Of("Dates"), Opacity = 0.75 }, dates }
        };
    }

    /// <summary>What the publication says about itself (§7.7).</summary>
    private StackPanel Descriptions(MetadataEdit current)
    {
        descriptions = MetadataRows.Descriptions();
        MetadataRows.Fill(descriptions, current.Descriptions ?? []);

        return new StackPanel { Spacing = 8, Margin = new Thickness(12), Children = { descriptions } };
    }

    /// <summary>
    /// A field under its label, the label being what assistive tools
    /// announce: a text box says its content, never what the content is for.
    /// </summary>
    private static StackPanel Field(string label, Control input)
    {
        AutomationProperties.SetName(input, label);

        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = 0.75 }, input } };
    }

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
            problem.Text = Text.Of("Refused: {0}", refused.Message);
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

        var newAccessibility = new AccessibilityEdit(Boxes.Ticked(modes), Boxes.Ticked(hazards), (summary.Text ?? string.Empty).Trim());

        ContributorEdit[] newPeople = MetadataRows.ReadPeople(people);
        SubjectEdit[] newSubjects = MetadataRows.ReadSubjects(subjects);

        DescriptionEdit[] newDescriptions = MetadataRows.ReadDescriptions(descriptions);
        EntityEdit[] newEntities = MetadataRows.ReadEntities(entities);
        WarningEdit[] newWarnings = MetadataRows.ReadWarnings(warnings);
        LinkEdit[] newLinks = MetadataRows.ReadLinks(links);

        var newRights = new RightsEdit(
            (copyright.Text ?? string.Empty).Trim(),
            (license.Text ?? string.Empty).Trim(),
            (statement.Text ?? string.Empty).Trim());

        string newPublisher = (publisher.Text ?? string.Empty).Trim();
        string newImprint = (imprint.Text ?? string.Empty).Trim();
        string newPlace = (place.Text ?? string.Empty).Trim();
        string newEdition = (edition.Text ?? string.Empty).Trim();
        DateEdit[] newDates = MetadataRows.ReadDates(dates);
        var newFormat = new PhysicalFormatEdit((trimWidth.Text ?? string.Empty).Trim(), (trimHeight.Text ?? string.Empty).Trim());

        return new MetadataEdit(
            newTitle == current.Title ? null : newTitle,
            newLanguage == current.Language ? null : newLanguage,
            newDirection == current.Direction ? null : newDirection,
            newSeries is null || newSeries == current.Series ? null : newSeries,
            Same(newAccessibility, current.Accessibility) ? null : newAccessibility,
            Same(newPeople, current.Contributors) ? null : newPeople,
            Same(newSubjects, current.Subjects) ? null : newSubjects,
            Same(newDescriptions, current.Descriptions) ? null : newDescriptions,
            Same(newEntities, current.Entities) ? null : newEntities,
            Same(newWarnings, current.Warnings) ? null : newWarnings,
            Same(newLinks, current.Links) ? null : newLinks,
            newRights == current.Rights ? null : newRights,
            newPublisher == (current.Publisher ?? string.Empty) ? null : newPublisher,
            newImprint == (current.Imprint ?? string.Empty) ? null : newImprint,
            newPlace == (current.Place ?? string.Empty) ? null : newPlace,
            newEdition == (current.Edition ?? string.Empty) ? null : newEdition,
            Same(newDates, current.Dates) ? null : newDates,
            newFormat == current.PhysicalFormat ? null : newFormat);
    }

    /// <remarks>
    /// Compared by their contents: the record holds lists, which compare by
    /// reference, and an untouched form would otherwise look like a change.
    /// </remarks>
    /// <remarks>
    /// Line by line: the records hold lists, which compare by reference, and
    /// a form nobody touched would otherwise look like a change.
    /// </remarks>
    private static bool Same(ContributorEdit[] left, IReadOnlyList<ContributorEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Name.Trim() == both.Second.Name.Trim()
            && both.First.Organization == both.Second.Organization
            && both.First.Roles.SequenceEqual(both.Second.Roles, StringComparer.Ordinal));

    private static bool Same(DateEdit[] left, IReadOnlyList<DateEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Event == both.Second.Event && both.First.Value.Trim() == both.Second.Value.Trim());

    private static bool Same(EntityEdit[] left, IReadOnlyList<EntityEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Name.Trim() == both.Second.Name.Trim()
            && both.First.Type == both.Second.Type
            && both.First.Role == both.Second.Role);

    private static bool Same(WarningEdit[] left, IReadOnlyList<WarningEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Type == both.Second.Type && both.First.Text.Trim() == both.Second.Text.Trim());

    private static bool Same(LinkEdit[] left, IReadOnlyList<LinkEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Relation == both.Second.Relation
            && both.First.Href.Trim() == both.Second.Href.Trim()
            && both.First.Text.Trim() == both.Second.Text.Trim());

    private static bool Same(DescriptionEdit[] left, IReadOnlyList<DescriptionEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Type == both.Second.Type && both.First.Text.Trim() == both.Second.Text.Trim());

    private static bool Same(SubjectEdit[] left, IReadOnlyList<SubjectEdit>? right) =>
        right is not null
        && left.Length == right.Count
        && left.Zip(right).All(both => both.First.Type == both.Second.Type && both.First.Text.Trim() == both.Second.Text.Trim());

    private static bool Same(AccessibilityEdit left, AccessibilityEdit? right) =>
        right is not null
        && left.AccessModes.SequenceEqual(right.AccessModes, StringComparer.Ordinal)
        && left.Hazards.SequenceEqual(right.Hazards, StringComparer.Ordinal)
        && (left.Summary ?? string.Empty) == (right.Summary ?? string.Empty);

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
