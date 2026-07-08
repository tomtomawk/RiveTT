using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

/// <summary>
/// Opens the minimal "License &amp; Account" window. IExternalCommand (not routed through
/// Route()), so it is always available regardless of license state — the user must always
/// be able to reach Activate/Refresh.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class OpenLicense : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            new LicenseWindow().ShowDialog();
        }
        catch (System.Exception ex)
        {
            TaskDialog.Show(Localization.T("license.window_title"),
                Localization.T("license.activate_failed", ex.Message));
        }
        return Result.Succeeded;
    }
}
