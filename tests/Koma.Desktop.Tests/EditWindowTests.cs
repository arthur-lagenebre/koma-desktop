using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// The metadata of a publication, as a form.
/// </summary>
[Collection(DrawnSuites.Name)]
public sealed class EditWindowTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-edit-window").FullName;

    [AvaloniaFact]
    public void FillsItsFormFromThePublication()
    {
        string path = Copy("valid-minimal.koma");
        var window = new EditWindow(path, PublicationEditor.Current(path));
        window.Show();

        // What the corpus package says: its title, its language, and the
        // right-to-left reading of §7.11.
        Assert.Equal("Corpus de conformite", window.Input<TextBox>("Title").Text);
        Assert.Equal("fr", window.Input<TextBox>("Language (BCP 47, such as fr or en-GB)").Text);
        Assert.Equal(1, window.Input<ComboBox>("Reading direction").SelectedIndex);
        Assert.Equal("Pages decrites.", window.Input<TextBox>("A sentence for a reader deciding whether they can read it").Text);
    }

    [AvaloniaFact]
    public void SaysWhyItRefusesAndKeepsTheFormAsItWas()
    {
        string path = Copy("valid-minimal.koma");
        byte[] before = File.ReadAllBytes(path);

        var window = new EditWindow(path, PublicationEditor.Current(path));
        window.Show();

        // A language that is no language tag: §7.4 wants BCP 47, and the
        // schema refuses the rest.
        window.Input<TextBox>("Language (BCP 47, such as fr or en-GB)").Text = "français";
        window.FindButton(b => b.Content as string == "Save").Press();

        Assert.Equal("français", window.Input<TextBox>("Language (BCP 47, such as fr or en-GB)").Text);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public void KeepsTheSaveButtonInTheWindowWhateverTheTabsHold()
    {
        // Seven tabs used to push it out of a stack with nothing to scroll.
        string path = Path.Combine(folder, "valid-minimal.koma");
        File.Copy(Corpus.Package("valid-minimal.koma"), path);

        var window = new EditWindow(path, PublicationEditor.Current(path));
        window.Show();

        Button save = window.FindButton(b => b.Content as string == "Save");
        Point corner = save.TranslatePoint(new Point(0, save.Bounds.Height), window) ?? new Point(0, double.MaxValue);

        Assert.True(corner.Y <= window.Height, $"the save button ends at {corner.Y} in a window {window.Height} high");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The operating system's to clean up.
        }
    }

    private string Copy(string package)
    {
        string path = Path.Combine(folder, package);
        File.Copy(Corpus.Package(package), path);

        return path;
    }
}
