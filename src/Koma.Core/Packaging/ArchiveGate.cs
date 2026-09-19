using System.Globalization;

namespace Koma.Core.Packaging;

/// <summary>
/// The check that has to happen before the archive is opened.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ArchiveInspector"/> examines an archive that is already open,
/// which is the right place for almost everything §3 and §13.1 ask for. The
/// entry count is the exception: opening the archive is itself the allocation
/// the limit exists to bound, so checking it afterwards reports a breach that
/// has already been paid for.
/// </para>
/// <para>
/// This runs first, from the end of the file alone.
/// </para>
/// </remarks>
public static class ArchiveGate
{
    /// <summary>
    /// Reads the end of the archive and reports what would forbid opening it.
    /// </summary>
    /// <param name="stream">The package, positioned anywhere; seekable.</param>
    /// <param name="limits">Defaults to the profile of §13.1.</param>
    /// <returns><see langword="null"/> when the archive may be opened.</returns>
    public static ContainerViolation? CheckBeforeOpening(Stream stream, ResourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        ResourceLimits profile = limits ?? ResourceLimits.Default;

        if (!ZipDirectory.TryRead(stream, out ZipDirectoryInfo? directory, out ZipDirectoryProblem problem))
        {
            return problem switch
            {
                ZipDirectoryProblem.Multipart => new ContainerViolation(ContainerViolationCode.MultipartArchive, null, "The archive spans more than one disk (§3)."),
                ZipDirectoryProblem.Truncated => new ContainerViolation(ContainerViolationCode.NotAZip, null, "The end of the central directory points outside the file."),
                _ => new ContainerViolation(ContainerViolationCode.NotAZip, null, "No end-of-central-directory record.")
            };
        }

        if (directory!.EntryCount > profile.MaxEntries)
            return new ContainerViolation(ContainerViolationCode.EntryCountLimit, null, string.Create(CultureInfo.InvariantCulture, $"Declares {directory.EntryCount} entries, above the {profile.MaxEntries} of §13.1."));

        return null;
    }
}
