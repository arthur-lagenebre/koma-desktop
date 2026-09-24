using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Koma.Core.Writing;
using Koma.TestSupport;

namespace Koma.Desktop.Tests;

/// <summary>
/// What the windows say about themselves to whoever is not looking at them.
/// </summary>
/// <remarks>
/// A text box announces its content and a combo box its value; neither says
/// what it is for. The label above a field is what says that, and this holds
/// the windows to putting it where a screen reader will find it.
/// </remarks>
public sealed class NamedForAssistiveToolsTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("koma-named").FullName;

    [AvaloniaFact]
    public void TheEditWindowNamesItsFields()
    {
        string path = Path.Combine(folder, "valid-minimal.koma");
        File.Copy(Corpus.Package("valid-minimal.koma"), path);

        var window = new EditWindow(path, PublicationEditor.Current(path));
        window.Show();

        Assert.Empty(Unnamed(window));
    }

    [AvaloniaFact]
    public void TheImportWindowNamesItsFields()
    {
        var window = new ImportWindow();
        window.Show();

        Assert.Empty(Unnamed(window));
    }

    [AvaloniaFact]
    public void TheBatchWindowNamesItsFields()
    {
        var window = new BatchEditWindow(3);
        window.Show();

        Assert.Empty(Unnamed(window));
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

    /// <summary>
    /// The inputs a screen reader would announce without saying what they
    /// are for.
    /// </summary>
    /// <remarks>
    /// The ones this application put there, not the ones a control is made
    /// of: a combo box holds a text box of its own, which belongs to its
    /// template and answers for itself.
    /// </remarks>
    private static string[] Unnamed(Window window) =>
    [
        .. window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c is TextBox or ComboBox or ListBox)
            .Where(c => c.TemplatedParent is null)
            .Where(c => string.IsNullOrEmpty(AutomationProperties.GetName(c)))
            .Select(c => c.GetType().Name)
    ];
}
