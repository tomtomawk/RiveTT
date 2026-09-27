using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Hosting;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.CodeExecution;
using RiveTT.Tools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Executes custom C# code snippets in the Revit context.
/// Uses Roslyn on Revit (2026.5+ or 2027). The sandbox remains active, but script execution
/// never asks for user authorization in RiveTT.
/// RiveTTRouter records every invocation in audit.jsonl with code snippet + SHA-256.
/// Scripts are persisted to %LOCALAPPDATA%/RiveTT/scripts/ and cleaned up at Revit shutdown
/// unless marked as reusable.
///
/// transactionMode "readonly" is the tool's one read branch, declared below: it runs inside a
/// TransactionGroup that is always rolled back (see RoslynExecutor), so the router lets it
/// through the ribbon lock. The field session of 2026-09-24 could not even READ the geometry of
/// the user's selection before someone unlocked Revit.
/// </summary>
[ReadOnlyActions("auto", "readonly", Key = "transactionMode")]
[ToolSafety(false, true, supportsDryRun: true)]
public class SendCodeToRevitTool : IRiveTTTool
{
    // Must stay in lockstep with RiveTTApp.CleanupTempScripts, which deletes
    // TEMP scripts from this same folder at Revit shutdown.
    public static string ScriptsFolder => RiveTTEnvironment.Current.ScriptsFolder;

    private const string HeaderEnd = "// =============================================";

    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "LAST RESORT ONLY — execute sandboxed custom C# code in the active Revit context. Prefer dedicated tools. Defaults to dryRun=true: the preview runs the sandbox check and reports what would execute, without running it or writing the script to disk. Globals: document (Document), uiDocument (UIDocument), app (Application), scriptArgs (JObject). transactionMode readonly rolls every model change back and works while RiveTT is locked.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var requestedMode = input["transactionMode"]?.Value<string>();
        var transactionMode = RoslynExecutor.NormalizeTransactionMode(requestedMode);
        var reusable = input["reusable"]?.Value<bool>() ?? false;
        var fromScript = input["fromScript"]?.Value<string>();
        var scriptName = SanitizeName(input["scriptName"]?.Value<string>() ?? fromScript ?? "script");
        // The most powerful tool on the surface was the only destructive one with no preview
        // at all: arbitrary C# ran on the model on the first call. It now previews by default
        // like every other destructive tool.
        var dryRun = ToolHelpers.GetDryRun(input);

        if (transactionMode == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"transactionMode '{requestedMode}' is not supported.",
                suggestion: "Use auto (default: one transaction), none (alias manual: no transaction, for " +
                            "StairsEditScope), group (one undo entry, Section(...) validated one by one) or " +
                            "readonly (every change rolled back; allowed while RiveTT is locked).");

