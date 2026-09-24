using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Koma.Core.Model;

namespace Koma.Desktop;

/// <summary>What the reader chose to import, and how.</summary>
/// <param name="Folder">Whether a folder was chosen, to be searched for archives, rather than files.</param>
/// <param name="Destination">
/// The folder the packages are written into, keeping whatever tree the
/// archives sat in; <see langword="null"/> writes each one beside its archive.
/// </param>
/// <param name="AccessModes">How the publications are read (§7.13), which no CBZ says.</param>
/// <param name="Hazards">What they may do to a reader (§7.13).</param>
public sealed record ImportChoice(bool Folder, bool KeepComicInfo, bool NumberFromFileName, string? Destination, IReadOnlyList<string> AccessModes, IReadOnlyList<string> Hazards);

/// <summary>
/// Asks what to convert and what to carry over, before any picker opens.
/// </summary>
/// <remarks>
/// A folder is offered beside files because a collection arrives by the
/// shelf-ful, not one volume at a time, and the ComicInfo question is asked
/// once for the lot: it is the same answer for a whole collection, and the
/// wrong moment to ask it is after two hundred conversions.
/// </remarks>
internal sealed class ImportWindow : Window
{
    private readonly CheckBox comicInfo = new()
    {
        Content = Text.Of("Keep the original ComicInfo.xml in each package"),
        IsChecked = true
    };

    private readonly CheckBox numbering = new()
    {
        Content = Text.Of("Number the volumes from the start of their file names")
    };

    private readonly RadioButton beside = new() { Content = Text.Of("Write each package beside its archive"), IsChecked = true, GroupName = "destination" };
    private readonly RadioButton elsewhere = new() { GroupName = "destination" };

    private readonly CheckBox[] modes = Boxes.For(OpenVocabularies.AccessModes);
    private readonly CheckBox[] hazards = Boxes.For(OpenVocabularies.AccessibilityHazards);

    private string? destination;

    /// <summary>
    /// Asks where the packages go, and falls back to beside the archives when
    /// no folder is chosen.
    /// </summary>
    private async Task ChooseDestination()
    {
        if (elsewhere.IsChecked != true)
        {
            destination = null;
            return;
        }

        IReadOnlyList<IStorageFolder> chosen = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Text.Of("Write the packages into"),
            AllowMultiple = false
        });

        destination = chosen.Count == 0 ? null : chosen[0].TryGetLocalPath();

        if (destination is null)
        {
            beside.IsChecked = true;
            return;
        }

        elsewhere.Content = destination;
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

    private ImportChoice Choice(bool folder) => new(folder, comicInfo.IsChecked == true, numbering.IsChecked == true, destination, Boxes.Ticked(modes), Boxes.Ticked(hazards));

    public ImportWindow()
    {
        Title = Text.Of("Import comic book archives");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var files = new Button { Content = Text.Of("Choose files…"), IsDefault = true };
        var folder = new Button { Content = Text.Of("Choose a folder…") };
        var cancel = new Button { Content = Text.Of("Cancel"), IsCancel = true };

        elsewhere.Content = Text.Of("Write them into another folder…");
        elsewhere.IsCheckedChanged += async (_, _) => await ChooseDestination();

        files.Click += (_, _) => Close(Choice(folder: false));
        folder.Click += (_, _) => Close(Choice(folder: true));
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = Text.Of("A folder is searched for .cbz archives, subfolders included. Each package is written beside its archive, and an existing file is never replaced."),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75
                },
                beside,
                elsewhere,
                new TextBlock
                {
                    Text = Text.Of("A folder of archives keeps its tree: what sat in a subfolder is written into the same subfolder there."),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.55
                },
                comicInfo,
                Field(Text.Of("How the publications are read"), Boxes.Row(modes)),
                Field(Text.Of("What they may do to a reader"), Boxes.Row(hazards)),
                new TextBlock
                {
                    Text = Text.Of("A CBZ says nothing about reading its pages, so a conversion can only say what it is told. Left empty, the packages say nothing either, and a reader is warned of it."),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.55
                },
                numbering,
                new TextBlock
                {
                    Text = Text.Of("A collection often numbers its files and not its metadata: 1 - Ante demonium.cbz. The number is then written as the volume's place in its series, and each conversion says it did so."),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.55
                },
                new TextBlock
                {
                    Text = Text.Of("ComicInfo is never normative for KOMA: it travels unchanged, for readers that still want it."),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.55
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, folder, files }
                }
            }
        };
    }
}
