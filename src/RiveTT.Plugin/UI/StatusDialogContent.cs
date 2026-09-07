namespace RiveTT.Plugin.UI;

/// <summary>Presentation only: never changes the session's write permission.</summary>
internal sealed record StatusDialogContent(string Heading, string Body, string Footer)
{
    internal static StatusDialogContent Create(
        bool? writesAllowed, bool serviceRunning, string? documentTitle,
        string pluginVersion, string? revitVersion)
    {
        var heading = writesAllowed switch
        {
            true => "Mode : Écriture autorisée",
            false => "Mode : Lecture seule",
            null => "Mode : Indisponible"
        };
        var permission = writesAllowed switch
        {
            true => "La lecture et les modifications du modèle sont autorisées.",
            false => "La consultation du modèle est autorisée. Les modifications sont bloquées.",
            null => "Le mode actuel ne peut pas être déterminé."
        };
        // A listening service does not prove that an AI client is connected.
        var connection = serviceRunning
            ? "RiveTT est prêt à recevoir les demandes de votre assistant."
            : "RiveTT est indisponible. Votre assistant ne peut pas accéder à Revit.";
        var document = string.IsNullOrWhiteSpace(documentTitle)
            ? "Aucun document ouvert"
            : documentTitle;
        var body = $"{permission}\n\nDocument actif : {document}\n\n{connection}\n\n" +
                   "Pour changer de mode, utilisez Lecture seule ou Écriture dans " +
                   "Compléments → RiveTT.";
        var footer = $"RiveTT {pluginVersion}";
        if (!string.IsNullOrWhiteSpace(revitVersion))
            footer += $"  •  Revit {revitVersion}";
        return new StatusDialogContent(heading, body, footer);
    }
}
