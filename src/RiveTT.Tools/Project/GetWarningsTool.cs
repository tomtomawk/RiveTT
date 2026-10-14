using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Retrieves all warnings/errors in the model with optional severity and
/// description filtering. Useful for model health auditing.
/// </summary>
[ToolSafety(true, false)]
public class GetWarningsTool : IRiveTTTool
{
    public string Name => "list_warnings";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Retrieves all warnings/errors in the model with optional severity and description filtering. Useful for model health auditing. "
        + "To check that edits added none: read once with snapshot=true (unfiltered), edit, then read with sinceSnapshot=true to get only the warnings that were not there before.";

    private const string SnapshotKey = "warningsSnapshot";

    private sealed class WarningsSnapshot
    {
        public WarningsSnapshot(Document document, HashSet<string> fingerprints)
        {
            Document = document;
            Fingerprints = fingerprints;
        }

        public Document Document { get; }
        public HashSet<string> Fingerprints { get; }
        public bool Contains(string fingerprint) => Fingerprints.Contains(fingerprint);
    }
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No active document in session");

        var severityFilter = input["severityFilter"]?.Value<string>() ?? "All";
        var maxWarnings    = input["maxWarnings"]?.Value<int>() ?? 500;
        var categoryFilter = input["categoryFilter"]?.Value<string>() ?? "";

        try
        {
            var allWarnings = doc.GetWarnings();
            var warnings = new List<object>();

            // snapshot / sinceSnapshot: the model carried 53 identical warnings before and after a
            // session of edits and there was no way to tell that none was added (field report ILO
            // 13/10/2026). snapshot=true remembers this read; sinceSnapshot=true returns only the
            // warnings absent from it. The baseline lives in the session, so it is dropped when the
            // active document changes.
            var snapshot = input["snapshot"]?.Value<bool>() ?? false;
            var sinceSnapshot = input["sinceSnapshot"]?.Value<bool>() ?? false;
            var baseline = session.Store.Get<WarningsSnapshot>(SnapshotKey);
            if (baseline != null && !ReferenceEquals(baseline.Document, doc)) baseline = null;
            if (sinceSnapshot && baseline == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "No warnings snapshot exists for this document.",
                    suggestion: "Call list_warnings with snapshot=true first (before the edits), then again with sinceSnapshot=true.");
            var currentFingerprints = new HashSet<string>();
            var alreadyKnown = 0;
            var severityCounts = new Dictionary<string, int>
            {
                { "Error", 0 },
                { "Warning", 0 }
            };

            int count = 0;
            foreach (var warning in allWarnings)
            {
                // A snapshot must see EVERY warning, so it keeps reading past the cap; the cap then
                // only limits what is returned.
                var capReached = count >= maxWarnings;
                if (capReached && !snapshot) break;

                var severity = warning.GetSeverity().ToString();

                if (severityFilter != "All" && severity != severityFilter)
                    continue;

                var description = warning.GetDescriptionText();

                if (!string.IsNullOrEmpty(categoryFilter) &&
                    description.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var failingIds = warning.GetFailingElements().Select(id =>
                {
                    return id.Value;
                }).ToList();

                var additionalIds = warning.GetAdditionalElements().Select(id =>
                {
                    return id.Value;
                }).ToList();

                // Identity of a warning across two reads: what it says and which elements it names.
                var fingerprint = $"{severity}|{description}|{string.Join(",", failingIds.OrderBy(v => v))}";
                currentFingerprints.Add(fingerprint);
                if (sinceSnapshot && baseline != null && baseline.Contains(fingerprint))
                {
                    alreadyKnown++;
                    continue;
                }
                if (capReached) continue;

                if (severityCounts.ContainsKey(severity))
                    severityCounts[severity]++;

                warnings.Add(new
                {
                    severity,
                    description,
                    failingElementIds = failingIds,
                    additionalElementIds = additionalIds
                });

                count++;
            }

            // The snapshot records the WHOLE current set, so it is only taken from an unfiltered
            // read — a filtered one would later report the rest as "new".
            var snapshotTaken = false;
            if (snapshot && severityFilter == "All" && string.IsNullOrEmpty(categoryFilter))
            {
                session.Store.Set(SnapshotKey, new WarningsSnapshot(doc, currentFingerprints));
                snapshotTaken = true;
            }

            return RiveTTResult<object>.Ok(new
            {
                totalWarnings    = allWarnings.Count,
                returnedWarnings = warnings.Count,
                severityCounts,
                sinceSnapshot,
                alreadyKnownInSnapshot = sinceSnapshot ? alreadyKnown : (int?)null,
                snapshotTaken,
                snapshotNote = snapshot && !snapshotTaken
                    ? "No snapshot taken: it needs an unfiltered read (severityFilter All, no categoryFilter)."
                    : null,
                warnings
            });
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"list_warnings could not get warnings: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }
}
