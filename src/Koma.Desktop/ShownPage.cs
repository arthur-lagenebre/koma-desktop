using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Koma.Core.Model;
using Koma.Core.Packaging;

namespace Koma.Desktop;

/// <summary>
/// A page ready to draw: its pixels, or none when §16 withholds it, and the
/// faults the reader must be told about either way.
/// </summary>
internal sealed class ShownPage(ManifestItem item, Bitmap? image, IReadOnlyList<ContainerViolation> faults) : IDisposable
{
    public ManifestItem Item { get; } = item;

    public Bitmap? Image { get; } = image;

    public IReadOnlyList<ContainerViolation> Faults { get; } = faults;

    public bool IsWithheld => Image is null;

    /// <summary>
    /// What shows through transparency (§10.5), and what fills an empty half
    /// beside this page (§10.4).
    /// </summary>
    /// <remarks>
    /// Nothing checks <c>background-color</c> against the <c>#RRGGBB</c> form
    /// of §10.5 yet, so a value that does not parse falls back to the default
    /// rather than failing a page that is otherwise fine to show.
    /// </remarks>
    public IBrush Background { get; } = new ImmutableSolidColorBrush(Color.TryParse(item.BackgroundColor, out Color colour) ? colour : Colors.White);

    public void Dispose() => Image?.Dispose();
}
