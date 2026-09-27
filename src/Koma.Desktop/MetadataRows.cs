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
