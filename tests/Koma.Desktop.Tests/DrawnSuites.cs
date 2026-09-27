namespace Koma.Desktop.Tests;

/// <summary>
/// The suites that draw the application, run one after another.
/// </summary>
/// <remarks>
/// <para>
/// The application has state that belongs to no window: the language, which
/// <see cref="Text.Current"/> holds for the whole process. A suite that
/// switches it to French while another compares an English label is a race,
/// and a race decides differently on a loaded machine than on a quiet one —
/// which is how this first showed itself, green here and red on CI.
/// </para>
/// <para>
/// Named rather than fixed by the one test that caused it, so that whatever
/// global state is added later is covered by the same rule.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DrawnSuites
{
    public const string Name = "drawn";
}
