using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// The shared dryRun preview for write tools whose effect is a Revit transaction.
///
/// A preview written by hand is a second implementation of the tool, and it drifts:
/// it guesses what Revit will accept, and it is the guess — not the operation — that
/// the caller ends up trusting. So these tools do not guess. They run the REAL
/// operation inside the transaction and then roll it back instead of committing.
/// What comes back is what Revit actually did, including its own objections, and the
/// model is untouched. <see cref="DeletionPreview"/> already worked this way for the
/// delete tools; this is the same idea for the rest.
///
/// The one thing a rolled-back probe cannot promise is ELEMENT IDS: the ids Revit
/// hands out during the probe are released by the rollback, and the real call will
/// allocate different ones. Previews therefore report counts, names and warnings, and
/// say so rather than publishing ids a caller would try to use.
/// </summary>
public static class ChangePreview
{
    public const string IdNote =
        "Preview from a rolled-back probe: the operation really ran and was undone, so counts, "
        + "names and warnings are Revit's own. Element ids are NOT included — the ids allocated "
        + "during the probe are released by the rollback and the real call allocates different ones.";

    /// <summary>
    /// Wraps a payload built inside a probe transaction that the caller has rolled back.
    /// Adds the fields every preview carries, so an agent can recognise one without
    /// knowing which tool produced it.
    /// </summary>
    /// <param name="message">What WOULD happen, in the caller's terms.</param>
    /// <param name="payload">Tool-specific detail: counts, names, resolved targets.</param>
    public static RiveTTResult<object> Probed(string message, object payload)
    {
        var obj = JObject.FromObject(payload);
        obj["dryRun"] = true;
        obj["mutated"] = false;
        obj["message"] = message;
        obj["previewMethod"] = "probe-and-rollback";
        obj["note"] = IdNote;
        return RiveTTResult<object>.Ok(obj);
    }

    /// <summary>
    /// Wraps a preview that could NOT be probed by transaction — the effect is on a file
    /// or another document, and no rollback would undo it (opening an IFC, reloading a
    /// link). What is reported is the resolved intent and the checkable preconditions,
    /// and the payload says which kind of preview this is: a caller must not read a
    /// declared preview as a proven one.
    /// </summary>
    /// <param name="blockers">Preconditions that already fail. Empty means "nothing known
    /// to stop it" — never "it will succeed".</param>
    public static RiveTTResult<object> Declared(
        string message, object payload, IEnumerable<string>? blockers = null)
    {
        var list = blockers?.ToList() ?? new List<string>();
        var obj = JObject.FromObject(payload);
        obj["dryRun"] = true;
        obj["mutated"] = false;
        obj["message"] = message;
        obj["previewMethod"] = "declared";
        obj["blockers"] = new JArray(list);
        obj["note"] =
            "This effect is on a file or another document, so it cannot be probed and rolled back. "
            + "The preview reports the resolved target and the preconditions that can be checked "
            + "from here; an empty blockers list means nothing known stands in the way, not that "
            + "the operation will succeed.";
        return RiveTTResult<object>.Ok(obj);
    }

    /// <summary>
    /// Rolls a probe transaction back, whatever state it is in. Called on the preview path
    /// in place of Commit: leaving a transaction open would strand the document in an edit
    /// state and the next tool call would fail with an unrelated message.
    /// </summary>
    public static void Rollback(Transaction probe)
    {
        if (probe.GetStatus() == TransactionStatus.Started)
            probe.RollBack();
    }
}