        if (input["scriptArgs"] is JToken argsToken && argsToken.Type != JTokenType.Null && argsToken is not JObject)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "scriptArgs must be a JSON object, e.g. {\"levelName\": \"R+1\", \"count\": 3}.");
        var scriptArgs = input["scriptArgs"] as JObject ?? new JObject();

        string? resolvedFrom = null;
        var editsApplied = 0;
        if (!string.IsNullOrWhiteSpace(fromScript))
        {
            if (!string.IsNullOrEmpty(code))
                return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    "Pass either code or fromScript, not both.",
                    suggestion: "fromScript re-runs the latest saved version of a script, optionally with edits; " +
                                "code runs the text you send.");

            var loaded = LoadSavedScript(ScriptsFolder, SanitizeName(fromScript!), out resolvedFrom, out var loadError);
            if (loaded == null) return loadError!;

            var edited = ApplyEdits(loaded, input["edits"], out editsApplied, out var editError);
            if (edited == null) return editError!;
            code = edited;
            // The router audits input["code"] (snippet + SHA-256) AFTER the call returns: a
            // script read from disk must be audited as the text that actually ran, not as an
            // empty code field.
            input["code"] = code;
        }
        else if (input["edits"] is JArray { Count: > 0 })
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "edits only applies with fromScript.",
                suggestion: "Name the saved script to correct with fromScript (its scriptName).");
        }

        if (string.IsNullOrEmpty(code))
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput, "code is required",
                suggestion: "Send the C# body in code, or re-run a saved script with fromScript.");

        // Sandbox validation is a technical boundary, not an authorization flow. It runs
        // before the dryRun branch so a preview reports a rejection instead of a plan.
        var sandboxResult = CodeSandbox.Validate(code!);
        if (sandboxResult != null)
        {
            return sandboxResult;
        }
        if (transactionMode == "readonly")
        {
            var readOnlyResult = CodeSandboxV2.ValidateReadOnlyScript(code!);
            if (readOnlyResult != null) return readOnlyResult;
        }

        if (dryRun)
        {
            // Compiled, not run: a syntax error or a readonly violation comes back now, not after
            // the script has been sent again for real (field report 2026-09-24: 300-line scripts
            // re-sent six times).
            var compileFailure = RoslynExecutor.CheckCompiles(code!, transactionMode);
            if (compileFailure != null) return compileFailure;

            // Nothing is written to disk here: persisting the script is itself a side effect.
            return RiveTTResult<object>.Ok(new
            {
                dryRun = true,
                message = "DryRun: the script passed the sandbox check, COMPILES, and was NOT executed. "
                        + "Review it, then set dryRun=false to run it.",
                sandbox = "passed",
                compiled = true,
                transactionMode,
                transactionModeEffect = DescribeMode(transactionMode),
                wouldRunInDocument = doc.Title,
                scriptName,
                resolvedFromScript = resolvedFrom,
                editsApplied,
                scriptArgKeys = scriptArgs.Properties().Select(p => p.Name).ToArray(),
                scriptLifetime = reusable ? "REUSABLE" : "TEMP (deleted at Revit close)",
                wouldSaveTo = Path.Combine(ScriptsFolder, $"<timestamp>_{scriptName}.cs"),
                codeLength = code!.Length,
                codeLineCount = code!.Split('\n').Length,
                warning = transactionMode == "readonly"
                    ? "readonly: every model change is rolled back after the run; calls acting outside the model were refused above."
                    : "Custom code is not bounded by a dedicated tool's guarantees: it can modify or "
                    + "delete anything in the open document, and RiveTT cannot preview its effect. "
                    + "Check that no dedicated tool covers the operation before running it."
            });
        }

        // Persist script to the local RiveTT directory.
        var scriptPath = PersistScript(code!, scriptName, reusable);

        // Build globals from session
        var uiApp = session.Store.Get<object>("uiApplication") as Autodesk.Revit.UI.UIApplication;
        var uiDoc = uiApp?.ActiveUIDocument;

        if (uiDoc == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "UIApplication not available in session");

        var globals = new ScriptGlobals
        {
            document = doc,
            uiDocument = uiDoc,
            app = uiApp!.Application,
            scriptArgs = scriptArgs
        };

        RiveTTResult<object> result;
        result = RoslynExecutor.Execute(code!, globals, transactionMode);

        // Attach script path to result so the caller knows where it was saved
        if (result.Success && result.Data is not null)
        {
            var data = Newtonsoft.Json.Linq.JObject.FromObject(result.Data);
            data["scriptSavedTo"] = scriptPath;
            data["scriptName"] = scriptName;
            data["scriptLifetime"] = reusable ? "REUSABLE" : "TEMP (deleted at Revit close)";
            if (resolvedFrom != null)
            {
                data["resolvedFromScript"] = resolvedFrom;
                data["editsApplied"] = editsApplied;
            }
            return RiveTTResult<object>.Ok(data);
        }

        if (!result.Success && result.Error != null)
        {
            // The script is saved even when it fails: that is what makes fromScript + edits
            // able to fix one line of it instead of re-sending all of it.
            var context = result.Error.Context != null
                ? new Dictionary<string, object>(result.Error.Context)
                : new Dictionary<string, object>();
            context["scriptSavedTo"] = scriptPath;
            context["scriptName"] = scriptName;
            return RiveTTResult<object>.Fail(result.Error.Code, result.Error.Message, result.Error.Suggestion, context);
        }

        return result;
    }

    private static string DescribeMode(string mode) => mode switch
    {
        "none" => "No enclosing transaction: the script opens and commits its own (StairsEditScope works here).",
        "group" => "One TransactionGroup, assimilated into a single undo entry. Each Section(...) is its own " +
                   "transaction: a failing section is rolled back alone and reported.",
        "readonly" => "A TransactionGroup that is always rolled back: nothing the script changes is kept. " +
                      "Allowed while RiveTT is locked.",
        _ => "One transaction around the whole script, committed with the failure preprocessor. A Revit error " +
             "at commit rolls back everything."
    };

    /// <summary>
    /// The latest saved version of <paramref name="name"/>, header stripped. Scripts are
    /// named "&lt;yyyyMMdd_HHmmss&gt;_&lt;name&gt;.cs", so the ordinal order of the file names
    /// is their chronological order.
    /// </summary>
    internal static string? LoadSavedScript(string folder, string name, out string? path, out RiveTTResult<object>? error)
    {
        path = null;
        error = null;
        try
        {
            var candidates = Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*_" + name + ".cs")
                    .Where(file => ScriptFileName.IsMatch(Path.GetFileName(file))
                                   && ScriptFileName.Match(Path.GetFileName(file)).Groups[1].Value == name)
                    .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
                    .ToList()
                : new List<string>();
            if (candidates.Count == 0)
            {
                var known = Directory.Exists(folder)
                    ? Directory.GetFiles(folder, "*.cs")
                        .Select(file => ScriptFileName.Match(Path.GetFileName(file)))
                        .Where(match => match.Success)
                        .Select(match => match.Groups[1].Value)
                        .Distinct()
                        .OrderBy(n => n)
                        .Take(30)
                        .ToList()
                    : new List<string>();
                error = RiveTTResult<object>.Fail(RiveTTErrorCode.ElementNotFound,
                    $"No saved script named '{name}'.",
                    suggestion: known.Count == 0
                        ? "No script is saved yet: send the code once with a scriptName, then refer to it."
                        : "Saved scripts: " + string.Join(", ", known) +
                          ". TEMP scripts are deleted when Revit closes; mark a script reusable to keep it.");
                return null;
            }

            path = candidates[0];
            return StripHeader(File.ReadAllText(path));
        }
        catch (Exception exception)
        {
            error = RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"The saved script '{name}' could not be read: {exception.Message}");
            return null;
        }
    }

    private static readonly Regex ScriptFileName = new(@"^\d{8}_\d{6}_([\w\-]+)\.cs$", RegexOptions.Compiled);

    internal static string StripHeader(string text)
    {
        var index = text.IndexOf(HeaderEnd, StringComparison.Ordinal);
        if (index < 0) return text;
        var start = text.IndexOf('\n', index);
        return start < 0 ? "" : text.Substring(start + 1);
    }

    /// <summary>
    /// Applies [{oldText, newText}] to <paramref name="code"/>. Each oldText must occur exactly
    /// once: a replacement that could land in two places is refused rather than guessed.
    /// </summary>
    internal static string? ApplyEdits(string code, JToken? editsToken, out int applied,
        out RiveTTResult<object>? error)
    {
        applied = 0;
        error = null;
        if (editsToken == null || editsToken.Type == JTokenType.Null) return code;
        if (editsToken is not JArray edits)
        {
            error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                "edits must be a JSON array: [{\"oldText\": \"...\", \"newText\": \"...\"}]");
            return null;
        }

        var current = code;
        for (var i = 0; i < edits.Count; i++)
        {
            var oldText = edits[i]["oldText"]?.Value<string>();
            var newText = edits[i]["newText"]?.Value<string>() ?? "";
            if (string.IsNullOrEmpty(oldText))
            {
                error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    $"edits[{i}].oldText is required and cannot be empty.");
                return null;
            }

            var occurrences = CountOccurrences(current, oldText!);
            if (occurrences != 1)
            {
                error = RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                    occurrences == 0
                        ? $"edits[{i}].oldText was not found in the saved script."
                        : $"edits[{i}].oldText occurs {occurrences} times in the saved script; it must be unique.",
                    suggestion: occurrences == 0
                        ? "Copy the text exactly, spaces and line breaks included; earlier edits of the same call apply first."
                        : "Include more surrounding text in oldText so it matches one place only.",
                    context: new Dictionary<string, object> { ["editIndex"] = i, ["occurrences"] = occurrences });
                return null;
            }

            var at = current.IndexOf(oldText!, StringComparison.Ordinal);
            current = current.Substring(0, at) + newText + current.Substring(at + oldText!.Length);
            applied++;
        }
        return current;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// Saves the script to the local RiveTT scripts directory with a TEMP or REUSABLE header.
    /// Returns the full path of the saved file.
    /// </summary>
    private static string PersistScript(string code, string scriptName, bool reusable)
    {
        try
        {
            Directory.CreateDirectory(ScriptsFolder);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{stamp}_{scriptName}.cs";
            var filePath = Path.Combine(ScriptsFolder, fileName);

            var lifetime = reusable ? "REUSABLE" : "TEMP";
            var header =
                $"// {lifetime}\n" +
                $"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"// Name: {scriptName}\n" +
                HeaderEnd + "\n";

            File.WriteAllText(filePath, header + code);
            return filePath;
        }
        catch
        {
            return "(could not save script)";
        }
    }

    /// <summary>Removes all TEMP scripts from %LOCALAPPDATA%\RiveTT\scripts.</summary>
    public static void CleanupTempScripts()
    {
        if (!Directory.Exists(ScriptsFolder)) return;
        foreach (var file in Directory.GetFiles(ScriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new StreamReader(file);
                var firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
            catch { }
        }
    }

    private static string SanitizeName(string name)
    {
        var safe = Regex.Replace(name, @"[^\w\-]", "-").Trim('-');
        return safe.Length == 0 ? "script" : safe.Substring(0, Math.Min(safe.Length, 40));
    }
}
