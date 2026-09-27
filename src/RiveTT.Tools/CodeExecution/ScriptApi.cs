using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// Helpers every send_code_to_revit script can call without a prefix
/// (<c>using static RiveTT.Tools.CodeExecution.ScriptApi;</c> is part of the wrapper).
///
/// Why they exist: the scripts of the 2026-09-24 field session re-implemented the same
/// half-dozen functions each time (mm conversion, level and type lookup, host-wall search,
/// family placement) and re-made the same mistakes in them — types looked up by an exact
/// name that did not match, level-based furniture placed at twice the level elevation,
/// hosted openings placed at Z = 0. Getting those right once, here, is cheaper than
/// re-deriving them in every 300-line script.
///
/// Lengths passed IN are millimetres; Revit values (XYZ, Elevation) are feet, as always.
/// Every helper that has to choose (a type found by prefix, an elevation corrected) says
/// so through <see cref="Log"/>, and the log comes back in the response.
/// </summary>
public static class ScriptApi
{
    [ThreadStatic] private static ScriptRunContext? _current;

    internal static ScriptRunContext? Current => _current;

    internal static ScriptRunContext Begin(Document document, string transactionMode, int prefixLines)
    {
        _current = new ScriptRunContext(document, transactionMode, prefixLines);
        return _current;
    }

    internal static void End() => _current = null;

    private static ScriptRunContext Context => _current
        ?? throw new InvalidOperationException("ScriptApi helpers are only available inside send_code_to_revit.");

    private static Document Doc => Context.Document;

    // ── units ────────────────────────────────────────────────────────────────

    /// <summary>Millimetres to Revit internal feet.</summary>
    public static double Mm(double millimetres) => millimetres / MmPerFoot;

    /// <summary>Revit internal feet to millimetres.</summary>
    public static double ToMm(double feet) => feet * MmPerFoot;

    /// <summary>A point given in millimetres, returned in Revit feet.</summary>
    public static XYZ Pt(double xMm, double yMm, double zMm = 0) => new XYZ(Mm(xMm), Mm(yMm), Mm(zMm));

    // ── journal ──────────────────────────────────────────────────────────────

    /// <summary>Adds a line to the script log returned in scriptRun.log (500 lines max).</summary>
    public static void Log(string message) => Context.AddLog(message);

