using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// The metadata of a publication, as a form.
/// </summary>
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
