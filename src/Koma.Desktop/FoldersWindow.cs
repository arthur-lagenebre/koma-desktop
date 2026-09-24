using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Koma.Desktop;

/// <summary>
/// The folders the library watches: what is in them is on the shelf.
/// </summary>
/// <remarks>
/// Removing a folder takes its publications off the shelf and forgets where
/// the reader was in them; it touches no file. That is worth saying in the
/// window, since a list of folders with a Remove button reads like a list of
/// files with one.
/// </remarks>
internal sealed class FoldersWindow : Window
{
    private readonly ListBox folders = new() { Height = 220 };
    private readonly Button remove = new();

    private readonly Button privacy = new();
    private readonly List<string> watched;
    private readonly HashSet<string> secret;

    public FoldersWindow(IReadOnlyList<string> watched, IReadOnlyList<string> secret)
    {
        this.watched = [.. watched];
        this.secret = [.. secret];

        Title = Text.Of("Watched folders");
        Width = 620;
        SizeToContent = SizeToContent.Height;

        var add = new Button { Content = Text.Of("Add folder…") };
        var close = new Button { Content = Text.Of("Close"), IsCancel = true, IsDefault = true };

        remove.Content = Text.Of("Remove");
        privacy.Content = Text.Of("Keep off the shelf");
        add.Click += OnAdd;
        remove.Click += (_, _) => Remove();
        privacy.Click += (_, _) => TogglePrivacy();
        close.Click += (_, _) => Close(Changed);
        folders.SelectionChanged += (_, _) => Chosen();

        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                folders,
                new TextBlock
                {
                    Text = Text.Of("Removing a folder takes its publications off the shelf and forgets where you were in them. No file is touched."),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { add, privacy, remove, close }
                }
            }
        };

        Fill();
    }

    /// <summary>Whether the library watches other folders than it did.</summary>
    public bool Changed { get; private set; }

    /// <summary>The folders as they stand, for the library to keep.</summary>
    public IReadOnlyList<string> Watched => watched;

    /// <summary>The folders whose publications stay off the shelf.</summary>
    public IReadOnlyList<string> Secret => [.. secret];

    private void Fill()
    {
        int chosen = folders.SelectedIndex;

        folders.ItemsSource = watched.Select(f => secret.Contains(f) ? Text.Of("{0} — private", f) : f).ToArray();
        folders.SelectedIndex = Math.Min(chosen, watched.Count - 1);
        Chosen();
    }

    /// <summary>What the buttons can do to the folder in hand.</summary>
    private void Chosen()
    {
        remove.IsEnabled = folders.SelectedIndex >= 0;
        privacy.IsEnabled = folders.SelectedIndex >= 0;
        privacy.Content = folders.SelectedIndex >= 0 && secret.Contains(watched[folders.SelectedIndex]) ? Text.Of("Show on the shelf") : Text.Of("Keep off the shelf");
    }

    private void TogglePrivacy()
    {
        if (folders.SelectedIndex < 0)
            return;

        string folder = watched[folders.SelectedIndex];

        if (!secret.Remove(folder))
            secret.Add(folder);

        Changed = true;
        Fill();
    }

    private async void OnAdd(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> chosen = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Text.Of("Add a folder of KOMA publications"),
            AllowMultiple = true
        });

        // A folder the platform cannot name as a path is one the scan cannot
        // walk, so it is not added.
        string[] added = [.. chosen.Select(f => f.TryGetLocalPath()).OfType<string>().Except(watched, StringComparer.Ordinal)];

        if (added.Length == 0)
            return;

        watched.AddRange(added);
        Changed = true;
        Fill();
    }

    private void Remove()
    {
        if (folders.SelectedIndex < 0)
            return;

        secret.Remove(watched[folders.SelectedIndex]);
        watched.RemoveAt(folders.SelectedIndex);
        Changed = true;
        Fill();
    }
}
