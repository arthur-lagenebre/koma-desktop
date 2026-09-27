using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Koma.Desktop;

/// <summary>
/// A list a reader adds to and takes from: contributors, subjects.
/// </summary>
/// <remarks>
/// A row at a time, with a button that takes one away and a button that adds
/// one: an editor that could only write what was already there could not
/// correct a conversion, and one that could not empty a list could not undo
/// it.
/// </remarks>
internal sealed class Rows : StackPanel
{
    private readonly Func<StackPanel> row;
    private readonly StackPanel lines = new() { Spacing = 4 };

    public Rows(string add, Func<StackPanel> row)
    {
        this.row = row;

        Spacing = 8;

        var button = new Button { Content = add, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => Add();

        Children.Add(lines);
        Children.Add(button);
    }

    /// <summary>The rows as they stand, in the order they are shown.</summary>
    public IEnumerable<StackPanel> Lines => lines.Children.OfType<StackPanel>().Select(line => (StackPanel)line.Children[0]);

    /// <summary>Starts again from these rows, one line each.</summary>
    public void Fill(int count)
    {
        lines.Children.Clear();

        for (int i = 0; i < count; i++)
            Add();
    }

    private void Add()
    {
        StackPanel fields = row();
        var take = new Button { Content = "✕", Width = 32 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { fields, take } };

        AutomationProperties.SetName(take, Text.Of("Take this line out"));
        take.Click += (_, _) => lines.Children.Remove(line);

        lines.Children.Add(line);
    }
}
