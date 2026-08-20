using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

[ToolSafety(false, false)]
public sealed class SaveDocumentTool : ICortexTool
{
    public string Name => "save_document";
    public string Category => "Documents";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Saves the active Revit project at its current path.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var document = session.Store.Get<object>("activeDocument") as Document;
        if (document == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");
        if (string.IsNullOrWhiteSpace(document.PathName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "This project has no path yet", suggestion: "Use save_as_document with an absolute RVT path.");
        try
        {
            document.Save();
            return CortexResult<object>.Ok(new { path = document.PathName, title = document.Title });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not save document: {exception.Message}");
        }
    }
}

[ToolSafety(false, false)]
public sealed class SaveAsDocumentTool : ICortexTool
{
    public string Name => "save_as_document";
    public string Category => "Documents";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Saves the active Revit project to an absolute RVT path.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var document = session.Store.Get<object>("activeDocument") as Document;
        var targetPath = input["targetPath"]?.Value<string>();
        var overwrite = input["overwrite"]?.Value<bool>() ?? false;
        if (document == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");
        if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathFullyQualified(targetPath) ||
            !targetPath.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "targetPath must be an absolute RVT path");
        if (File.Exists(targetPath) && !overwrite)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Target already exists: {targetPath}", suggestion: "Set overwrite=true to replace it.");
        try
        {
            document.SaveAs(targetPath, new SaveAsOptions { OverwriteExistingFile = overwrite });
            return CortexResult<object>.Ok(new { path = targetPath, title = document.Title });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Could not save project as: {exception.Message}");
        }
    }
}
