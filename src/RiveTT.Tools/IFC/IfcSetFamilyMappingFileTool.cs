using System.IO;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.IFC;

/// <summary>
/// Sets the IFC family mapping file path in the session store.
/// Subsequent ifc_export_basic and ifc_export_with_configuration calls
/// will use this mapping file automatically.
///
/// Classified as a WRITE tool although it does not touch the model. Since the ribbon lock,
/// [ToolSafety] is a permission boundary, not a label: this call changes persistent session
/// state that silently alters the output of every later IFC export, so it belongs behind the
/// same gate as those exports. It was marked read-only and passed straight through the lock
/// — a read-only session could still redirect all subsequent exports. The name-prefix
/// heuristic agrees: ifc_set_ is not a read prefix.
/// </summary>
[ToolSafety(false, false)]
public class IfcSetFamilyMappingFileTool : IRiveTTTool
{
    public string Name => "ifc_set_family_mapping_file";
    public string Category => "IFC";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description =>
        "Set the family mapping file used by subsequent IFC exports (persists for the session; pass an "
        + "empty filePath to clear it). Counts as a write: it changes what every later IFC export produces.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var filePath = input["filePath"]?.Value<string>();
        if (filePath == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "filePath is required",
                suggestion: "Provide the full path to a .txt family mapping file, or empty string to clear");

        if (string.IsNullOrWhiteSpace(filePath))
        {
            session.Store.Set("ifc_family_mapping_file", "");
            return RiveTTResult<object>.Ok(new
            {
                action = "cleared",
                message = "Family mapping file cleared from session",
            });
        }

        // H25-wave: the stored path is later read by the IFC export tools; restrict it
        // to user-owned directories like every other caller-supplied file path.
        if (!PathSafety.TryResolveSafe(filePath, out var safePath, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                pathError,
                suggestion: "Provide a path under Documents, Desktop, Downloads, the user profile, or temp");
        filePath = safePath;

        if (!File.Exists(filePath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"File not found: {filePath}");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".txt")
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Expected .txt file, got: {ext}",
                suggestion: "The family mapping file must be a .txt file");

        session.Store.Set("ifc_family_mapping_file", filePath);

        return RiveTTResult<object>.Ok(new
        {
            action = "set",
            filePath,
            message = "Family mapping file set. Subsequent IFC exports will use this mapping.",
        });
    }
}
