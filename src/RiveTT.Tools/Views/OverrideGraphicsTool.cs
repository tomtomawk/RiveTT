using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Views;

/// <summary>
/// Sets or resets graphic overrides (color, transparency, halftone, line weight) for elements in a view.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class OverrideGraphicsTool : IRiveTTTool
{
    public string Name => "override_graphics";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Sets or resets graphic overrides (color, transparency, halftone, line weight) for elements in a view. "
        + "action=hide_category | show_category hides or shows WHOLE CATEGORIES per view (categories: OST_* code or name, e.g. "
        + "OST_Viewers for section marks, OST_Callouts, OST_Elev; viewIds or viewId, else the active view) — no elementIds needed. "
        + "A category controlled by a view template is reported as skipped, not forced.";

    /// <summary>
    /// View.SetCategoryHidden over several views and categories: the per-view "hide section
    /// markers" gesture that had no tool (field report ILO 13/10/2026). Reports each pair that
    /// could not be applied, with Revit's reason, rather than skipping it silently.
    /// </summary>
    private static RiveTTResult<object> SetCategoryVisibility(Document doc, JObject input, bool hide)
    {
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        if (categories.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "categories is required for hide_category / show_category",
                suggestion: "Pass OST_* codes or category names, e.g. [\"OST_Viewers\"] for section marks.");

        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var single = input["viewId"]?.Value<long?>() ?? 0;
        if (single > 0 && !viewIds.Contains(single)) viewIds.Add(single);
        var views = viewIds.Count == 0
            ? new List<View?> { doc.ActiveView }
            : viewIds.Select(id => doc.GetElement(new ElementId(id)) as View).ToList();

        var unresolved = categories.Where(c => CategoryResolver.ResolveToId(doc, c) == null).ToList();
        if (unresolved.Count > 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Category not found: {string.Join(", ", unresolved)}",
                suggestion: "Use the language-independent OST_* code (OST_Viewers = section marks, OST_Callouts, OST_Elev).");

        var applied = new List<object>();
        var skipped = new List<object>();
        var dryRun = ToolHelpers.GetDryRun(input);
        using var tx = new Transaction(doc, hide ? "RiveTT: Hide Categories" : "RiveTT: Show Categories");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        foreach (var view in views)
        {
            if (view == null) { skipped.Add(new { reason = "view not found" }); continue; }
            foreach (var name in categories)
            {
                var catId = CategoryResolver.ResolveToId(doc, name)!;
                try
                {
                    if (view is ViewSheet)
                    {
                        skipped.Add(new { viewId = ToolHelpers.GetElementIdValue(view.Id), view = view.Name, category = name,
                            reason = "a sheet has no category visibility" });
                        continue;
                    }
                    if (!view.CanCategoryBeHidden(catId))
                    {
                        skipped.Add(new { viewId = ToolHelpers.GetElementIdValue(view.Id), view = view.Name, category = name,
                            reason = "this category cannot be hidden in this view (view template control, or not a hideable category)" });
                        continue;
                    }
                    view.SetCategoryHidden(catId, hide);
                    applied.Add(new { viewId = ToolHelpers.GetElementIdValue(view.Id), view = view.Name, category = name,
                        hidden = view.GetCategoryHidden(catId) });
                }
                catch (Exception ex)
                {
                    skipped.Add(new { viewId = ToolHelpers.GetElementIdValue(view.Id), view = view.Name, category = name, reason = ex.Message });
                }
            }
        }

        var payload = new { action = hide ? "hide_category" : "show_category", appliedCount = applied.Count, applied, skipped };
        if (dryRun)
        {
            ChangePreview.Rollback(tx);
            return ChangePreview.Probed(
                "DryRun: the operation ran inside a transaction and was rolled back. The model is untouched; "
                + "what follows is what Revit produced.", payload);
        }
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");
        return RiveTTResult<object>.Ok(payload);
    }
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var requestedAction = (input["action"]?.Value<string>() ?? "set").ToLowerInvariant();
        if (requestedAction is "hide_category" or "show_category")
            return SetCategoryVisibility(doc, input, hide: requestedAction == "hide_category");

        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        if (elementIds.Count == 0)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "elementIds array is required");

        var action = input["action"]?.Value<string>() ?? "set";
        var viewIdLong = input["viewId"]?.Value<long>() ?? 0;

        try
        {
            View? view;
            if (viewIdLong > 0)
            {
                view = doc.GetElement(new ElementId(viewIdLong)) as View;
            }
            else view = doc.ActiveView;

            if (view == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "Could not resolve view");

            if (view is ViewSheet)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"View '{view.Name}' is a Drawing Sheet — graphic overrides are not supported on sheets. " +
                    "Activate a floor plan, section, elevation, or 3D view first, then retry.",
                    suggestion: "Use the activate_view tool to switch to a graphical view before applying overrides.");

            if (!session.RequestConfirmation("modify graphics for", elementIds.Count))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

            var dryRun = ToolHelpers.GetDryRun(input);
            using var tx = new Transaction(doc, "RiveTT: Override Graphics");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

            int modified = 0;
            if (action == "reset")
            {
                var emptyOverrides = new OverrideGraphicSettings();
                foreach (var eid in elementIds)
                {
                    view.SetElementOverrides(new ElementId(eid), emptyOverrides);
                    modified++;
                }
            }
            else
            {
                var overrides = new OverrideGraphicSettings();

                var r = input["colorR"]?.Value<int?>();
                var g = input["colorG"]?.Value<int?>();
                var b = input["colorB"]?.Value<int?>();
                if (r.HasValue && g.HasValue && b.HasValue)
                {
                    var color = new Color((byte)r.Value, (byte)g.Value, (byte)b.Value);
                    overrides.SetProjectionLineColor(color);
                    overrides.SetSurfaceForegroundPatternColor(color);

                    var solidFill = new FilteredElementCollector(doc)
                        .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                        .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                    if (solidFill != null)
                        overrides.SetSurfaceForegroundPatternId(solidFill.Id);
                }

                var transparency = input["transparency"]?.Value<int?>();
                if (transparency.HasValue)
                    overrides.SetSurfaceTransparency(transparency.Value);

                var isHalftone = input["isHalftone"]?.Value<bool?>();
                if (isHalftone.HasValue)
                    overrides.SetHalftone(isHalftone.Value);

                var lineWeight = input["projectionLineWeight"]?.Value<int?>();
                if (lineWeight.HasValue)
                    overrides.SetProjectionLineWeight(lineWeight.Value);

                foreach (var eid in elementIds)
                {
                    view.SetElementOverrides(new ElementId(eid), overrides);
                    modified++;
                }
            }

            // Built BEFORE the rollback: afterwards the elements this describes no longer
            // exist and reading a name off one throws. Captured verbatim from the real
            // return, so the preview cannot drift from what applying actually reports.
            var previewPayload = new { action, modifiedCount = modified, viewName = view.Name };

            if (dryRun)
            {
                ChangePreview.Rollback(tx);
                return ChangePreview.Probed(
                    "DryRun: the operation ran inside a transaction and was rolled back. The model is "
                    + "untouched; what follows is what Revit produced.",
                    previewPayload);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

return RiveTTResult<object>.Ok(previewPayload);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"override_graphics could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
