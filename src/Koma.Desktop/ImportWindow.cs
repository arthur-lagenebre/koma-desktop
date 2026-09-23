using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Koma.Desktop;

/// <summary>What the reader chose to import, and how.</summary>
/// <param name="Folder">Whether a folder was chosen, to be searched for archives, rather than files.</param>
public sealed record ImportChoice(bool Folder, bool KeepComicInfo);

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
        Content = "Keep the original ComicInfo.xml in each package",
        IsChecked = true
    };

    public ImportWindow()
    {
        Title = "Import comic book archives";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;

        var files = new Button { Content = "Choose files…", IsDefault = true };
        var folder = new Button { Content = "Choose a folder…" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };

        files.Click += (_, _) => Close(new ImportChoice(Folder: false, comicInfo.IsChecked == true));
        folder.Click += (_, _) => Close(new ImportChoice(Folder: true, comicInfo.IsChecked == true));
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = "A folder is searched for .cbz archives, subfolders included. Each package is written beside its archive, and an existing file is never replaced.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75
                },
                comicInfo,
                new TextBlock
                {
                    Text = "ComicInfo is never normative for KOMA: it travels unchanged, for readers that still want it.",
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
