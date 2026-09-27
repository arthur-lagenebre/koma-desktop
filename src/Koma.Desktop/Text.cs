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
        ["Folders…"] = "Dossiers…",
        ["Watched folders"] = "Dossiers surveillés",
        ["Remove"] = "Retirer",
        ["Show private"] = "Afficher les privées",
        ["Keep off the shelf"] = "Garder hors de l'étagère",
        ["Show on the shelf"] = "Remettre sur l'étagère",
        ["{0} — private"] = "{0} — privé",
        ["Removing a folder takes its publications off the shelf and forgets where you were in them. No file is touched."] = "Retirer un dossier ôte ses publications de l'étagère et oublie où vous en étiez. Aucun fichier n'est touché.",
        ["Import CBZ…"] = "Importer des CBZ…",
        ["Open…"] = "Ouvrir…",
        ["Contents"] = "Sommaire",
        ["Edit…"] = "Modifier…",
        ["Pages…"] = "Pages…",
		["Series on the shelf"] = "Séries",
        ["On their own"] = "Œuvres seules",
        ["Resume {0}"] = "Reprendre {0}",
        ["Fit page"] = "Page entière",
        ["Fit width"] = "Pleine largeur",
        ["English"] = "Anglais",
        ["French"] = "Français",

        ["Search titles, series and files"] = "Chercher un titre, une série, un fichier",
        ["Order of the shelf"] = "Ordre de l'étagère",
        ["Pages of the publication"] = "Pages de la publication",
        ["Spread {0} of {1}."] = "Planche {0} sur {1}.",
        ["Page {0}."] = "Page {0}.",
        ["Decorative."] = "Décorative.",
        ["Not described."] = "Non décrite.",
        ["Refused: {0}"] = "Refusé : {0}",
        ["What was refused"] = "Ce qui a été refusé",
        ["Copy everything"] = "Tout copier",
        ["How a spread fits the window"] = "Ajustement d'une planche à la fenêtre",
        ["Language of the application"] = "Langue de l'application",
        ["By title"] = "Par titre",
        ["By series"] = "Par série",
        ["Recently read"] = "Lues récemment",
        ["No series"] = "Hors série",
        ["1 volume"] = "1 volume",
        ["{0} volumes"] = "{0} volumes",
        ["Reading {0}"] = "En cours : {0}",
        ["{0}, {1} volumes"] = "{0}, {1} volumes",
        ["← All series"] = "← Toutes les séries",
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
        ["Edit {0} together…"] = "Modifier les {0} ensemble…",
        ["Drop the picking"] = "Annuler la sélection",
        ["{0} publications — Edit together"] = "{0} publications — Modifier ensemble",
        ["Editing — KOMA"] = "Modification — KOMA",
        ["Editing… {0} / {1}"] = "Modification… {0} / {1}",
        ["{0} changed, {1} refused."] = "{0} modifiées, {1} refusées.",
        ["Only the fields you tick are written; the rest are left as they are in each publication."] = "Seuls les champs cochés sont écrits ; les autres restent tels quels dans chaque publication.",
        ["Number them in the order they are shown, from"] = "Les numéroter dans l'ordre affiché, à partir de",
        ["What they say about reading them"] = "Ce qu'elles disent de leur lecture",
        ["Who they are the work of"] = "De qui elles sont l'œuvre",
        ["What they are about"] = "Ce dont elles parlent",
        ["In place of what each publication has, rather than added to it"] = "À la place de ce que chaque publication porte, plutôt qu'en plus",
        ["A sentence for a reader deciding whether they can read them"] = "Une phrase pour qui se demande s'il peut les lire",
        ["Check this publication…"] = "Vérifier cette publication…",
        ["{0} — report"] = "{0} — rapport",
        ["Opens."] = "S'ouvre.",
        ["Does not open."] = "Ne s'ouvre pas.",
        ["KOMA {0}, which this build does not read."] = "KOMA {0}, que cette version ne lit pas.",
        ["Nothing to report."] = "Rien à signaler.",
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
        ["Write each package beside its archive"] = "Écrire chaque paquet à côté de son archive",
        ["Write them into another folder…"] = "Les écrire dans un autre dossier…",
        ["Write the packages into"] = "Écrire les paquets dans",
        ["A folder of archives keeps its tree: what sat in a subfolder is written into the same subfolder there."] = "Un dossier d'archives garde son arborescence : ce qui était dans un sous-dossier est écrit dans le même sous-dossier là-bas.",
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
        ["Publication"] = "Publication",
        ["People"] = "Personnes",
        ["Subjects"] = "Sujets",
        ["Publisher"] = "Éditeur",
        ["Add someone"] = "Ajouter quelqu'un",
        ["Add a subject"] = "Ajouter un sujet",
        ["Take this line out"] = "Retirer cette ligne",
        ["Name"] = "Nom",
        ["Roles, separated by spaces"] = "Rôles, séparés par des espaces",
        ["An organization"] = "Une organisation",
        ["The roles of §7.6: writer, artist, colorist, translator, editor…"] = "Les rôles du §7.6 : writer, artist, colorist, translator, editor…",
        ["Kind"] = "Type",
        ["Subject"] = "Sujet",
        ["Descriptions"] = "Descriptions",
        ["Add a description"] = "Ajouter une description",
        ["Text"] = "Texte",
        ["Imprint"] = "Collection",
        ["Place of publication"] = "Lieu d'édition",
        ["Edition"] = "Édition",
        ["Dates"] = "Dates",
        ["Add a date"] = "Ajouter une date",
        ["What happened"] = "Ce qui s'est passé",
        ["When, as 2026 or 2026-05 or 2026-05-23"] = "Quand, sous la forme 2026, 2026-05 ou 2026-05-23",
        ["Trimmed width, in millimetres"] = "Largeur rognée, en millimètres",
        ["Trimmed height, in millimetres"] = "Hauteur rognée, en millimètres",
        ["Story"] = "Histoire",
        ["Warnings and links"] = "Avertissements et liens",
        ["Rights"] = "Droits",
        ["Add a character or a place"] = "Ajouter un personnage ou un lieu",
        ["Add a warning"] = "Ajouter un avertissement",
        ["Add a rating"] = "Ajouter une classification",
        ["Whose classification"] = "Classification de qui",
        ["What it says"] = "Ce qu'elle dit",
        ["Where it applies, as two capitals"] = "Où elle s'applique, en deux majuscules",
        ["A rating belongs to whoever gives it — cero, pegi, a publisher's own — so the scheme is a name, not a choice from a list this project keeps."] = "Une classification appartient à qui la donne — cero, pegi, celle d'un éditeur — donc le schéma est un nom, pas un choix dans une liste que ce projet tiendrait.",
        ["Add a link"] = "Ajouter un lien",
        ["Part in the story"] = "Rôle dans l'histoire",
        ["What a reader is warned about"] = "Ce dont on prévient le lecteur",
        ["A word about it"] = "Une précision",
        ["What is at the other end"] = "Ce qu'il y a au bout",
        ["Address"] = "Adresse",
        ["What to call it"] = "Comment l'appeler",
        ["Copyright"] = "Copyright",
        ["Licence, by its identifier"] = "Licence, par son identifiant",
        ["Anything else worth saying"] = "Ce qu'il reste à dire",
        ["The characters, teams and places of the story, which is a different list from the people who made it."] = "Les personnages, équipes et lieux de l'histoire, qui ne sont pas les personnes qui l'ont faite.",
        ["The ratings a conversion wrote are kept as they are; only the warnings are edited here."] = "Les classifications écrites par une conversion sont conservées telles quelles ; seuls les avertissements se modifient ici.",
        ["Number in the series"] = "Numéro dans la série",
        ["Volumes in the series"] = "Nombre de volumes",
        ["How the publication is read"] = "Comment la publication se lit",
        ["How the publications are read"] = "Comment les publications se lisent",
        ["What they may do to a reader"] = "Ce qu'elles peuvent provoquer",
        ["A CBZ says nothing about reading its pages, so a conversion can only say what it is told. Left empty, the packages say nothing either, and a reader is warned of it."] = "Un CBZ ne dit rien de la façon dont ses pages se lisent : une conversion ne peut dire que ce qu'on lui donne. Laissé vide, les paquets ne diront rien non plus, et un lecteur en sera averti.",
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
        ["Number printed on the page"] = "Numéro imprimé sur la page",
        ["Number printed on its right half"] = "Numéro imprimé sur sa moitié droite",
        ["Alternative text"] = "Texte alternatif",
        ["Decorative: carries nothing to describe"] = "Décorative : rien à décrire",
        ["Move up"] = "Monter",
        ["Move down"] = "Descendre",
        ["Take this page out"] = "Retirer cette page",
        ["The pages could not all be read: {0}"] = "Les pages n'ont pas toutes pu être lues : {0}",
        ["Take {0} out of the publication? The page and its file go, and this cannot be taken back."] = "Retirer {0} de la publication ? La page et son fichier s'en vont, et cela ne se reprend pas.",
        ["Yes, take it out"] = "Oui, la retirer",

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
