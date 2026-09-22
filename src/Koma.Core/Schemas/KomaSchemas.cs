using System.Collections.Concurrent;
using System.Xml.Linq;
using Koma.Core.Packaging;

namespace Koma.Core.Schemas;

/// <summary>
/// The four schemas of KOMA 0.9, loaded from the assembly, and the layer-2
/// check of a core document (§15, §17).
/// </summary>
public static class KomaSchemas
{
    private static readonly Dictionary<string, (string Schema, string Code)> Documents = new(StringComparer.Ordinal)
    {
        [CorePaths.Container] = ("container", ContainerViolationCode.SchemaInvalidContainer),
        [CorePaths.Manifest] = ("manifest", ContainerViolationCode.SchemaInvalidManifest),
        [CorePaths.Metadata] = ("metadata", ContainerViolationCode.SchemaInvalidMetadata),
        [CorePaths.Navigation] = ("navigation", ContainerViolationCode.SchemaInvalidNavigation)
    };

    private static readonly ConcurrentDictionary<string, RelaxNgSchema> Loaded = new(StringComparer.Ordinal);

    /// <summary>
    /// The violation a core document commits against its schema, or
    /// <see langword="null"/> when it matches.
    /// </summary>
    /// <param name="entryName">The document's path, which says which schema applies (§1).</param>
    public static ContainerViolation? Check(XDocument document, string entryName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entryName);

        if (!Documents.TryGetValue(entryName, out (string Schema, string Code) target))
            throw new ArgumentException($"{entryName} is not a core document (§1).", nameof(entryName));

        string? fault = Loaded.GetOrAdd(target.Schema, Load).Validate(document);

        return fault is null ? null : new ContainerViolation(target.Code, entryName, $"{entryName} does not match its schema at {fault} (§17).");
    }

    private static RelaxNgSchema Load(string schema)
    {
        string resource = $"Koma.Core.Schemas.koma-{schema}-0.9.rng";
        using Stream stream = typeof(KomaSchemas).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"{resource} is not in the assembly; the submodule was not there when it was built.");

        return RelaxNgSchema.Load(XDocument.Load(stream));
    }
}
