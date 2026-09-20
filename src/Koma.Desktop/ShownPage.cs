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

    public void Dispose() => Image?.Dispose();
}
