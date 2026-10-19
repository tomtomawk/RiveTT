using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Project;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Loads a .rfa family, loads a family already open in memory (load_from_open), lists
/// loaded families, or duplicates a family type.
///
/// supportsDryRun is declared for the whole tool because the router reads it per tool,
/// not per action. Only load_from_open previews: Execute refuses dryRun on every other
/// action itself, so declaring it here never lets a real load run under dryRun:true.
/// </summary>
[ToolSafety(false, false, supportsDryRun: true)]
public class LoadFamilyTool : IRiveTTTool
{
    public string Name => "load_family";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Loads a .rfa family (action load), loads a family ALREADY OPEN in Revit - opened by "
        + "open_family, edited, or created and never saved - into a project without any file path (action "
        + "load_from_open, supports dryRun), lists loaded families, or duplicates a family type.";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";

        // The router no longer refuses dryRun for this tool (see the class summary), so the
        // actions that cannot preview must refuse it here, before touching the model.
        if (input["dryRun"]?.Value<bool>() == true
            && !string.Equals(action, "load_from_open", StringComparison.OrdinalIgnoreCase))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"load_family action '{action}' does not support dryRun: it cannot preview, only apply.",
                suggestion: "Re-issue the call without dryRun, or use action \"load_from_open\", the only action that previews. "
                          + "Nothing was executed.",
                context: new Dictionary<string, object> { ["supportsDryRun"] = false, ["modelChanged"] = false });

        try
        {
            return action.ToLowerInvariant() switch
            {
                "load" => LoadFamily(doc, input, session),
                "load_from_open" => LoadFromOpen(input, session),
                "list" => ListFamilies(doc, input),
                "duplicate_type" => DuplicateType(doc, input),
                _ => RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: load, load_from_open, list, duplicate_type")
            };
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"load_family could not complete: {ex.Message}",
                suggestion: "Unexpected failure, not a rejected input: the wording above is Revit own. "
                    + "Re-check the ids and the target with a read tool before retrying, and narrow the "
                    + "call if it covered many elements. The full call, its duration and this error are "
                    + "in %LOCALAPPDATA%\\RiveTT\\audit.jsonl.");
        }
    }

    private static RiveTTResult<object> LoadFamily(Document doc, JObject input, RiveTTSession session)
    {
        var familyPath = input["familyPath"]?.Value<string>();
        if (string.IsNullOrEmpty(familyPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "familyPath is required for load");

        // The overload without IFamilyLoadOptions returns false (does nothing) the
        // moment a same-named family already exists in the project — the normal
        // case for reloading a family edited outside Revit, and get_server_capabilities'
        // documented way to do it. Default to overwriting: see P1.7 in
        // PLAN_CORRECTION.md.
        var overwriteExisting = input["overwriteExisting"]?.Value<bool>() ?? true;

        if (!PathSafety.TryResolveSafe(familyPath, out var safeFamilyPath, out var pathError))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, pathError,
                suggestion: "Give an absolute .rfa path outside the Windows system folders; "
                          + "the project drive and network shares are accepted.");

        if (!File.Exists(safeFamilyPath))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"familyPath does not exist: {safeFamilyPath}");

        if (!session.RequestConfirmation("load family", 1))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Cancelled, "Operation cancelled by user");

        // Taken before the load: after it the old element may be gone. Reported as previousFamilyId /
        // familyIdChanged so a caller holding the old id learns that it went stale.
        var before = FamilyReloadSupport.FamilySnapshot.Take(doc,
            FindFamily(doc, Path.GetFileNameWithoutExtension(safeFamilyPath)));

        using var tx = new Transaction(doc, "RiveTT: Load Family");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);

        if (doc.LoadFamily(safeFamilyPath, new Utilities.OverwritingFamilyLoadOptions(overwriteExisting), out var family))
        {
            if (tx.Commit() != TransactionStatus.Committed)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            var types = family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(fs => fs != null)
                .Select(fs => new { id = ToolHelpers.GetElementIdValue(fs!.Id), name = fs.Name })
                .ToList();

            var loadedFamilyId = ToolHelpers.GetElementIdValue(family.Id);
            return RiveTTResult<object>.Ok(new
            {
                familyId = loadedFamilyId,
                previousFamilyId = before?.FamilyId,
                familyIdChanged = FamilyReloadSupport.IdChanged(before?.FamilyId, loadedFamilyId),
                wasExisting = before != null,
                verification = FamilyReloadSupport.BuildVerification(doc, family, before),
                familyName = family.Name,
                categoryName = family.FamilyCategory?.Name,
                typeCount = types.Count,
                types
            });
        }

        tx.RollBack();
        // The path exists and was readable — a false return here means Revit
        // itself refused it (corrupt file, category not loadable in this
        // document, family already identical), not a bad path.
        return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
            "Revit refused to load the family from a valid path: the file may be corrupt, its category may " +
            "not be loadable into this document, or (with overwriteExisting=false) a family of the same name " +
            "already exists.",
            suggestion: overwriteExisting
                ? "Check the .rfa opens cleanly in Revit and its category matches the target document."
                : "Retry with overwriteExisting=true to update the family already in the project.");
    }

    internal static bool SameName(string? a, string? b)
        => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static List<Document> OpenDocuments(Autodesk.Revit.ApplicationServices.Application app)
    {
        var list = new List<Document>();
        foreach (Document d in app.Documents)
            if (d.IsValidObject && !d.IsLinked) list.Add(d);
        return list;
    }

    internal static Document? MatchByTitle(IEnumerable<Document> docs, string name)
        => docs.FirstOrDefault(d => SameName(d.Title, name))
           ?? docs.FirstOrDefault(d => SameName(Path.GetFileNameWithoutExtension(d.Title), name)
                                       || SameName(d.PathName, name));

    internal static object[] DescribeDocs(IEnumerable<Document> docs)
        => docs.Select(d => (object)new { title = d.Title, path = d.PathName, isModified = d.IsModified }).ToArray();

    private static string FamilyNameOf(Document source)
    {
        string? owner = null;
        try { owner = source.OwnerFamily?.Name; } catch { /* no owner family yet */ }
        return !string.IsNullOrWhiteSpace(owner) ? owner! : Path.GetFileNameWithoutExtension(source.Title);
    }

    internal static Family? FindFamily(Document project, string name)
        => new FilteredElementCollector(project).OfClass(typeof(Family)).Cast<Family>()
            .FirstOrDefault(f => SameName(f.Name, name));

    private static RiveTTResult<object> LoadFromOpen(JObject input, RiveTTSession session)
    {
        var application = DocumentLifecycleSupport.ResolveApplication(session);
        if (application == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "No Revit application context is available yet",
                suggestion: "Open Revit (2026.5+ or 2027) and wait for its session to be published.");

        var sourceName = input["sourceDocument"]?.Value<string>();
        var targetName = input["targetDocument"]?.Value<string>();
        var overwrite = input["overwriteExisting"]?.Value<bool>() ?? true;
        var renameTo = input["renameTo"]?.Value<string>();
        var activateTypes = input["activateTypes"]?.Value<bool>() ?? true;
        var closeSource = input["closeSource"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;

        var uiApplication = DocumentLifecycleSupport.ResolveUiApplication(session);
        var active = uiApplication?.ActiveUIDocument?.Document;
        var open = OpenDocuments(application);
        var families = open.Where(d => d.IsFamilyDocument).ToList();
        var projects = open.Where(d => !d.IsFamilyDocument).ToList();

        Document? source;
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            source = MatchByTitle(families, sourceName!);
            if (source == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"No open family document matches sourceDocument '{sourceName}'.",
                    suggestion: "Pass the exact title of one of the open family documents listed in context.",
                    context: new Dictionary<string, object> { ["openFamilies"] = DescribeDocs(families), ["modelChanged"] = false });
        }
        else if (active != null && active.IsFamilyDocument) source = active;
        else if (families.Count == 1) source = families[0];
        else
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                families.Count == 0
                    ? "No family document is open: there is nothing to load."
                    : "Several family documents are open and none is active: sourceDocument is required.",
                suggestion: "Pass sourceDocument with the title of the family to load (open families are listed in context).",
                context: new Dictionary<string, object> { ["openFamilies"] = DescribeDocs(families), ["modelChanged"] = false });

        Document? target;
        if (!string.IsNullOrWhiteSpace(targetName))
        {
            target = MatchByTitle(projects, targetName!);
            if (target == null)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"No open project matches targetDocument '{targetName}'.",
                    suggestion: "The target must be a project already open in Revit (open_document first). Open projects are listed in context.",
                    context: new Dictionary<string, object> { ["openProjects"] = DescribeDocs(projects), ["modelChanged"] = false });
        }
        else if (projects.Count == 1) target = projects[0];
        else
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                projects.Count == 0
                    ? "No project is open: a family can only be loaded into an open project."
                    : "Several projects are open: targetDocument is required.",
                suggestion: "Pass targetDocument with the title of the project to load into (open projects are listed in context).",
                context: new Dictionary<string, object> { ["openProjects"] = DescribeDocs(projects), ["modelChanged"] = false });

        if (target.IsReadOnly)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"Target project '{target.Title}' is read-only.",
                suggestion: "Open the project with write access, or pick another targetDocument.");

        foreach (var doc in new[] { source, target })
            if (doc.IsModifiable)
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"transaction ouverte sur {doc.Title}, valider avant chargement",
                    suggestion: "Commit or cancel the transaction (or finish the edit mode) in that document, then retry. Nothing was loaded.",
                    context: new Dictionary<string, object> { ["document"] = doc.Title, ["modelChanged"] = false });

        var familyName = FamilyNameOf(source);
        string? categoryName = null;
        try { categoryName = source.OwnerFamily?.FamilyCategory?.Name; } catch { /* no owner family yet */ }
        var existing = FindFamily(target, familyName);
        var wasExisting = existing != null;

        if (!string.IsNullOrWhiteSpace(renameTo) && !SameName(renameTo, familyName)
            && FindFamily(target, renameTo!) != null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"renameTo '{renameTo}' already exists as a family in '{target.Title}'.",
                suggestion: "Pick a name that is not used by a family of the target project. Nothing was loaded.",
                context: new Dictionary<string, object> { ["modelChanged"] = false });

        // Identity by reference OR path/title: ReferenceEquals alone reported the active family as
        // "not active" (dryRun sourceIsActive:false), then the close hit Revit's active-document refusal.
        var sourceIsActive = FamilyReloadSupport.SameDocument(active, source);
        var closePlan = FamilyReloadSupport.DecideClose(sourceIsActive, target.PathName);
        var before = wasExisting ? FamilyReloadSupport.FamilySnapshot.Take(target, existing) : null;

        var skip = wasExisting && !overwrite;
        if (skip || dryRun)
        {
            var sourceTypeCount = 0;
            try { sourceTypeCount = source.FamilyManager.Types.Size; } catch { /* diagnostic only */ }
            return RiveTTResult<object>.Ok(new
            {
                status = dryRun ? "dry_run" : "skipped_existing",
                previewMethod = dryRun ? "declared" : null,
                message = dryRun
                    ? (skip
                        ? $"DryRun: '{familyName}' already exists in '{target.Title}' and overwriteExisting=false, so it would be skipped."
                        : $"DryRun: would load '{familyName}' from '{source.Title}' into '{target.Title}'"
                          + (wasExisting ? " (reload of an existing family)." : "."))
                    : $"'{familyName}' already exists in '{target.Title}' and overwriteExisting=false: nothing loaded.",
                wouldBe = dryRun ? (skip ? "skipped_existing" : wasExisting ? "reloaded" : "loaded") : null,
                familyId = existing != null ? ToolHelpers.GetElementIdValue(existing.Id) : (long?)null,
                familyName,
                category = categoryName,
                wasExisting,
                sourceTypeCount,
                renameTo,
                activateTypes,
                closeSource,
                sourceDocument = source.Title,
                targetDocument = target.Title,
                sourceIsActive,
                wouldReactivate = closeSource && closePlan == SourceClosePlan.ReactivateTargetThenClose ? target.Title : null,
                wouldCloseSource = closeSource && !skip && closePlan != SourceClosePlan.TargetNeverSaved,
                sourceCloseBlockedReason = closeSource && closePlan == SourceClosePlan.TargetNeverSaved
                    ? FamilyReloadSupport.TargetNeverSavedReason(source.Title, target.Title) : null,
                sourceClosed = false,
                activeDocument = active?.Title
            });
        }

        // LoadFamily runs OUTSIDE any transaction: the method opens its own.
        Family? fam;
        try
        {
            fam = source.LoadFamily(target, new OverwritingFamilyLoadOptions(overwrite));
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Revit refused to load '{familyName}' into '{target.Title}': {ex.Message}",
                suggestion: "The wording is Revit's own. Check the family category is loadable in a project and that no "
                          + "edit mode is active on either document. Verify the target with load_family(action: \"list\").",
                context: new Dictionary<string, object> { ["exception"] = ex.GetType().Name });
        }
        if (fam == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"Revit returned no family when loading '{familyName}' into '{target.Title}'.",
                suggestion: "The family category may not be loadable in this project, or the load was declined. "
                          + "Verify with load_family(action: \"list\") before retrying.");

        if (!string.IsNullOrWhiteSpace(renameTo) || activateTypes)
        {
            using var tx = new Transaction(target, "RiveTT - load_from_open");
            tx.Start();
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            try
            {
                if (!string.IsNullOrWhiteSpace(renameTo) && !SameName(fam.Name, renameTo))
                    fam.Name = renameTo!;
                if (activateTypes)
                {
                    foreach (var id in fam.GetFamilySymbolIds())
                        if (target.GetElement(id) is FamilySymbol fs && !fs.IsActive)
                            fs.Activate();
                    target.Regenerate();
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                        $"The family was loaded, but Revit rolled back the rename/activation: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "The family is in the project under its original name with inactive types. Fix the reported errors and retry.",
                        context: new Dictionary<string, object> { ["familyLoaded"] = true });
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                    $"The family was loaded, but the rename/activation failed: {ex.Message}",
                    suggestion: "The family is in the project under its original name with inactive types. Retry after checking the name.",
                    context: new Dictionary<string, object> { ["familyLoaded"] = true });
            }
        }

        var types = fam.GetFamilySymbolIds()
            .Select(id => target.GetElement(id) as FamilySymbol)
            .Where(fs => fs != null)
            .Select(fs => new { id = ToolHelpers.GetElementIdValue(fs!.Id), name = fs.Name, activated = fs.IsActive })
            .ToList();
        var familyId = ToolHelpers.GetElementIdValue(fam.Id);
        var finalName = fam.Name;
        var finalCategory = categoryName ?? fam.FamilyCategory?.Name;
        var sourceTitle = source.Title;
        var targetTitle = target.Title;
        var verification = FamilyReloadSupport.BuildVerification(target, fam, before);

        // Order: LoadFamily (done above), then re-activation of the target, then the close. Closing is
        // best effort: the family is already in the project, so a refusal here is reported with its
        // real cause, never turned into a failure of the load and never rolled back.
        var sourceClosed = false;
        var sourceClosePending = false;
        string? sourceCloseReason = null;
        var closeWarnings = new List<string>();
        var switchedDocument = false;
        if (closeSource)
        {
            try
            {
                if (closePlan == SourceClosePlan.TargetNeverSaved)
                    sourceCloseReason = FamilyReloadSupport.TargetNeverSavedReason(sourceTitle, targetTitle);
                else
                {
                    if (closePlan == SourceClosePlan.ReactivateTargetThenClose && uiApplication == null)
                        sourceCloseReason = "No UIApplication in session: the target project cannot be re-activated.";
                    else if (closePlan == SourceClosePlan.ReactivateTargetThenClose)
                    {
                        // The path Revit itself reports for the open target, verbatim: a mapped drive or UNC
                        // path re-typed by hand would make Revit open a second copy instead of returning it.
                        var openBefore = OpenDocuments(application).Count;
                        using var dialogs = new OpenDialogAutoAnswer(uiApplication);
                        uiApplication!.OpenAndActivateDocument(target.PathName);
                        switchedDocument = true;
                        closeWarnings.AddRange(dialogs.Warnings);
                        if (OpenDocuments(application).Count > openBefore)
                            closeWarnings.Add($"Revit opened an additional document while re-activating '{targetTitle}': "
                                + "the path it holds for the target may differ from the one it reopened. Check the open documents.");
                        var activeNow = uiApplication.ActiveUIDocument?.Document;
                        if (FamilyReloadSupport.SameDocument(activeNow, source))
                        {
                            // The switch only takes effect when this API call returns: close from Idling.
                            DeferredSourceClose.Schedule(uiApplication, source);
                            sourceClosePending = true;
                            sourceCloseReason = $"Revit applies the activation of '{targetTitle}' once this call returns; "
                                + $"'{sourceTitle}' is closed without saving on the next idle tick (within 30 s).";
                        }
                    }
                    if (!sourceClosePending && sourceCloseReason == null)
                    {
                        sourceClosed = source.Close(false);
                        if (!sourceClosed) sourceCloseReason = "Document.Close(false) returned false.";
                    }
                }
            }
            catch (Exception ex) { sourceCloseReason = ex.Message; }
        }

        string? activeTitle = null;
        try { activeTitle = uiApplication?.ActiveUIDocument?.Document.Title; } catch { /* report below */ }

        if (switchedDocument || sourceClosed)
        {
            // The active document changed under the session: nothing cached about the previous one may survive,
            // and execution.documentTitle of this very response must name the new active document.
            session.BumpDocumentVersion();
            session.Cache.InvalidateAll();
            if (activeTitle != null) session.Store.Set("documentTitleHint", activeTitle);
        }

        var previousFamilyId = before?.FamilyId;
        var familyIdChanged = FamilyReloadSupport.IdChanged(previousFamilyId, familyId);

        return RiveTTResult<object>.Ok(new
        {
            status = wasExisting ? "reloaded" : "loaded",
            familyId,
            previousFamilyId,
            familyIdChanged,
            familyIdNote = familyIdChanged
                ? "Revit assigned a new ElementId to the family on reload; type ids and instances are reported in verification."
                : null,
            verification,
            familyName = finalName,
            category = finalCategory,
            wasExisting,
            renamedFrom = SameName(finalName, familyName) ? null : familyName,
            typeCount = types.Count,
            types,
            sourceDocument = sourceTitle,
            targetDocument = targetTitle,
            sourceClosed,
            sourceClosePending,
            sourceCloseReason,
            closeWarnings,
            activeDocument = activeTitle ?? targetTitle
        });
    }

    private static RiveTTResult<object> ListFamilies(Document doc, JObject input)
    {
        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();

        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryFilter!);
            if (catId != ElementId.InvalidElementId)
                families = families.Where(f => f.FamilyCategory?.Id == catId);
        }

        var result = families.Select(f => new
        {
            id = ToolHelpers.GetElementIdValue(f.Id),
            name = f.Name,
            category = f.FamilyCategory?.Name,
            isEditable = f.IsEditable,
            typeCount = f.GetFamilySymbolIds().Count
        }).ToList();

        return RiveTTResult<object>.Ok(new { familyCount = result.Count, families = result });
    }

    private static RiveTTResult<object> DuplicateType(Document doc, JObject input)
    {
        var sourceTypeId = input["sourceTypeId"]?.Value<long>() ?? 0;
        var newTypeName = input["newTypeName"]?.Value<string>();

        if (sourceTypeId <= 0 || string.IsNullOrEmpty(newTypeName))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "sourceTypeId and newTypeName required");

        var sourceType = doc.GetElement(new ElementId(sourceTypeId)) as FamilySymbol;
        if (sourceType == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound, "Source family type not found");

        using var tx = new Transaction(doc, "RiveTT: Duplicate Family Type");
        tx.Start();
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        var newType = sourceType.Duplicate(newTypeName) as FamilySymbol;
        if (tx.Commit() != TransactionStatus.Committed)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return RiveTTResult<object>.Ok(new
        {
            newTypeId = newType != null ? ToolHelpers.GetElementIdValue(newType.Id) : 0,
            newTypeName = newType?.Name,
            familyName = sourceType.FamilyName
        });
    }
}