    // ── lookups ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The level with this name: exact first, then ignoring case. Throws with the list of
    /// the document's levels when neither matches.
    /// </summary>
    public static Level LevelByName(string name)
    {
        var levels = new FilteredElementCollector(Doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        var level = levels.FirstOrDefault(l => l.Name == name)
            ?? levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (level != null) return level;
        throw new ArgumentException(
            $"No level named '{name}'. Levels: {string.Join(", ", levels.OrderBy(l => l.Elevation).Select(l => $"'{l.Name}'"))}");
    }

    /// <summary>
    /// An element type of class <typeparamref name="T"/> (WallType, FloorType, RoofType,
    /// FamilySymbol...) found by name: exact, then ignoring case, then by a UNIQUE prefix.
    /// Anything but an exact match is logged with the name actually retained; an ambiguous
    /// prefix or no match throws with the candidates.
    /// </summary>
    public static T TypeByName<T>(string name) where T : ElementType
    {
        var types = new FilteredElementCollector(Doc).OfClass(typeof(T)).Cast<T>().ToList();
        var exact = types.FirstOrDefault(t => t.Name == name);
        if (exact != null) return exact;

        var caseless = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (caseless != null)
        {
            Log($"TypeByName<{typeof(T).Name}>('{name}'): retained '{caseless.Name}' (case differs).");
            return caseless;
        }

        var prefixed = types.Where(t => t.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (prefixed.Count == 1)
        {
            Log($"TypeByName<{typeof(T).Name}>('{name}'): no exact match, retained '{prefixed[0].Name}' by prefix.");
            return prefixed[0];
        }

        var candidates = (prefixed.Count > 1 ? prefixed : types).Select(t => $"'{t.Name}'").Take(25);
        throw new ArgumentException(prefixed.Count > 1
            ? $"{typeof(T).Name} '{name}' is ambiguous: {prefixed.Count} names start with it: {string.Join(", ", candidates)}"
            : $"No {typeof(T).Name} named '{name}'. Available: {string.Join(", ", candidates)}");
    }

    /// <summary>
    /// A loadable family type by family and type name (exact, then ignoring case). Throws
    /// with the family's types, or with the similar family names, when nothing matches.
    /// </summary>
    public static FamilySymbol SymbolByName(string familyName, string typeName)
    {
        var symbols = new FilteredElementCollector(Doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
        var inFamily = symbols.Where(s => s.FamilyName == familyName).ToList();
        if (inFamily.Count == 0)
            inFamily = symbols.Where(s => string.Equals(s.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (inFamily.Count == 0)
        {
            var near = symbols.Select(s => s.FamilyName).Distinct()
                .Where(f => f.IndexOf(familyName, StringComparison.OrdinalIgnoreCase) >= 0
                         || familyName.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(15).Select(f => $"'{f}'");
            throw new ArgumentException($"No loaded family named '{familyName}'. Similar: {string.Join(", ", near)}");
        }

        var symbol = inFamily.FirstOrDefault(s => s.Name == typeName)
            ?? inFamily.FirstOrDefault(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
        if (symbol != null) return symbol;
        throw new ArgumentException(
            $"Family '{familyName}' has no type '{typeName}'. Types: {string.Join(", ", inFamily.Select(s => $"'{s.Name}'"))}");
    }

    /// <summary>
    /// The wall whose location line passes within half its width (plus
    /// <paramref name="toleranceMm"/>) of <paramref name="point"/> in plan, and whose height
    /// spans the point's Z. With <paramref name="level"/>, only walls whose base level is
    /// that level. The nearest one wins; null when no wall qualifies.
    /// </summary>
    public static Wall? FindHostWall(XYZ point, Level? level = null, double toleranceMm = 20)
        => HostWallFinder.Find(Doc, point, level, toleranceMm, out _);

    // ── placement ────────────────────────────────────────────────────────────

    /// <summary>
    /// Places a level-based family (furniture, sanitary, bikes...) at <paramref name="point"/>
    /// in plan, <paramref name="offsetMm"/> above <paramref name="level"/>. The Z of
    /// <paramref name="point"/> is ignored.
    ///
    /// Then MEASURES where Revit put it and moves it by the difference: on Revit 2026 the
    /// level overload of NewFamilyInstance added the level elevation to an absolute Z, and
    /// the whole furniture of an upper floor floated one storey up (+2.89 m at R+1). The
    /// correction does not depend on which convention Revit applies; it is logged.
    /// </summary>
    public static FamilyInstance PlaceOnLevel(FamilySymbol symbol, XYZ point, Level level,
        double rotationDeg = 0, double offsetMm = 0)
    {
        var doc = Doc;
        ActivateSymbol(symbol);
        var targetZ = level.Elevation + Mm(offsetMm);
        var instance = doc.Create.NewFamilyInstance(new XYZ(point.X, point.Y, targetZ), symbol, level,
            StructuralType.NonStructural);
        doc.Regenerate();

        var correction = ElevationCorrection.Apply(doc, instance, targetZ);
        if (correction != 0)
            Log($"PlaceOnLevel('{symbol.FamilyName} : {symbol.Name}'): Revit placed it " +
                $"{ToMm(-correction):F0} mm away from {level.Name}+{offsetMm:F0} mm; moved back into place.");

        if (Math.Abs(rotationDeg) > 1e-9)
        {
            var anchor = new XYZ(point.X, point.Y, targetZ);
            ElementTransformUtils.RotateElement(doc, instance.Id,
                Line.CreateBound(anchor, anchor + XYZ.BasisZ), rotationDeg * Math.PI / 180.0);
        }
        return instance;
    }

    /// <summary>
    /// Places a wall-hosted family (door, window, ETEL...) in <paramref name="host"/> at
    /// <paramref name="point"/> in plan, <paramref name="sillMm"/> above
    /// <paramref name="level"/>. The Z sent to Revit is the ABSOLUTE elevation: at Z = 0 on an
    /// upper floor every opening failed with "cannot cut instance out of wall" (x77 in the
    /// 2026-09-24 session, none of which named the elevation).
    /// </summary>
    public static FamilyInstance PlaceInWall(FamilySymbol symbol, XYZ point, Wall host, Level level,
        double sillMm = 0)
    {
        var doc = Doc;
        ActivateSymbol(symbol);
        var absolute = new XYZ(point.X, point.Y, level.Elevation + Mm(sillMm));
        var instance = doc.Create.NewFamilyInstance(absolute, symbol, host, level, StructuralType.NonStructural);
        doc.Regenerate();
        return instance;
    }

    // ── best effort ──────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="body"/> as an isolated section and reports it in
    /// scriptRun.sections. Returns true when the section was kept.
    ///
    /// With no transaction open (transactionMode "group", "none" or "readonly") the section is
    /// its own Transaction, committed with the failure preprocessor: a Revit error at commit
    /// rolls back THIS section only, with its messages and element ids. Inside the single
    /// transaction of mode "auto" it is a SubTransaction: an exception is isolated, but a Revit
    /// failure raised at the final commit still rolls back the whole script — prefer "group"
    /// for real section-by-section validation.
    /// </summary>
    public static bool Section(string name, Action body)
    {
        var context = Context;
        var doc = context.Document;
        var record = new ScriptSectionRecord(name);
        var watch = Stopwatch.StartNew();
        context.Sections.Add(record);

        if (doc.IsModifiable)
        {
            using var sub = new SubTransaction(doc);
            sub.Start();
            record.Isolation = "subTransaction";
            try
            {
                body();
                if (sub.GetStatus() == TransactionStatus.Started) sub.Commit();
                record.Status = "committed";
            }
            catch (Exception exception)
            {
                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                record.Fail(exception, context.PrefixLines);
            }
        }
        else
        {
            using var tx = new Transaction(doc, "RiveTT: " + (name.Length > 60 ? name.Substring(0, 60) : name));
            record.Isolation = "transaction";
            try
            {
                tx.Start();
                var failures = TransactionFailureHandling.SuppressWarnings(tx);
                body();
                if (tx.GetStatus() == TransactionStatus.Started
                    && tx.Commit() != TransactionStatus.Committed)
                {
                    record.Status = "rolledBack";
                    record.Failures = TransactionFailureHandling.ToContext(failures, null);
                    record.Error = TransactionFailureHandling.Describe(failures);
                }
                else
                {
                    record.Status = "committed";
                    record.WarningsSuppressed = failures.WarningsSuppressed;
                }
            }
            catch (Exception exception)
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                record.Fail(exception, context.PrefixLines);
            }
        }

        record.DurationMs = watch.ElapsedMilliseconds;
        return record.Status == "committed";
    }

    // ── internals ────────────────────────────────────────────────────────────

    private static void ActivateSymbol(FamilySymbol symbol)
    {
        if (symbol.IsActive) return;
        symbol.Activate();
        Doc.Regenerate();
    }

}

/// <summary>State of one script run, read back by RoslynExecutor for the response.</summary>
internal sealed class ScriptRunContext
{
    private const int MaxLogLines = 500;
    private const int MaxLogLineLength = 500;

    public ScriptRunContext(Document document, string transactionMode, int prefixLines)
    {
        Document = document;
        TransactionMode = transactionMode;
        PrefixLines = prefixLines;
    }

    public Document Document { get; }
    public string TransactionMode { get; }
    public int PrefixLines { get; }
    public List<string> LogLines { get; } = new List<string>();
    public int LogLinesDropped { get; private set; }
    public List<ScriptSectionRecord> Sections { get; } = new List<ScriptSectionRecord>();

    public void AddLog(string message)
    {
        if (LogLines.Count >= MaxLogLines)
        {
            LogLinesDropped++;
            return;
        }
        message ??= "";
        LogLines.Add(message.Length > MaxLogLineLength ? message.Substring(0, MaxLogLineLength) + "..." : message);
    }
}

/// <summary>One Section(...) of a script, as reported in scriptRun.sections.</summary>
internal sealed class ScriptSectionRecord
{
    public ScriptSectionRecord(string name) => Name = name;

    public string Name { get; }
    public string Status { get; set; } = "running";
    public string Isolation { get; set; } = "";
    public string? Error { get; set; }
    public int? Line { get; set; }
    public long DurationMs { get; set; }
    public int WarningsSuppressed { get; set; }
    public Dictionary<string, object>? Failures { get; set; }

    public void Fail(Exception exception, int prefixLines)
    {
        Status = "failed";
        Error = $"{exception.GetType().Name}: {exception.Message}";
        Line = ScriptLineNumbers.FromException(exception, prefixLines);
    }

    public object ToReport() => new
    {
        name = Name,
        status = Status,
        isolation = Isolation,
        error = Error,
        line = Line,
        durationMs = DurationMs,
        warningsSuppressed = WarningsSuppressed,
        failures = Failures
    };
}

/// <summary>Maps a runtime stack frame of the compiled script back to the caller's line.</summary>
internal static class ScriptLineNumbers
{
    private static readonly Regex FrameLine = new(
        Regex.Escape(RoslynCompilerWorker.ScriptFileName) + @":line (\d+)", RegexOptions.Compiled);

    /// <summary>
    /// The caller's line of the innermost script frame of <paramref name="exception"/> (or of
    /// its inner exceptions), or null when the stack carries no script line.
    /// </summary>
    public static int? FromException(Exception? exception, int prefixLines)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var line = FromStackTrace(current.StackTrace, prefixLines);
            if (line != null) return line;
        }
        return null;
    }

    public static int? FromStackTrace(string? stackTrace, int prefixLines)
    {
        if (string.IsNullOrEmpty(stackTrace)) return null;
        var match = FrameLine.Match(stackTrace);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var wrappedLine)) return null;
        var line = wrappedLine - prefixLines;
        return line >= 1 ? line : null;
    }
}
