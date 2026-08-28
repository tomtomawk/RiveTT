using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Creates a new material in the project with optional color, class, and transparency.
/// </summary>
[ToolSafety(false, false)]
public class CreateMaterialTool : IRiveTTTool
{
    public string Name => "create_material";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a new material in the project with name, class, color, transparency, and optional structural/thermal asset setup.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var name = input["name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(name))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "name is required",
                suggestion: "Provide a material name, e.g. {\"name\": \"Custom Concrete\"}");

        var materialClass    = input["materialClass"]?.Value<string>();
        var materialCategory = input["materialCategory"]?.Value<string>();
        var colorHex         = input["color"]?.Value<string>();
        var transparency     = input["transparency"]?.Value<int>();
        var shininess        = input["shininess"]?.Value<int>();
        var smoothness       = input["smoothness"]?.Value<int>();

        try
        {
            ElementId newMatId;

            using (var tx = new Transaction(doc, "RiveTT: Create Material"))
            {
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                newMatId = Material.Create(doc, name);
                var mat = doc.GetElement(newMatId) as Material;
                if (mat == null)
                {
                    tx.RollBack();
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, "Material.Create returned invalid element");
                }

                if (!string.IsNullOrEmpty(materialClass))
                    mat.MaterialClass = materialClass;

                if (!string.IsNullOrEmpty(materialCategory))
                    mat.MaterialCategory = materialCategory;

                if (!string.IsNullOrEmpty(colorHex))
                {
                    var c = ParseColor(colorHex!);
                    if (c != null) mat.Color = c;
                }

                if (transparency.HasValue)
                    mat.Transparency = Math.Max(0, Math.Min(100, transparency.Value));

                if (shininess.HasValue)
                    mat.Shininess = Math.Max(0, Math.Min(128, shininess.Value));

                if (smoothness.HasValue)
                    mat.Smoothness = Math.Max(0, Math.Min(100, smoothness.Value));

                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }

            long idValue;
            idValue = newMatId.Value;

            return RiveTTResult<object>.Ok(new
            {
                materialId = idValue,
                name,
                materialClass = materialClass ?? "",
                message = $"Material '{name}' created successfully"
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown, $"Failed to create material: {ex.Message}");
        }
    }

    private static Color? ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return null;
        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new Color(r, g, b);
        }
        catch { return null; }
    }
}
