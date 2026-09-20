namespace Koma.Core.Packaging;

/// <summary>
/// Where the core documents live (§1).
/// </summary>
/// <remarks>
/// §1 fixes these paths, and the attributes of §6 and §8 that name them must
/// carry exactly these values. They are therefore checked against this class
/// and never followed: an attribute that could take one value only is a
/// statement about the package, not a pointer into it.
/// </remarks>
public static class CorePaths
{
    public const string Container = "META-INF/container.xml";
    public const string Manifest = "koma/manifest.xml";
    public const string Metadata = "koma/metadata.xml";
    public const string Navigation = "koma/nav.xml";
}
