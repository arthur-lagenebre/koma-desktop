namespace Koma.Desktop;

/// <summary>The languages the interface is written in.</summary>
public enum UiLanguage
{
    English,
    French
}

/// <summary>
/// What the interface says, in the language the reader chose.
/// </summary>
/// <remarks>
/// <para>
/// English is the key and the fallback: a string with no translation is
/// shown as it is written in the code, so a sentence added and not yet
/// translated reads as English rather than as a missing key.
/// </para>
/// <para>
/// A table rather than resource files: satellite assemblies would put a
/// second file beside an executable the release is meant to keep to one.
/// </para>
/// <para>
/// What Koma.Core reports stays in English. Its violations and the notes of
/// a conversion are the vocabulary of the specification, which a bug report
/// or a comparison with the reference converter is read against.
/// </para>
/// </remarks>
internal static class Text
{
    private static readonly Dictionary<string, string> French = new(StringComparer.Ordinal)
    {
        ["Library"] = "Bibliothèque",
        ["Refresh"] = "Actualiser",
        ["Add folder…"] = "Ajouter un dossier…",
        ["Import CBZ…"] = "Importer des CBZ…",
        ["Open…"] = "Ouvrir…",
        ["Contents"] = "Sommaire",
        ["Edit…"] = "Modifier…",
        ["Pages…"] = "Pages…",
        ["Resume {0}"] = "Reprendre {0}",
        ["Fit page"] = "Page entière",
        ["Fit width"] = "Pleine largeur",
        ["English"] = "Anglais",
        ["French"] = "Français",

        ["Search titles, series and files"] = "Chercher un titre, une série, un fichier",
        ["By title"] = "Par titre",
        ["By series"] = "Par série",
        ["Recently read"] = "Lues récemment",
        ["No series"] = "Hors série",
        ["Could not be opened"] = "Illisibles",
        ["Nothing on the shelf matches."] = "Rien ne correspond sur l'étagère.",
        ["1 page"] = "1 page",
        ["{0} pages"] = "{0} pages",
        ["{0} · started"] = "{0} · commencée",
        ["page {0} of {1}"] = "page {0} sur {1}",
        ["{0} of {1}"] = "{0} sur {1}",
        ["{0} publication"] = "{0} publication",
        ["{0} publications"] = "{0} publications",
        ["No folder is watched yet. Add one to fill the library."] = "Aucun dossier n'est surveillé. Ajoutez-en un pour remplir la bibliothèque.",
        ["Scanning…"] = "Balayage…",
        ["Scanning… {0} / {1}"] = "Balayage… {0} / {1}",
        ["Importing… {0} / {1}"] = "Import… {0} / {1}",
        ["Edit metadata…"] = "Modifier les métadonnées…",
        ["Edit this page…"] = "Modifier cette page…",

        ["Open a KOMA publication"] = "Ouvrir une publication KOMA",
        ["KOMA publication"] = "Publication KOMA",
        ["Comic book archive"] = "Archive de bande dessinée",
        ["Add a folder of KOMA publications"] = "Ajouter un dossier de publications KOMA",
        ["Import comic book archives"] = "Importer des archives de bande dessinée",
        ["Import every archive of a folder"] = "Importer toutes les archives d'un dossier",
        ["Choose files…"] = "Choisir des fichiers…",
        ["Choose a folder…"] = "Choisir un dossier…",
        ["Cancel"] = "Annuler",
        ["Close"] = "Fermer",
        ["Save"] = "Enregistrer",
        ["Save this page"] = "Enregistrer cette page",
        ["Keep the original ComicInfo.xml in each package"] = "Conserver le ComicInfo.xml d'origine dans chaque paquet",
        ["Number the volumes from the start of their file names"] = "Numéroter les volumes d'après le début de leur nom de fichier",
        ["A folder is searched for .cbz archives, subfolders included. Each package is written beside its archive, and an existing file is never replaced."] = "Un dossier est parcouru à la recherche d'archives .cbz, sous-dossiers compris. Chaque paquet est écrit à côté de son archive, et aucun fichier existant n'est remplacé.",
        ["ComicInfo is never normative for KOMA: it travels unchanged, for readers that still want it."] = "ComicInfo n'a jamais valeur de norme pour KOMA : il voyage inchangé, pour les lecteurs qui s'en servent encore.",
        ["A collection often numbers its files and not its metadata: 1 - Ante demonium.cbz. The number is then written as the volume's place in its series, and each conversion says it did so."] = "Une collection numérote souvent ses fichiers et non ses métadonnées : 1 - Ante demonium.cbz. Le numéro devient alors la place du volume dans sa série, et chaque conversion le signale.",
        ["Import report — KOMA"] = "Rapport d'import — KOMA",

        ["Title"] = "Titre",
        ["Language (BCP 47, such as fr or en-GB)"] = "Langue (BCP 47, par exemple fr ou en-GB)",
        ["Reading direction"] = "Sens de lecture",
        ["Left to right"] = "De gauche à droite",
        ["Right to left"] = "De droite à gauche",
        ["Series"] = "Série",
        ["Number in the series"] = "Numéro dans la série",
        ["Volumes in the series"] = "Nombre de volumes",
        ["How the publication is read"] = "Comment la publication se lit",
        ["What it may do to a reader"] = "Ce qu'elle peut provoquer",
        ["A sentence for a reader deciding whether they can read it"] = "Une phrase pour qui se demande s'il peut la lire",
        ["{0} — Edit metadata"] = "{0} — Modifier les métadonnées",

        ["{0} — Pages"] = "{0} — Pages",
        ["Role"] = "Rôle",
        ["Drawn across the whole spread"] = "Dessinée sur toute la planche",
        ["Place in the spread"] = "Place dans la planche",
        ["Wherever it falls"] = "Où elle tombe",
        ["Left of the spread"] = "À gauche de la planche",
        ["Right of the spread"] = "À droite de la planche",
        ["Alone, centred"] = "Seule, centrée",
        ["Chapter opening here, if any"] = "Chapitre commençant ici, s'il y en a un",
        ["Alternative text"] = "Texte alternatif",
        ["Decorative: carries nothing to describe"] = "Décorative : rien à décrire",

        ["Writing the publication…"] = "Écriture de la publication…"
    };

    public static UiLanguage Current { get; set; }

    /// <summary>The sentence in the current language, or as written when it has no translation.</summary>
    public static string Of(string english)
    {
        ArgumentNullException.ThrowIfNull(english);

        return Current == UiLanguage.French && French.TryGetValue(english, out string? french) ? french : english;
    }

    /// <summary>The sentence with its placeholders filled, in the current language.</summary>
    public static string Of(string english, params object?[] values) => string.Format(System.Globalization.CultureInfo.CurrentCulture, Of(english), values);
}
