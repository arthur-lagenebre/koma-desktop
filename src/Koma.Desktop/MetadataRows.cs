using Avalonia.Controls;
using Avalonia.Layout;
using Koma.Core.Model;
using Koma.Core.Writing;

namespace Koma.Desktop;

/// <summary>
/// The two lists of §7 a reader edits by hand: who a publication is the work
/// of, and what it is about.
/// </summary>
/// <remarks>
/// Built here rather than in each window, since the edit window writes them
/// for one publication and the batch window for forty, and a row that differed
/// between the two would be a row that behaved differently.
/// </remarks>
internal static class MetadataRows
{
    private static readonly string[] SubjectTypes = [.. OpenVocabularies.SubjectTypes];
    private static readonly string[] DescriptionTypes = [.. OpenVocabularies.DescriptionTypes];
    private static readonly string[] EntityTypes = [.. OpenVocabularies.EntityTypes];
    private static readonly string[] EntityRoles = ["", .. OpenVocabularies.EntityRoles];
    private static readonly string[] ContentWarnings = [.. OpenVocabularies.ContentWarnings];
    private static readonly string[] LinkRelations = [.. OpenVocabularies.LinkRelations];
    private static readonly string[] DateEvents = [.. OpenVocabularies.DateEvents];

    public static Rows People() => new(Text.Of("Add someone"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("Name"), new TextBox { Width = 220 }),
            Field(Text.Of("Roles, separated by spaces"), new TextBox { Width = 240 }),
            new CheckBox { Content = Text.Of("An organization"), VerticalAlignment = VerticalAlignment.Bottom }
        }
    });

    public static Rows Subjects() => new(Text.Of("Add a subject"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("Kind"), new ComboBox { ItemsSource = SubjectTypes, SelectedIndex = 0, Width = 150 }),
            Field(Text.Of("Subject"), new TextBox { Width = 340 })
        }
    });

    public static Rows Descriptions() => new(Text.Of("Add a description"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("Kind"), new ComboBox { ItemsSource = DescriptionTypes, SelectedIndex = 0, Width = 150 }),
            Field(Text.Of("Text"), new TextBox { Width = 380, Height = 90, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap })
        }
    });

    public static void Fill(Rows rows, IReadOnlyList<DescriptionEdit> descriptions)
    {
        rows.Fill(descriptions.Count);

        foreach ((StackPanel line, DescriptionEdit description) in rows.Lines.Zip(descriptions))
        {
            Input<ComboBox>(line, 0).SelectedIndex = Math.Max(0, Array.IndexOf(DescriptionTypes, description.Type));
            Input<TextBox>(line, 1).Text = description.Text;
        }
    }

    public static DescriptionEdit[] ReadDescriptions(Rows rows) =>
    [
        .. rows.Lines.Select(line => new DescriptionEdit(
            Input<ComboBox>(line, 0).SelectedItem as string ?? "summary",
            Input<TextBox>(line, 1).Text ?? string.Empty))
    ];

    public static Rows Entities() => new(Text.Of("Add a character or a place"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("Name"), new TextBox { Width = 220 }),
            Field(Text.Of("Kind"), new ComboBox { ItemsSource = EntityTypes, SelectedIndex = 0, Width = 150 }),
            Field(Text.Of("Part in the story"), new ComboBox { ItemsSource = EntityRoles, SelectedIndex = 0, Width = 150 })
        }
    });

    /// <summary>
    /// The schemes met most often, offered without being imposed: a scheme is
    /// somebody else's vocabulary, and this list is a convenience rather than
    /// a table this project keeps.
    /// </summary>
    private static readonly string[] Schemes = ["cero", "esrb", "pegi", "bbfc", "acb", "usk", "csm"];

    public static Rows Ratings() => new(Text.Of("Add a rating"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("Whose classification"), new AutoCompleteBox { ItemsSource = Schemes, Width = 160, FilterMode = AutoCompleteFilterMode.StartsWith }),
            Field(Text.Of("What it says"), new TextBox { Width = 120 }),
            Field(Text.Of("Where it applies, as two capitals"), new TextBox { Width = 120 })
        }
    });

    public static void Fill(Rows rows, IReadOnlyList<RatingEdit> ratings)
    {
        rows.Fill(ratings.Count);

        foreach ((StackPanel line, RatingEdit rating) in rows.Lines.Zip(ratings))
        {
            Input<AutoCompleteBox>(line, 0).Text = rating.Scheme;
            Input<TextBox>(line, 1).Text = rating.Value;
            Input<TextBox>(line, 2).Text = rating.Region;
        }
    }

    public static RatingEdit[] ReadRatings(Rows rows) =>
    [
        .. rows.Lines.Select(line => new RatingEdit(
            Input<AutoCompleteBox>(line, 0).Text ?? string.Empty,
            Input<TextBox>(line, 1).Text ?? string.Empty,
            Input<TextBox>(line, 2).Text ?? string.Empty))
    ];

    public static Rows Warnings() => new(Text.Of("Add a warning"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("What a reader is warned about"), new ComboBox { ItemsSource = ContentWarnings, SelectedIndex = 0, Width = 180 }),
            Field(Text.Of("A word about it"), new TextBox { Width = 320 })
        }
    });

    public static Rows Links() => new(Text.Of("Add a link"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("What is at the other end"), new ComboBox { ItemsSource = LinkRelations, SelectedIndex = 0, Width = 180 }),
            Field(Text.Of("Address"), new TextBox { Width = 300 }),
            Field(Text.Of("What to call it"), new TextBox { Width = 180 })
        }
    });

    public static Rows Dates() => new(Text.Of("Add a date"), () => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            Field(Text.Of("What happened"), new ComboBox { ItemsSource = DateEvents, SelectedIndex = 0, Width = 180 }),
            Field(Text.Of("When, as 2026 or 2026-05 or 2026-05-23"), new TextBox { Width = 220 })
        }
    });

    public static void Fill(Rows rows, IReadOnlyList<DateEdit> dates)
    {
        rows.Fill(dates.Count);

        foreach ((StackPanel line, DateEdit date) in rows.Lines.Zip(dates))
        {
            Input<ComboBox>(line, 0).SelectedIndex = Math.Max(0, Array.IndexOf(DateEvents, date.Event));
            Input<TextBox>(line, 1).Text = date.Value;
        }
    }

    public static DateEdit[] ReadDates(Rows rows) =>
    [
        .. rows.Lines.Select(line => new DateEdit(
            Input<ComboBox>(line, 0).SelectedItem as string ?? "publication",
            Input<TextBox>(line, 1).Text ?? string.Empty))
    ];

    public static void Fill(Rows rows, IReadOnlyList<EntityEdit> entities)
    {
        rows.Fill(entities.Count);

        foreach ((StackPanel line, EntityEdit entity) in rows.Lines.Zip(entities))
        {
            Input<TextBox>(line, 0).Text = entity.Name;
            Input<ComboBox>(line, 1).SelectedIndex = Math.Max(0, Array.IndexOf(EntityTypes, entity.Type));
            Input<ComboBox>(line, 2).SelectedIndex = Math.Max(0, Array.IndexOf(EntityRoles, entity.Role));
        }
    }

    public static void Fill(Rows rows, IReadOnlyList<WarningEdit> warnings)
    {
        rows.Fill(warnings.Count);

        foreach ((StackPanel line, WarningEdit warning) in rows.Lines.Zip(warnings))
        {
            Input<ComboBox>(line, 0).SelectedIndex = Math.Max(0, Array.IndexOf(ContentWarnings, warning.Type));
            Input<TextBox>(line, 1).Text = warning.Text;
        }
    }

    public static void Fill(Rows rows, IReadOnlyList<LinkEdit> links)
    {
        rows.Fill(links.Count);

        foreach ((StackPanel line, LinkEdit link) in rows.Lines.Zip(links))
        {
            Input<ComboBox>(line, 0).SelectedIndex = Math.Max(0, Array.IndexOf(LinkRelations, link.Relation));
            Input<TextBox>(line, 1).Text = link.Href;
            Input<TextBox>(line, 2).Text = link.Text;
        }
    }

    public static EntityEdit[] ReadEntities(Rows rows) =>
    [
        .. rows.Lines.Select(line => new EntityEdit(
            Input<TextBox>(line, 0).Text ?? string.Empty,
            Input<ComboBox>(line, 1).SelectedItem as string ?? "character",
            Input<ComboBox>(line, 2).SelectedItem as string ?? string.Empty))
    ];

    public static WarningEdit[] ReadWarnings(Rows rows) =>
    [
        .. rows.Lines.Select(line => new WarningEdit(
            Input<ComboBox>(line, 0).SelectedItem as string ?? "other",
            Input<TextBox>(line, 1).Text ?? string.Empty))
    ];

    public static LinkEdit[] ReadLinks(Rows rows) =>
    [
        .. rows.Lines.Select(line => new LinkEdit(
            Input<ComboBox>(line, 0).SelectedItem as string ?? "other",
            Input<TextBox>(line, 1).Text ?? string.Empty,
            Input<TextBox>(line, 2).Text ?? string.Empty))
    ];

    public static void Fill(Rows rows, IReadOnlyList<ContributorEdit> people)
    {
        rows.Fill(people.Count);

        foreach ((StackPanel line, ContributorEdit contributor) in rows.Lines.Zip(people))
        {
            Input<TextBox>(line, 0).Text = contributor.Name;
            Input<TextBox>(line, 1).Text = string.Join(' ', contributor.Roles);
            ((CheckBox)line.Children[2]).IsChecked = contributor.Organization;
        }
    }

    public static void Fill(Rows rows, IReadOnlyList<SubjectEdit> subjects)
    {
        rows.Fill(subjects.Count);

        foreach ((StackPanel line, SubjectEdit subject) in rows.Lines.Zip(subjects))
        {
            Input<ComboBox>(line, 0).SelectedIndex = Math.Max(0, Array.IndexOf(SubjectTypes, subject.Type));
            Input<TextBox>(line, 1).Text = subject.Text;
        }
    }

    public static ContributorEdit[] ReadPeople(Rows rows) =>
    [
        .. rows.Lines.Select(line => new ContributorEdit(
            Input<TextBox>(line, 0).Text ?? string.Empty,
            [.. (Input<TextBox>(line, 1).Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            ((CheckBox)line.Children[2]).IsChecked == true))
    ];

    public static SubjectEdit[] ReadSubjects(Rows rows) =>
    [
        .. rows.Lines.Select(line => new SubjectEdit(
            Input<ComboBox>(line, 0).SelectedItem as string ?? "keyword",
            Input<TextBox>(line, 1).Text ?? string.Empty))
    ];

    /// <summary>The input of a field, a field being a label and its input.</summary>
    private static T Input<T>(StackPanel line, int at)
        where T : Control =>
        (T)((StackPanel)line.Children[at]).Children[1];

    private static StackPanel Field(string label, Control input)
    {
        Avalonia.Automation.AutomationProperties.SetName(input, label);

        return new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Opacity = 0.75 }, input } };
    }
}
