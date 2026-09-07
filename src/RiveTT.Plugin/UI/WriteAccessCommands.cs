using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RiveTT.Core.Hosting;

namespace RiveTT.Plugin.UI;

/// <summary>
/// Keeps the panel usable with no document open. Revit disables an external
/// command by default when zero documents are loaded, which would have hidden
/// the write lock exactly when the session starts.
/// </summary>
public sealed class AlwaysAvailable : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) => true;
}

[Transaction(TransactionMode.Manual)]
public sealed class LockWritesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        RibbonWriteAccess.Apply(writesAllowed: false);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class AllowWritesCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        RibbonWriteAccess.Apply(writesAllowed: true);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class ShowStatusCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var app = RiveTTApp.Instance;
        var policy = app?.Session?.WriteAccess;
        var auditPath = RiveTTEnvironment.Current.AuditLogPath;
        var document = commandData?.Application?.ActiveUIDocument?.Document;

        var version = typeof(ShowStatusCommand).Assembly.GetName().Version?.ToString(3) ?? "inconnue";
        var content = StatusDialogContent.Create(
            policy?.WritesAllowed, app?.IsServiceRunning == true, document?.Title,
            version, commandData?.Application?.Application?.VersionNumber);

        var dialog = new TaskDialog("RiveTT")
        {
            TitleAutoPrefix = false,
            MainInstruction = content.Heading,
            MainContent = content.Body,
            FooterText = content.Footer,
            CommonButtons = TaskDialogCommonButtons.Close,
            DefaultButton = TaskDialogResult.Close
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
            "Afficher le journal d'activité", "Retrouver l'historique des demandes de votre assistant.");

        if (dialog.Show() == TaskDialogResult.CommandLink1)
            RevealInExplorer(auditPath);

        return Result.Succeeded;
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            // The log is created on the first tool call, so it may not exist yet:
            // fall back to its folder rather than failing on a missing file.
            var target = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            TaskDialog.Show("RiveTT", $"Ouverture impossible : {exception.Message}");
        }
    }
}

internal static class RibbonWriteAccess
{
    /// <summary>
    /// Applies the toggle. Clicking the mode that is already current is a no-op,
    /// so re-clicking never produces a dialog or an audit entry.
    /// </summary>
    public static void Apply(bool writesAllowed)
    {
        var policy = RiveTTApp.Instance?.Session?.WriteAccess;
        if (policy == null)
        {
            TaskDialog.Show("RiveTT",
                "Le connecteur n'est pas démarré : le mode ne peut pas être changé.");
            return;
        }

        if (!policy.Set(writesAllowed, "ribbon")) return;

        System.Diagnostics.Trace.WriteLine(
            $"[RiveTT] Write access set to {writesAllowed} from the ribbon.");
    }
}
