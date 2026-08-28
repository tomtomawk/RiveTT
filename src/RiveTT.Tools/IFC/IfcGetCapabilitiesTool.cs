using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.IFC;

/// <summary>
/// Reports IFC capabilities: supported versions, available actions, and
/// whether the open-source revit-ifc add-in is installed.
/// </summary>
[ToolSafety(true, false)]
public class IfcGetCapabilitiesTool : IRiveTTTool
{
    public string Name => "ifc_get_capabilities";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Get IFC capabilities: supported versions, import/export availability, revit-ifc add-in detection";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var supportedExportVersions = new List<string>();
        foreach (var v in Enum.GetValues(typeof(IFCVersion)))
        {
            if ((int)v > 0)
                supportedExportVersions.Add(v.ToString()!);
        }

        var revitIfcAddinInstalled = DetectRevitIfcAddin();

        var capabilities = new
        {
            supportedExportVersions,
            supportedImportActions = new[] { "open", "link" },
            supportedImportIntents = new[] { "reference", "parametric" },
            revitIfcAddinInstalled,
            canExport = true,
            canImport = true,
            canLink = true,
        };

        return RiveTTResult<object>.Ok(capabilities);
    }

    private static bool DetectRevitIfcAddin()
    {
        try
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Any(a => a.GetName().Name != null &&
                          a.GetName().Name!.StartsWith("IFCExporter", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
