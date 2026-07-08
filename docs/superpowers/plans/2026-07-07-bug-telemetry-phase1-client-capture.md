# Bug Telemetry Phase 0+1 — Client Capture Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the client-side telemetry foundation of `docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md` — consent-gated, fail-closed error/bottleneck capture with local queue and sender, route-wide exception hardening, consent UI, and product-doc alignment. No production endpoint is enabled by this plan.

**Architecture:** New `RevitCortex.Core/Telemetry/` namespace (netstandard2.0, no Revit references): sanitizer → classifier → fingerprinter → event → disk queue → HTTP sender, orchestrated by an `ErrorReporter` facade. The Plugin wires the reporter into `CortexRouter` (single hook next to the existing `AuditLogger.LogWithPerf`), adds route-wide exception capture, a first-run consent dialog, and a Settings toggle.

**Tech Stack:** C# (netstandard2.0 Core, net48+net8 Plugin), Newtonsoft.Json 13.0.3, xUnit. Build gates: `Debug R25` AND `Debug R24`.

**Plan sequence (spec phases → plans):** This is Plan 1 of 5. Plan 2 = ingest Worker (Cloudflare, vitest). Plan 3 = support report hardening + UI notifier prompts/toasts + Server `_bridge` failure capture + report-level consent copy. Plan 4 = rctriage automation. Plan 5 = paid release gate. Known-issue toasts and repeated-failure prompts are raised as events in this plan but get their UI in Plan 3 (they are useless before the Worker of Plan 2 exists).

**Cross-target rules (repeat of CLAUDE.md, they bite here):** no `record`, no `init`, no `GetValueOrDefault`, no `^`/`..`. Core is netstandard2.0: also no Revit types, no WPF. After each task build BOTH `Debug R25` and `Debug R24`.

---

### Task 1: Worktree + branch from main

The spec mandates a dedicated branch cut from `main` in an isolated worktree (a parallel workstream shares this clone; concurrent sessions share the git index; the current checkout is dirty with dynamo work).

**Files:** none (git only)

- [ ] **Step 1: Create the worktree**

```powershell
git -C "C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex" worktree add "C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex-telemetry-dev" -b feature/bug-telemetry main
```

Expected: `Preparing worktree (new branch 'feature/bug-telemetry')`.

- [ ] **Step 2: Bring over the two specs + this plan**

The specs/plan were committed on `feature/dynamo-integration`, not `main`:

```powershell
git -C "C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex-telemetry-dev" checkout feature/dynamo-integration -- docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-design.md docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md docs/superpowers/plans/2026-07-07-bug-telemetry-phase1-client-capture.md
```

- [ ] **Step 3: Commit**

```powershell
git -C "C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex-telemetry-dev" add docs/superpowers && git -C "C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex-telemetry-dev" commit -m "docs(telemetry): carry telemetry specs + phase1 plan onto feature/bug-telemetry"
```

**All subsequent tasks run inside `C:\Users\luigi.dattilo\Desktop\ClaudeCode\RevitCortex-telemetry-dev`.**

---

### Task 1B: CortexEnvironment (prod/dev profile)

Dev builds must coexist with the production install on the same machine (the developer uses RevitCortex in production daily). One central profile object decides every environment-dependent value; detection is zero-config via the addin folder name.

**Files:**
- Create: `src/RevitCortex.Core/Hosting/CortexEnvironment.cs`
- Test: `src/RevitCortex.Tests/Hosting/CortexEnvironmentTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Hosting;

public class CortexEnvironmentTests
{
    [Fact]
    public void Detect_AddinFolderContainsRevitCortexDev_IsDevProfile()
    {
        var env = CortexEnvironment.Detect(
            @"C:\Users\x\AppData\Roaming\Autodesk\Revit\Addins\2025\RevitCortexDev\RevitCortex.Plugin.dll");
        Assert.True(env.IsDev);
        Assert.Equal("dev", env.ProfileName);
        Assert.EndsWith(".revitcortex-dev", env.RootFolder);
        Assert.Equal(8081, env.DefaultPort);
        Assert.Equal("http://127.0.0.1:8787", env.DefaultTelemetryEndpoint);
    }

    [Fact]
    public void Detect_ProductionFolder_IsProdProfile()
    {
        var env = CortexEnvironment.Detect(
            @"C:\ProgramData\Autodesk\Revit\Addins\2025\RevitCortex\RevitCortex.Plugin.dll");
        Assert.False(env.IsDev);
        Assert.EndsWith(".revitcortex", env.RootFolder);
        Assert.Equal(8080, env.DefaultPort);
        Assert.Equal("https://ingest.revitcortex.dev", env.DefaultTelemetryEndpoint);
    }

    [Fact]
    public void Detect_NullOrGarbage_FallsBackToProd()
    {
        Assert.False(CortexEnvironment.Detect(null).IsDev);
        Assert.False(CortexEnvironment.Detect("???").IsDev);
    }

    [Fact]
    public void Paths_DeriveFromRootFolder()
    {
        var env = CortexEnvironment.Detect(@"C:\x\RevitCortexDev\p.dll");
        Assert.EndsWith(@".revitcortex-dev\settings.json", env.SettingsFilePath);
        Assert.EndsWith(@".revitcortex-dev\audit.jsonl", env.AuditLogPath);
        Assert.EndsWith(@".revitcortex-dev\telemetry-queue.jsonl", env.TelemetryQueuePath);
        Assert.EndsWith(@".revitcortex-dev\support-reports", env.SupportReportsFolder);
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (filter `CortexEnvironmentTests`)

- [ ] **Step 3: Implement `CortexEnvironment.cs`**

```csharp
using System;
using System.IO;

namespace RevitCortex.Core.Hosting;

/// <summary>
/// Central prod/dev profile: every environment-dependent value (folders,
/// default port, telemetry endpoint) comes from here. The dev profile is
/// detected from the addin folder name (deploy-dev.ps1 installs into
/// "RevitCortexDev\"), so prod and dev plugins coexist on the same machine
/// without sharing settings, audit, queue, reports, or port.
/// </summary>
public class CortexEnvironment
{
    public string ProfileName { get; }
    public bool IsDev { get; }
    public string RootFolder { get; }
    public int DefaultPort { get; }
    public string DefaultTelemetryEndpoint { get; }

    public string SettingsFilePath => Path.Combine(RootFolder, "settings.json");
    public string AuditLogPath => Path.Combine(RootFolder, "audit.jsonl");
    public string TelemetryQueuePath => Path.Combine(RootFolder, "telemetry-queue.jsonl");
    public string SupportReportsFolder => Path.Combine(RootFolder, "support-reports");

    private CortexEnvironment(string profileName, bool isDev, string rootFolder,
        int defaultPort, string defaultTelemetryEndpoint)
    {
        ProfileName = profileName;
        IsDev = isDev;
        RootFolder = rootFolder;
        DefaultPort = defaultPort;
        DefaultTelemetryEndpoint = defaultTelemetryEndpoint;
    }

    private static CortexEnvironment? _current;

    /// <summary>Process-wide profile, detected from the executing assembly's folder.</summary>
    public static CortexEnvironment Current
    {
        get
        {
            var c = _current;
            if (c == null)
            {
                string? location = null;
                try { location = typeof(CortexEnvironment).Assembly.Location; } catch { }
                c = Detect(location);
                _current = c;
            }
            return c;
        }
    }

    /// <summary>Test seam: force a profile (pass null to re-detect).</summary>
    public static void OverrideForTests(CortexEnvironment? env) { _current = env; }

    public static CortexEnvironment Detect(string? assemblyLocation)
    {
        bool dev = false;
        try
        {
            var dir = Path.GetDirectoryName(assemblyLocation ?? "") ?? "";
            dev = dir.IndexOf("RevitCortexDev", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { }
        return dev ? Dev() : Prod();
    }

    public static CortexEnvironment Prod() => new CortexEnvironment(
        "prod", false, HomePath(".revitcortex"), 8080, "https://ingest.revitcortex.dev");

    public static CortexEnvironment Dev() => new CortexEnvironment(
        "dev", true, HomePath(".revitcortex-dev"), 8081, "http://127.0.0.1:8787");

    private static string HomePath(string folder) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder);
}
```

- [ ] **Step 4: Run tests PASS. Step 5: Build R25 + R24, commit** — `feat(hosting): CortexEnvironment prod/dev profile`

---

### Task 1C: Migrate coexistence-critical call sites + deploy-dev.ps1

Only the call sites that break prod/dev coexistence migrate now (dynamo/PBI paths stay as-is — harmless and out of scope).

**Files:**
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs` (settings/port read, single shared AuditLogger wired to router AND ToolExecutionHandler, ribbon panel name, temp-script cleanup)
- Modify: `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml.cs` (`SettingsFilePath`, `DefaultPort`, DTO `Port` default)
- Modify: `src/RevitCortex.Plugin/UI/ToolsSettingsPage.xaml.cs` (`SettingsFilePath` — its Save writes `DisabledTools`, must never hit the prod file)
- Modify: `src/RevitCortex.Plugin/Commands/SendSupportReport.cs` (`ReportsFolder`, settings read, report-zip source folder)
- Modify: `src/RevitCortex.Core/Hosting/CortexEnvironment.cs` (add `ScriptsFolder` derived path)
- Modify: `src/RevitCortex.Core/Security/CortexSettings.cs` (`DefaultPath` → profile settings path; `Port` default → profile port — gates `EnableCodeExecution` for send_code_to_revit)
- Modify: `src/RevitCortex.Tools/Elements/SendCodeToRevitTool.cs` (`ScriptsFolder` → profile scripts path, in lockstep with `CleanupTempScripts`)
- Test: `src/RevitCortex.Tests/Hosting/CortexEnvironmentTests.cs` (`ScriptsFolder` assertion in `Paths_DeriveFromRootFolder`)
- Create: `deploy-dev.ps1` (repo root)

- [ ] **Step 1: Migrate the Plugin call sites.** Grep the four files for hardcoded `.revitcortex` / `"settings.json"` / `new AuditLogger()` defaults and route them through `CortexEnvironment.Current`:

  - `RevitCortexApp.OnStartup`: wherever it reads `settings.json` for the port, use `CortexEnvironment.Current.SettingsFilePath` and fall back to `CortexEnvironment.Current.DefaultPort` (not literal 8080). Construct the router with an explicit audit logger: `new CortexRouter(_session, analyzer, auditLogger: new AuditLogger(CortexEnvironment.Current.AuditLogPath))`.
  - Ribbon creation: when `CortexEnvironment.Current.IsDev`, suffix the ribbon tab/panel title with `" Dev"` — two addins registering the same tab name collide.
  - `GeneralSettingsPage.SettingsFilePath`: return `CortexEnvironment.Current.SettingsFilePath`.
  - `SendSupportReport.ReportsFolder`: return `CortexEnvironment.Current.SupportReportsFolder`; its `ReadKeepCount` settings path likewise.

  Verification: `grep -n "\.revitcortex" src/RevitCortex.Plugin/RevitCortexApp.cs src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml.cs src/RevitCortex.Plugin/Commands/SendSupportReport.cs` returns **zero** hardcoded prod paths in those files.

- [ ] **Step 2: Create `deploy-dev.ps1`** — read `deploy.ps1` and mirror its build+copy shape with these differences (do not modify `deploy.ps1`):

  - Target folder: `%APPDATA%\Autodesk\Revit\Addins\<year>\RevitCortexDev\` (user-scope, no elevation; ProgramData is admin-locked on this machine).
  - Manifest: write `RevitCortexDev.addin` next to the folder with Name `RevitCortex Dev`, the SAME `FullClassName` as prod, and this fixed dedicated AddInId (two manifests with the same GUID conflict): `d3f8a2c4-9b1e-4e5f-8a7c-2f6d0b9e4a11`.
  - Default configuration `Debug R25`, param `-Year` (default 2025).
  - It must never write into any folder named `RevitCortex\` (prod).

- [ ] **Step 3: Build R25 + R24, run full test suite, commit** — `feat(hosting): dev profile call-site migration + deploy-dev.ps1`

- [ ] **Step 4: Manual check (once, with Revit open):** run `deploy-dev.ps1`, start Revit → two ribbon tabs ("RevitCortex" prod + "RevitCortex Dev"), dev writes `~/.revitcortex-dev/`, prod files untouched, dev Cortex Switch binds 8081.

---

### Task 2: TelemetryEvent + KnownIssueMatch models

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/TelemetryEvent.cs`
- Create: `src/RevitCortex.Core/Telemetry/KnownIssueMatch.cs`
- Test: `src/RevitCortex.Tests/Telemetry/TelemetryEventTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetryEventTests
{
    [Fact]
    public void Serializes_WithCamelCaseWireNames_AndOmitsNullSanitizedMessage()
    {
        var evt = new TelemetryEvent
        {
            EventId = "e1", InstallationId = "i1", Kind = "error",
            Fingerprint = "a3f9c2e1b0d47f68", Tool = "create_dimensions",
            ErrorCode = "InvalidInput", FailureStage = "tool",
            MessageClass = "parameter_missing", MessageOrigin = "exception",
            SanitizedMessage = null, PluginVersion = "1.0.40",
            RevitVersion = "2025", Target = "R25", OsMajor = "Windows 10.0",
            Locale = "it", DurationMs = 12, ResponseBytes = 34,
            Timestamp = "2026-07-07T10:30:00Z"
        };

        var json = JObject.Parse(JsonConvert.SerializeObject(evt));

        Assert.Equal(1, (int)json["schemaVersion"]!);
        Assert.Equal("e1", (string)json["eventId"]!);
        Assert.Equal("a3f9c2e1b0d47f68", (string)json["fingerprint"]!);
        Assert.Equal("2026-07-07T10:30:00Z", (string)json["ts"]!);
        Assert.Null(json["sanitizedMessage"]);
        Assert.Equal("exception", (string)json["messageOrigin"]!);
    }

    [Fact]
    public void KnownIssueMatch_RoundTrips()
    {
        var json = "{\"fingerprint\":\"abc\",\"issueId\":\"RC-014\",\"status\":\"fixed\",\"fixVersion\":\"1.0.42\",\"publicTitle\":\"t\"}";
        var m = JsonConvert.DeserializeObject<KnownIssueMatch>(json)!;
        Assert.Equal("RC-014", m.IssueId);
        Assert.Equal("1.0.42", m.FixVersion);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
dotnet test src\RevitCortex.Tests\RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~TelemetryEventTests"
```

Expected: build FAIL — `TelemetryEvent` does not exist.

- [ ] **Step 3: Implement `TelemetryEvent.cs`**

```csharp
using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// One automatic telemetry occurrence (error or bottleneck). Wire schema v1 —
/// see docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md.
/// MUST NOT ever carry: tool inputs, raw exception text, document titles/paths,
/// usernames, machine names, parameter/family/type names, element ids.
/// </summary>
public class TelemetryEvent
{
    [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonProperty("eventId")] public string EventId { get; set; } = "";
    [JsonProperty("installationId")] public string InstallationId { get; set; } = "";
    [JsonProperty("kind")] public string Kind { get; set; } = "error";
    [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonProperty("tool")] public string Tool { get; set; } = "";
    [JsonProperty("errorCode", NullValueHandling = NullValueHandling.Ignore)]
    public string? ErrorCode { get; set; }
    [JsonProperty("failureStage")] public string FailureStage { get; set; } = "tool";
    [JsonProperty("messageClass")] public string MessageClass { get; set; } = "unknown";
    [JsonProperty("messageOrigin")] public string MessageOrigin { get; set; } = "exception";
    [JsonProperty("sanitizedMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string? SanitizedMessage { get; set; }
    [JsonProperty("pluginVersion")] public string PluginVersion { get; set; } = "";
    [JsonProperty("revitVersion")] public string RevitVersion { get; set; } = "";
    [JsonProperty("target")] public string Target { get; set; } = "";
    [JsonProperty("osMajor")] public string OsMajor { get; set; } = "";
    [JsonProperty("locale")] public string Locale { get; set; } = "";
    [JsonProperty("durationMs")] public long DurationMs { get; set; }
    [JsonProperty("responseBytes")] public long ResponseBytes { get; set; }
    [JsonProperty("ts")] public string Timestamp { get; set; } = "";
}
```

- [ ] **Step 4: Implement `KnownIssueMatch.cs`**

```csharp
using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>Exact known-issue match returned by the ingest Worker for a submitted fingerprint.</summary>
public class KnownIssueMatch
{
    [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonProperty("issueId")] public string IssueId { get; set; } = "";
    [JsonProperty("status")] public string Status { get; set; } = "";
    [JsonProperty("fixVersion", NullValueHandling = NullValueHandling.Ignore)]
    public string? FixVersion { get; set; }
    [JsonProperty("publicTitle", NullValueHandling = NullValueHandling.Ignore)]
    public string? PublicTitle { get; set; }
}
```

- [ ] **Step 5: Run test to verify it passes**

Same command as Step 2. Expected: 2 passed.

- [ ] **Step 6: Build both targets, commit**

```powershell
dotnet build -c "Debug R25" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj; dotnet build -c "Debug R24" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
git add src/RevitCortex.Core/Telemetry src/RevitCortex.Tests/Telemetry && git commit -m "feat(telemetry): TelemetryEvent + KnownIssueMatch wire models"
```

---

### Task 3: MessageSanitizer (fail-closed)

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/MessageSanitizer.cs`
- Test: `src/RevitCortex.Tests/Telemetry/MessageSanitizerTests.cs`

- [ ] **Step 1: Write the failing tests (adversarial cases from the spec's acceptance criteria)**

```csharp
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class MessageSanitizerTests
{
    [Theory]
    [InlineData("Element 12345 does not exist in the active document",
                "Element 99 does not exist in the active document")]
    [InlineData("Failed on C:\\Projects\\TorreA\\model.rvt line 12",
                "Failed on D:\\Other\\Secret\\z.rvt line 99")]
    [InlineData("Guid 0d534e54-53c8-4f7e-a418-11ab5b58a475 invalid",
                "Guid ffffffff-aaaa-bbbb-cccc-000011112222 invalid")]
    public void Normalize_CollapsesVariants_ToSameString(string a, string b)
    {
        Assert.Equal(MessageSanitizer.Normalize(a), MessageSanitizer.Normalize(b));
    }

    [Fact]
    public void Normalize_StripsQuotedStrings_PathsGuidsNumbersEmails()
    {
        var raw = "Param 'WBS_Codice' on \"Torre A - Modello Centrale\" at C:\\Users\\mario.rossi\\file.rvt (id 606873, mario.rossi@gpapartners.com)";
        var n = MessageSanitizer.Normalize(raw);
        Assert.DoesNotContain("WBS_Codice", n);
        Assert.DoesNotContain("Torre A", n);
        Assert.DoesNotContain("mario.rossi", n);
        Assert.DoesNotContain("606873", n);
        Assert.DoesNotContain(":\\", n);
    }

    [Fact]
    public void Normalize_StripsUnquotedCompoundAndIfcTokens()
    {
        var n = MessageSanitizer.Normalize("Parameter WBS_Code missing; IfcWallStandardCase rejected");
        Assert.DoesNotContain("WBS_Code", n);
        Assert.DoesNotContain("IfcWallStandardCase", n);
    }

    [Fact]
    public void TrySanitize_TemplatedSafeMessage_ReturnsTrueWithText()
    {
        var ok = MessageSanitizer.TrySanitizeForTransmission(
            "Element 12345 does not exist in the active document", out var s);
        Assert.True(ok);
        Assert.Contains("does not exist in the active document", s);
        Assert.DoesNotContain("12345", s);
    }

    [Theory]
    [InlineData("Cannot open \\\\server\\share\\proj.rvt")]
    [InlineData("User luigi.dattilo@gpapartners.com not authorized")]
    public void TrySanitize_ResidualSuspiciousContent_FailsClosed(string raw)
    {
        // These are crafted so a residue survives stripping (regex gaps are
        // expected in the wild — the verdict must fail closed, not leak).
        var mutated = raw.Replace("\\\\", "\\ \\").Replace("@", " @ ");
        var ok = MessageSanitizer.TrySanitizeForTransmission(mutated, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TrySanitize_EmptyOrNull_FailsClosed()
    {
        Assert.False(MessageSanitizer.TrySanitizeForTransmission(null, out _));
        Assert.False(MessageSanitizer.TrySanitizeForTransmission("   ", out _));
    }

    [Fact]
    public void TrySanitize_CapsAt200Chars()
    {
        var ok = MessageSanitizer.TrySanitizeForTransmission(new string('a', 500), out var s);
        Assert.True(ok);
        Assert.True(s.Length <= 200);
    }
}
```

- [ ] **Step 2: Run to verify FAIL** (same test command pattern, filter `MessageSanitizerTests`)

- [ ] **Step 3: Implement `MessageSanitizer.cs`**

```csharp
using System.Text.RegularExpressions;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Strips potentially identifying content from failure messages.
/// Normalize() feeds the fingerprint (aggressive, lossy, stable).
/// TrySanitizeForTransmission() additionally applies a fail-closed verdict:
/// if any suspicious residue survives, NO text leaves the machine.
/// </summary>
public static class MessageSanitizer
{
    private const string Placeholder = "_";

    // Order matters: longest/most-specific first. Revit UniqueId = GUID + 8 hex.
    private static readonly Regex RxRevitUniqueId = new Regex(
        @"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}-[0-9a-fA-F]{8}\b", RegexOptions.Compiled);
    private static readonly Regex RxGuid = new Regex(
        @"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex RxUncPath = new Regex(@"\\\\[^\s""']+", RegexOptions.Compiled);
    private static readonly Regex RxDrivePath = new Regex(@"[A-Za-z]:\\[^\s""']*", RegexOptions.Compiled);
    private static readonly Regex RxEmail = new Regex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
    private static readonly Regex RxQuoted = new Regex("\"[^\"]*\"|'[^']*'|«[^»]*»", RegexOptions.Compiled);
    private static readonly Regex RxIfcToken = new Regex(@"\bIfc[A-Z][A-Za-z0-9]*\b", RegexOptions.Compiled);
    private static readonly Regex RxCompoundToken = new Regex(@"\b[A-Za-z]+_[A-Za-z0-9_]*\b", RegexOptions.Compiled);
    private static readonly Regex RxNumber = new Regex(@"\d+([.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex RxWhitespace = new Regex(@"\s+", RegexOptions.Compiled);

    // Anything matching this AFTER stripping means the sanitizer could not
    // prove the text safe -> fail closed (send messageClass only).
    private static readonly Regex RxResidue = new Regex(
        "[\"'«»@]|[A-Za-z]:\\\\|\\\\", RegexOptions.Compiled);

    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var s = message!;
        s = RxRevitUniqueId.Replace(s, Placeholder);
        s = RxGuid.Replace(s, Placeholder);
        s = RxUncPath.Replace(s, Placeholder);
        s = RxDrivePath.Replace(s, Placeholder);
        s = RxEmail.Replace(s, Placeholder);
        s = RxQuoted.Replace(s, Placeholder);
        s = RxIfcToken.Replace(s, Placeholder);
        s = RxCompoundToken.Replace(s, Placeholder);
        s = RxNumber.Replace(s, Placeholder);
        s = RxWhitespace.Replace(s, " ").Trim();
        return s.ToLowerInvariant();
    }

    public static bool TrySanitizeForTransmission(string? message, out string sanitized)
    {
        sanitized = "";
        var n = Normalize(message);
        if (n.Length == 0) return false;
        if (RxResidue.IsMatch(n)) return false;
        sanitized = n.Length <= 200 ? n : n.Substring(0, 200);
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify PASS.** If a residue test fails, tighten `RxResidue` — never loosen a stripping regex to make a test green.

- [ ] **Step 5: Build R25 + R24, commit** — `feat(telemetry): fail-closed MessageSanitizer`

---

### Task 4: MessageClassifier

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/MessageClassifier.cs`
- Test: `src/RevitCortex.Tests/Telemetry/MessageClassifierTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class MessageClassifierTests
{
    [Theory]
    [InlineData("Timeout", "anything", "timeout")]
    [InlineData("Cancelled", "anything", "cancelled")]
    [InlineData("TransactionFailed", "commit failed", "transaction_failed")]
    [InlineData("PermissionDenied", "blocked in read-only mode", "read_only_block")]
    [InlineData("PermissionDenied", "code execution disabled", "permission_denied")]
    [InlineData("Unknown", "Unhandled exception: NullReferenceException", "exception")]
    [InlineData("InvalidInput", "Parameter 'X' not found on element", "parameter_missing")]
    [InlineData("InvalidInput", "Unknown category OST_Fake", "invalid_category")]
    [InlineData("InvalidInput", "failed to parse JSON body", "parse_error")]
    [InlineData("ElementNotFound", "socket closed by bridge", "connection_failed")]
    [InlineData("InvalidInput", "something else entirely", "unknown")]
    public void Classify_MapsKnownShapes(string code, string message, string expected)
    {
        Assert.Equal(expected, MessageClassifier.Classify(code, message));
    }

    [Fact]
    public void Classify_NullInputs_ReturnsUnknown()
    {
        Assert.Equal("unknown", MessageClassifier.Classify(null, null));
    }
}
```

- [ ] **Step 2: Run to verify FAIL**

- [ ] **Step 3: Implement**

```csharp
namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Maps a failure to a coarse class so telemetry stays useful without raw
/// message text. May inspect the raw local message; the raw text is then
/// discarded (never transmitted from here).
/// </summary>
public static class MessageClassifier
{
    public static string Classify(string? errorCode, string? message)
    {
        var m = (message ?? "").ToLowerInvariant();

        if (m.Contains("unhandled exception")) return "exception";

        switch (errorCode)
        {
            case "Timeout": return "timeout";
            case "Cancelled": return "cancelled";
            case "TransactionFailed": return "transaction_failed";
            case "PermissionDenied":
                return m.Contains("read-only") ? "read_only_block" : "permission_denied";
            case "Unknown": return "exception";
        }

        if (m.Contains("parameter") &&
            (m.Contains("not found") || m.Contains("missing") || m.Contains("does not exist")))
            return "parameter_missing";
        if (m.Contains("category")) return "invalid_category";
        if (m.Contains("parse") || m.Contains("json") || m.Contains("deserial")) return "parse_error";
        if (m.Contains("socket") || m.Contains("connect") || m.Contains("bridge")) return "connection_failed";

        return "unknown";
    }
}
```

- [ ] **Step 4: Run tests PASS. Step 5: Build R25+R24, commit** — `feat(telemetry): MessageClassifier`

---

### Task 5: ErrorFingerprinter

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/ErrorFingerprinter.cs`
- Test: `src/RevitCortex.Tests/Telemetry/ErrorFingerprinterTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class ErrorFingerprinterTests
{
    [Fact]
    public void SameBug_DifferentElementIds_SameFingerprint()
    {
        var a = Fp("Element 12345 does not exist");
        var b = Fp("Element 99 does not exist");
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentTool_DifferentFingerprint()
    {
        var a = ErrorFingerprinter.Compute("tool_a", "InvalidInput", "tool", "unknown",
            MessageSanitizer.Normalize("x"));
        var b = ErrorFingerprinter.Compute("tool_b", "InvalidInput", "tool", "unknown",
            MessageSanitizer.Normalize("x"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fingerprint_Is16LowercaseHexChars()
    {
        var f = Fp("anything");
        Assert.Matches("^[0-9a-f]{16}$", f);
    }

    private static string Fp(string message) =>
        ErrorFingerprinter.Compute("create_dimensions", "InvalidInput", "tool",
            "parameter_missing", MessageSanitizer.Normalize(message));
}
```

- [ ] **Step 2: FAIL. Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace RevitCortex.Core.Telemetry;

/// <summary>Stable bug identity: SHA256(tool|errorCode|stage|class|normalizedMessage), first 16 hex chars.</summary>
public static class ErrorFingerprinter
{
    public static string Compute(string tool, string? errorCode, string failureStage,
        string messageClass, string normalizedMessage)
    {
        var input = tool + "|" + (errorCode ?? "") + "|" + failureStage + "|"
            + messageClass + "|" + normalizedMessage;
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: PASS. Step 5: Build R25+R24, commit** — `feat(telemetry): ErrorFingerprinter`

---

### Task 6: TelemetryConfig (consent gate + merge-write settings)

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/TelemetryConfig.cs`
- Test: `src/RevitCortex.Tests/Telemetry/TelemetryConfigTests.cs`

Settings keys (spec): `EnableTelemetry` (default **false**), `TelemetryConsentAnswered`, `TelemetryConsentVersion`, `TelemetryEndpoint`, `InstallationId`, `BottleneckDurationMs` (10000), `BottleneckResponseBytes` (512000), `ZipPromptFailureThreshold` (3). Merge-write only — never drop unknown keys (the v1.0.36 installer bug was exactly a blind rewrite).

- [ ] **Step 1: Failing tests**

```csharp
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetryConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(),
        "rc-tests-" + Guid.NewGuid().ToString("N"), "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_path)!, true); } catch { }
    }

    [Fact]
    public void Defaults_TelemetryDisabled_ConsentNotAnswered()
    {
        var c = TelemetryConfig.Load(_path);
        Assert.False(c.EnableTelemetry);
        Assert.False(c.ConsentAnswered);
        Assert.True(c.NeedsConsentPrompt);
        Assert.False(c.EffectiveEnabled);
        Assert.Equal(10000, c.BottleneckDurationMs);
        Assert.Equal(512000, c.BottleneckResponseBytes);
        Assert.Equal(3, c.ZipPromptFailureThreshold);
        Assert.Equal("https://ingest.revitcortex.dev", c.Endpoint);
    }

    [Fact]
    public void MarkConsent_True_EnablesAndStampsVersion_PreservingOtherKeys()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{\"Port\":8080,\"EnableDynamo\":true}");

        var c = TelemetryConfig.Load(_path);
        c.MarkConsent(true);

        var reloaded = TelemetryConfig.Load(_path);
        Assert.True(reloaded.EnableTelemetry);
        Assert.True(reloaded.ConsentAnswered);
        Assert.False(reloaded.NeedsConsentPrompt);
        Assert.True(reloaded.EffectiveEnabled);

        var raw = JObject.Parse(File.ReadAllText(_path));
        Assert.Equal(8080, (int)raw["Port"]!);          // merge-write proof
        Assert.True((bool)raw["EnableDynamo"]!);
        Assert.Equal(TelemetryConfig.CurrentConsentVersion, (string)raw["TelemetryConsentVersion"]!);
    }

    [Fact]
    public void ConsentVersionBump_RequiresReprompt_AndDisablesEffective()
    {
        var c = TelemetryConfig.Load(_path);
        c.MarkConsent(true);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var raw = JObject.Parse(File.ReadAllText(_path));
        raw["TelemetryConsentVersion"] = "2000-01-01";  // simulate older consent
        File.WriteAllText(_path, raw.ToString());

        var stale = TelemetryConfig.Load(_path);
        Assert.True(stale.NeedsConsentPrompt);
        Assert.False(stale.EffectiveEnabled);
    }

    [Fact]
    public void EnsureInstallationId_GeneratesOnce_AndPersists()
    {
        var c = TelemetryConfig.Load(_path);
        var id1 = c.EnsureInstallationId();
        var id2 = TelemetryConfig.Load(_path).EnsureInstallationId();
        Assert.Equal(id1, id2);
        Assert.True(Guid.TryParse(id1, out _));
    }
}
```

- [ ] **Step 2: FAIL. Step 3: Implement**

```csharp
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Telemetry settings backed by ~/.revitcortex/settings.json. All writes are
/// merge-writes (read JObject, set keys, write back) so unrelated keys are
/// never dropped. EffectiveEnabled is THE consent gate: enabled AND answered
/// AND consent version current.
/// </summary>
public class TelemetryConfig
{
    public const string CurrentConsentVersion = "2026-07-07";

    private readonly string _path;
    private JObject _root;

    private TelemetryConfig(string path, JObject root)
    {
        _path = path;
        _root = root;
    }

    public static TelemetryConfig Load(string? path = null)
    {
        var p = path ?? Hosting.CortexEnvironment.Current.SettingsFilePath;
        JObject root;
        try
        {
            root = File.Exists(p) ? JObject.Parse(File.ReadAllText(p)) : new JObject();
        }
        catch
        {
            root = new JObject(); // unreadable settings must not crash telemetry
        }
        return new TelemetryConfig(p, root);
    }

    public bool EnableTelemetry => ReadBool("EnableTelemetry", false);
    public bool ConsentAnswered => ReadBool("TelemetryConsentAnswered", false);
    public string StoredConsentVersion => ReadString("TelemetryConsentVersion", "");
    public string Endpoint => ReadString("TelemetryEndpoint",
        Hosting.CortexEnvironment.Current.DefaultTelemetryEndpoint);
    public long BottleneckDurationMs => ReadLong("BottleneckDurationMs", 10000);
    public long BottleneckResponseBytes => ReadLong("BottleneckResponseBytes", 512000);
    public int ZipPromptFailureThreshold => (int)ReadLong("ZipPromptFailureThreshold", 3);

    public bool NeedsConsentPrompt =>
        !ConsentAnswered || StoredConsentVersion != CurrentConsentVersion;

    public bool EffectiveEnabled =>
        EnableTelemetry && ConsentAnswered && StoredConsentVersion == CurrentConsentVersion;

    public void MarkConsent(bool enabled)
    {
        MergeWrite(root =>
        {
            root["EnableTelemetry"] = enabled;
            root["TelemetryConsentAnswered"] = true;
            root["TelemetryConsentVersion"] = CurrentConsentVersion;
        });
    }

    public string EnsureInstallationId()
    {
        var existing = ReadString("InstallationId", "");
        if (!string.IsNullOrEmpty(existing)) return existing;
        var id = Guid.NewGuid().ToString();
        MergeWrite(root => root["InstallationId"] = id);
        return id;
    }

    private void MergeWrite(Action<JObject> mutate)
    {
        try
        {
            JObject root;
            try
            {
                root = File.Exists(_path) ? JObject.Parse(File.ReadAllText(_path)) : new JObject();
            }
            catch { root = new JObject(); }

            mutate(root);

            var dir = Path.GetDirectoryName(_path);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, root.ToString());
            _root = root;
        }
        catch { /* settings write failure must never crash the host */ }
    }

    private bool ReadBool(string key, bool fallback)
    {
        var t = _root[key];
        return t != null && t.Type == JTokenType.Boolean ? (bool)t : fallback;
    }

    private string ReadString(string key, string fallback)
    {
        var t = _root[key];
        return t != null && t.Type == JTokenType.String ? ((string?)t ?? fallback) : fallback;
    }

    private long ReadLong(string key, long fallback)
    {
        var t = _root[key];
        return t != null && (t.Type == JTokenType.Integer) ? (long)t : fallback;
    }
}
```

- [ ] **Step 4: PASS. Step 5: Build R25+R24, commit** — `feat(telemetry): TelemetryConfig with affirmative-consent gate + merge-write`

---

### Task 7: TelemetryQueue (5 MB cap, drop-oldest, thread-safe)

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/TelemetryQueue.cs`
- Test: `src/RevitCortex.Tests/Telemetry/TelemetryQueueTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using System;
using System.IO;
using System.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetryQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-q-" + Guid.NewGuid().ToString("N"));
    private string QueuePath => Path.Combine(_dir, "telemetry-queue.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static TelemetryEvent Evt(string id) =>
        new TelemetryEvent { EventId = id, Tool = "t", Fingerprint = "f" };

    [Fact]
    public void Enqueue_ThenPeek_ReturnsEventsInOrder()
    {
        var q = new TelemetryQueue(QueuePath);
        q.Enqueue(Evt("a"));
        q.Enqueue(Evt("b"));
        var batch = q.PeekBatch(10);
        Assert.Equal(new[] { "a", "b" }, batch.Events.Select(e => e.EventId));
        Assert.Equal(2, batch.LineCount);
    }

    [Fact]
    public void RemoveLines_DropsOnlyTheBatch()
    {
        var q = new TelemetryQueue(QueuePath);
        q.Enqueue(Evt("a")); q.Enqueue(Evt("b")); q.Enqueue(Evt("c"));
        var batch = q.PeekBatch(2);
        q.RemoveLines(batch.LineCount);
        var rest = q.PeekBatch(10);
        Assert.Equal(new[] { "c" }, rest.Events.Select(e => e.EventId));
    }

    [Fact]
    public void PeekBatch_SkipsMalformedLines_ButCountsThem()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(QueuePath, new[] { "{not json", "" });
        var q = new TelemetryQueue(QueuePath);
        q.Enqueue(Evt("a"));
        var batch = q.PeekBatch(10);
        Assert.Single(batch.Events);
        Assert.Equal(3, batch.LineCount); // malformed lines are consumed with the batch
    }

    [Fact]
    public void Enqueue_OverCap_DropsOldest()
    {
        var q = new TelemetryQueue(QueuePath, maxBytes: 4096);
        for (int i = 0; i < 100; i++) q.Enqueue(Evt("evt-" + i.ToString("D3")));
        Assert.True(new FileInfo(QueuePath).Length <= 4096);
        var batch = q.PeekBatch(1000);
        Assert.Equal("evt-099", batch.Events.Last().EventId); // newest survived
        Assert.NotEqual("evt-000", batch.Events.First().EventId); // oldest dropped
    }

    [Fact]
    public void PendingLineCount_ReflectsQueue()
    {
        var q = new TelemetryQueue(QueuePath);
        Assert.Equal(0, q.PendingLineCount);
        q.Enqueue(Evt("a"));
        Assert.Equal(1, q.PendingLineCount);
    }
}
```

- [ ] **Step 2: FAIL. Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

public class TelemetryBatch
{
    public List<TelemetryEvent> Events { get; }
    public int LineCount { get; }

    public TelemetryBatch(List<TelemetryEvent> events, int lineCount)
    {
        Events = events;
        LineCount = lineCount;
    }
}

/// <summary>
/// Durable JSONL queue for telemetry events. Capped (default 5 MB) with
/// drop-oldest overflow. Thread-safe via a single lock (same spirit as
/// AuditLogger). All operations swallow I/O failures: losing telemetry is
/// always preferable to affecting the host.
/// </summary>
public class TelemetryQueue
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _lock = new object();

    public TelemetryQueue(string path, long maxBytes = 5 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
    }

    public int PendingLineCount
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    return File.Exists(_path) ? File.ReadAllLines(_path).Length : 0;
                }
                catch { return 0; }
            }
        }
    }

    public void Enqueue(TelemetryEvent evt)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(_path,
                    JsonConvert.SerializeObject(evt, Formatting.None) + "\n");

                var info = new FileInfo(_path);
                if (info.Length > _maxBytes) CompactLocked();
            }
            catch { /* never crash the host */ }
        }
    }

    public TelemetryBatch PeekBatch(int maxEvents)
    {
        lock (_lock)
        {
            var events = new List<TelemetryEvent>();
            int lines = 0;
            try
            {
                if (!File.Exists(_path)) return new TelemetryBatch(events, 0);
                foreach (var line in File.ReadAllLines(_path))
                {
                    if (events.Count >= maxEvents) break;
                    lines++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var evt = JsonConvert.DeserializeObject<TelemetryEvent>(line);
                        if (evt != null) events.Add(evt);
                    }
                    catch { /* malformed line: counted, skipped, removed with batch */ }
                }
            }
            catch { }
            return new TelemetryBatch(events, lines);
        }
    }

    public void RemoveLines(int lineCount)
    {
        if (lineCount <= 0) return;
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path)) return;
                var remaining = File.ReadAllLines(_path).Skip(lineCount).ToArray();
                File.WriteAllLines(_path, remaining);
            }
            catch { }
        }
    }

    // Drop oldest lines until under 80% of cap. Caller holds _lock.
    private void CompactLocked()
    {
        var lines = File.ReadAllLines(_path);
        long budget = (long)(_maxBytes * 0.8);
        var kept = new List<string>();
        long size = 0;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            size += lines[i].Length + 1;
            if (size > budget) break;
            kept.Add(lines[i]);
        }
        kept.Reverse();
        File.WriteAllLines(_path, kept);
    }
}
```

- [ ] **Step 4: PASS. Step 5: Build R25+R24, commit** — `feat(telemetry): capped drop-oldest TelemetryQueue`

---

### Task 8: TelemetrySender

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/TelemetrySender.cs`
- Test: `src/RevitCortex.Tests/Telemetry/TelemetrySenderTests.cs`

- [ ] **Step 1: Failing tests (fake HttpMessageHandler)**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class TelemetrySenderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-s-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"accepted\":1,\"knownIssues\":[]}";
        public string? LastRequestBody;
        public string? LastUrl;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            LastRequestBody = request.Content == null ? null
                : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body)
            };
        }
    }

    private (TelemetrySender sender, TelemetryQueue queue, FakeHandler handler) Make()
    {
        Directory.CreateDirectory(_dir);
        var config = TelemetryConfig.Load(Path.Combine(_dir, "settings.json"));
        var queue = new TelemetryQueue(Path.Combine(_dir, "queue.jsonl"));
        var handler = new FakeHandler();
        var sender = new TelemetrySender(config, queue, handler);
        return (sender, queue, handler);
    }

    [Fact]
    public void FlushOnce_EmptyQueue_NoRequest_ReturnsTrue()
    {
        var (sender, _, handler) = Make();
        Assert.True(sender.FlushOnce());
        Assert.Null(handler.LastUrl);
    }

    [Fact]
    public void FlushOnce_Success_PostsBatch_AndDequeues()
    {
        var (sender, queue, handler) = Make();
        queue.Enqueue(new TelemetryEvent { EventId = "e1", Fingerprint = "f1" });

        Assert.True(sender.FlushOnce());
        Assert.EndsWith("/v1/events", handler.LastUrl);
        Assert.Contains("\"eventId\":\"e1\"", handler.LastRequestBody);
        Assert.Equal(0, queue.PendingLineCount);
    }

    [Fact]
    public void FlushOnce_ServerError_KeepsQueue()
    {
        var (sender, queue, handler) = Make();
        handler.Status = HttpStatusCode.InternalServerError;
        queue.Enqueue(new TelemetryEvent { EventId = "e1" });

        Assert.False(sender.FlushOnce());
        Assert.Equal(1, queue.PendingLineCount);
    }

    [Fact]
    public void FlushOnce_KnownIssueInResponse_RaisesEvent()
    {
        var (sender, queue, handler) = Make();
        handler.Body = "{\"accepted\":1,\"knownIssues\":[{\"fingerprint\":\"f1\",\"issueId\":\"RC-014\",\"status\":\"fixed\",\"fixVersion\":\"1.0.42\"}]}";
        queue.Enqueue(new TelemetryEvent { EventId = "e1", Fingerprint = "f1" });

        var matches = new List<KnownIssueMatch>();
        sender.KnownIssueMatched += matches.Add;
        sender.FlushOnce();

        Assert.Single(matches);
        Assert.Equal("RC-014", matches[0].IssueId);
    }
}
```

- [ ] **Step 2: FAIL. Step 3: Implement**

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Batched background flush of the telemetry queue to {endpoint}/v1/events.
/// 5 s HTTP timeout, no aggressive retry (failed batches simply stay queued
/// for the next flush). Never throws from public entry points.
/// </summary>
public class TelemetrySender : IDisposable
{
    private const int MaxBatch = 100;
    private const string ClientKey = "rc-public-2026";

    private readonly TelemetryConfig _config;
    private readonly TelemetryQueue _queue;
    private readonly HttpClient _http;
    private Timer? _timer;
    private int _flushing;

    public event Action<KnownIssueMatch>? KnownIssueMatched;

    public TelemetrySender(TelemetryConfig config, TelemetryQueue queue,
        HttpMessageHandler? handler = null)
    {
        _config = config;
        _queue = queue;
        try
        {
            // net48 host: default protocols may exclude TLS 1.2 (same fix as UpdateChecker).
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch { }
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(5);
        _http.DefaultRequestHeaders.Add("X-RC-Key", ClientKey);
    }

    /// <summary>Start the periodic 5-minute flush timer.</summary>
    public void Start()
    {
        try
        {
            _timer = new Timer(_ => FlushOnce(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }
        catch { }
    }

    /// <summary>Called by the reporter after each enqueue: flush early at 20 pending.</summary>
    public void NotifyEnqueued()
    {
        try
        {
            if (_queue.PendingLineCount >= 20)
                ThreadPool.QueueUserWorkItem(_ => FlushOnce());
        }
        catch { }
    }

    /// <summary>One flush pass. True when the queue is empty afterwards or was empty.</summary>
    public bool FlushOnce()
    {
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return false;
        try
        {
            var batch = _queue.PeekBatch(MaxBatch);
            if (batch.Events.Count == 0)
            {
                if (batch.LineCount > 0) _queue.RemoveLines(batch.LineCount); // all-malformed
                return true;
            }

            var payload = JsonConvert.SerializeObject(new { events = batch.Events });
            var url = _config.Endpoint.TrimEnd('/') + "/v1/events";
            var response = _http.PostAsync(url,
                new StringContent(payload, Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode) return false;

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            _queue.RemoveLines(batch.LineCount);
            RaiseKnownIssues(body);
            return true;
        }
        catch
        {
            return false; // stays queued; next flush retries
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
        }
    }

    private void RaiseKnownIssues(string body)
    {
        try
        {
            var parsed = JsonConvert.DeserializeObject<EventsResponse>(body);
            if (parsed?.KnownIssues == null) return;
            foreach (var m in parsed.KnownIssues)
            {
                try { KnownIssueMatched?.Invoke(m); } catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { _timer?.Dispose(); } catch { }
        try { FlushOnce(); } catch { }   // best-effort shutdown flush
        try { _http.Dispose(); } catch { }
    }

    private class EventsResponse
    {
        [JsonProperty("accepted")] public int Accepted { get; set; }
        [JsonProperty("knownIssues")] public KnownIssueMatch[]? KnownIssues { get; set; }
    }
}
```

- [ ] **Step 4: PASS. Step 5: Build R25+R24, commit** — `feat(telemetry): TelemetrySender with batch flush + known-issue callback`

---

### Task 9: ErrorReporter facade

**Files:**
- Create: `src/RevitCortex.Core/Telemetry/TelemetryEnvironment.cs`
- Create: `src/RevitCortex.Core/Telemetry/ErrorReporter.cs`
- Test: `src/RevitCortex.Tests/Telemetry/ErrorReporterTests.cs`

> **SECURITY ACCEPTANCE CRITERION (carried from Task 3 review).** `MessageSanitizer.TrySanitizeForTransmission` is fail-closed but has one documented residual gap: a lone all-alphabetic token (any case, e.g. the Italian workset name `strutture`) is shape-indistinguishable from a template word and passes. That gap is only acceptable because ErrorReporter is the sole caller and enforces the `messageOrigin=templated` contract. Therefore ErrorReporter MUST:
> 1. Only attempt `TrySanitizeForTransmission` when `errorCode != null && errorCode != "Unknown"` (RevitCortex's own structured `CortexResult.Fail`, not a wrapped exception). This is already in the Step-4 code below — keep it.
> 2. Treat any message that could embed raw model data as exception-origin even when the code is structured. Concretely: this task's tests must include a case proving that a templated message embedding an UNQUOTED interpolated name (e.g. `"Failed to tag room Strutture: object reference not set"`, the bare-`{room.Name}`+`ex.Message` pattern that exists in real Fail templates) does NOT transmit text — assert `MessageOrigin == "exception"` and `SanitizedMessage == null`. If the shape-based sanitizer would let it through, ErrorReporter must additionally gate on `ex.Message` presence / bare-name shape before calling the sanitizer. Fail-closed dominates: when unsure, `origin = "exception"`, no text.
> A reviewer of this task MUST verify both points against the real code, not just the happy-path tests.

> **EMPIRICAL GROUND TRUTH (verified 2026-07-08 against the committed MessageSanitizer + a repo grep of real `CortexResult.Fail` templates).** Two facts drive the gate design below:
> - **(a)** Almost every `ex.Message`-embedding template in `src/RevitCortex.Tools` uses `CortexErrorCode.Unknown` (e.g. `Fail(Unknown, $"Failed to tag walls: {ex.Message}")`), so gate #1 (`errorCode != "Unknown"`) already blocks them. Good, but do not *rely* only on that convention — the spec (paid-readiness, line 176) wants `ex.Message`-embedding templates classed as exception-origin regardless of code.
> - **(b)** Structured-code templates that interpolate a name are almost all single-quoted (`$"Level '{levelName}' not found"`, `$"A grid named '{newName}' already exists"`) → `RxQuoted` strips them → safe. The one bare-after-colon shape (`$"Category not found: {categoryName}"`) is saved ONLY because the trailing colon on `found:` trips `RxSafeWord`. That safety is INCIDENTAL: a bare leading-cap token with no adjacent punctuation (worst case `"...tag room Strutture object reference not set"`) passes the sanitizer and LEAKS `strutture`. The gate must not depend on punctuation luck.

- [ ] **Step 3b: ADDITIONAL GATE (mandatory — the plan's literal Step-4 code alone does NOT satisfy the criterion).**
  Add a private static helper to `ErrorReporter` and call it in the sanitize decision. The helper is a conservative pre-filter that runs the SAME stripping the sanitizer uses, then rejects the message (→ exception origin) if any residue betrays embedded uncontrolled data that the sanitizer's shape-allowlist cannot catch:

```csharp
using System.Text.RegularExpressions;   // add to ErrorReporter usings

// A message is eligible for text transmission only if it is a pure
// structural template: no ex.Message fingerprints, and no capitalized word
// in a NON-INITIAL position after stripping. RevitCortex's own template
// vocabulary is lowercase structural English ("does", "not", "exist",
// "category", "found"); a mid-sentence Capitalized token that survives
// stripping is uncontrolled interpolated data (a workset/room/type/family
// name), which the shape sanitizer would wave through when no punctuation
// happens to be adjacent. Fail-closed: any doubt -> not a pure template.
private static readonly Regex RxNonInitialCap = new Regex(
    @"(?<=\S\s)[A-Z][A-Za-z]*", RegexOptions.Compiled);

private static bool IsPureTemplate(string? message)
{
    if (string.IsNullOrWhiteSpace(message)) return false;
    // Run the sanitizer's own stripping first so quoted names, paths, GUIDs,
    // compound tokens and numbers are already redacted to "_" and do not
    // trip the capital-word check (e.g. 'Strutture' -> _ is fine).
    var stripped = MessageSanitizer.StripForTemplateCheck(message);
    // A non-initial capitalized word surviving stripping = interpolated proper
    // noun / bare name. Reject.
    if (RxNonInitialCap.IsMatch(stripped)) return false;
    return true;
}
```

  This requires exposing the sanitizer's stripping to the reporter. Add ONE `internal static` passthrough to `MessageSanitizer` (do NOT duplicate the regex set):

```csharp
/// <summary>Case-preserving strip used by ErrorReporter's pure-template
/// pre-filter. Same patterns as Normalize but without the final ToLower.</summary>
internal static string StripForTemplateCheck(string? message)
    => StripKnownPatterns(message);
```

  **VERIFIED 2026-07-08:** `StripKnownPatterns` is `private static` in `MessageSanitizer`, and `RevitCortex.Core` has NO `InternalsVisibleTo`. `ErrorReporter` lives in the same assembly (`RevitCortex.Core.Telemetry`), so an `internal static StripForTemplateCheck` is directly visible to it — no `InternalsVisibleTo` attribute is required. The tests assert only through the public `ErrorReporter.Record`, never the helper, so they need no special visibility either. Do NOT add `InternalsVisibleTo`.

  Then in `Record`, change the sanitize condition from:
  `if (errorCode != null && errorCode != "Unknown" && MessageSanitizer.TrySanitizeForTransmission(message, out var safe))`
  to:
  `if (errorCode != null && errorCode != "Unknown" && IsPureTemplate(message) && MessageSanitizer.TrySanitizeForTransmission(message, out var safe))`

  **Why non-initial only:** the first word of a template is legitimately capitalized ("Element does not exist", "Category not found") — that is controlled vocabulary, not interpolated data. Interpolated names appear mid-sentence. This keeps the safe templates (`"Element 12345 does not exist"` → after strip `"Element _ does not exist"` → no non-initial cap → still transmits) while rejecting the leak (`"...room Strutture object..."` → non-initial `Strutture` → exception origin).

- [ ] **Step 3c: MANDATORY adversarial tests** (add to `ErrorReporterTests`, beyond the plan's Step-1 list):

```csharp
[Fact]
public void Record_TemplatedBareNameMidSentence_NeverSendsText()
{
    // Structured code, but the template embeds a bare (unquoted) interpolated
    // name with no adjacent punctuation — the worst-case leak the sanitizer
    // alone would wave through. Must be classed exception-origin, no text.
    var (r, q, _) = Make();
    r.Record("tag_rooms", false, "TransactionFailed",
        "Failed to tag room Strutture object reference not set", "tool", 1, 1);
    var evt = q.PeekBatch(10).Events.Single();
    Assert.Equal("exception", evt.MessageOrigin);
    Assert.Null(evt.SanitizedMessage);
}

[Fact]
public void Record_StructuredCodeButExMessageEmbedded_NeverSendsText()
{
    // A structured (non-Unknown) code whose template still appended ex.Message.
    // Even though gate #1 passes, the embedded exception phrase carries a
    // capitalized proper-noun-shaped token -> must fail closed.
    var (r, q, _) = Make();
    r.Record("some_tool", false, "TransactionFailed",
        "Save failed: The DESKTOP model is locked by Mario", "tool", 1, 1);
    var evt = q.PeekBatch(10).Events.Single();
    Assert.Equal("exception", evt.MessageOrigin);
    Assert.Null(evt.SanitizedMessage);
}

[Fact]
public void Record_QuotedNameInTemplate_StillTransmits_NameRedacted()
{
    // The common safe shape: name is single-quoted, so RxQuoted redacts it.
    // Text still transmits (templated) but the name must NOT appear.
    var (r, q, _) = Make();
    r.Record("create_level", false, "InvalidInput",
        "A level named 'Strutture' already exists", "tool", 1, 1);
    var evt = q.PeekBatch(10).Events.Single();
    Assert.Equal("templated", evt.MessageOrigin);
    Assert.DoesNotContain("strutture", evt.SanitizedMessage!.ToLowerInvariant());
}
```

  If `Record_QuotedNameInTemplate_StillTransmits_NameRedacted` fails because `IsPureTemplate` rejects a quoted-name template (it should NOT — stripping redacts `'Strutture'` to `_` before the capital-word check), that is a real bug in the helper; fix the helper, do not weaken the test.

- [ ] **Step 1: Failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitCortex.Core.Telemetry;
using Xunit;

namespace RevitCortex.Tests.Telemetry;

public class ErrorReporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-r-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (ErrorReporter reporter, TelemetryQueue queue, TelemetryConfig config) Make(
        bool consented = true, long durThreshold = 10000, long bytesThreshold = 512000)
    {
        Directory.CreateDirectory(_dir);
        var settings = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settings,
            "{\"BottleneckDurationMs\":" + durThreshold +
            ",\"BottleneckResponseBytes\":" + bytesThreshold + "}");
        var config = TelemetryConfig.Load(settings);
        if (consented) config.MarkConsent(true);
        config = TelemetryConfig.Load(settings);
        var queue = new TelemetryQueue(Path.Combine(_dir, "queue.jsonl"));
        var env = new TelemetryEnvironment
        {
            PluginVersion = "1.0.40", RevitVersion = "2025",
            Target = "R25", OsMajor = "Windows 10.0", Locale = "it"
        };
        return (new ErrorReporter(config, queue, sender: null, env), queue, config);
    }

    [Fact]
    public void Record_Failure_QueuesErrorEvent_WithFingerprint()
    {
        var (r, q, _) = Make();
        r.Record("create_dimensions", success: false, errorCode: "InvalidInput",
            message: "Element 12345 does not exist", failureStage: "tool",
            durationMs: 10, responseBytes: 20);

        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("error", evt.Kind);
        Assert.Equal("create_dimensions", evt.Tool);
        Assert.Matches("^[0-9a-f]{16}$", evt.Fingerprint);
        Assert.False(string.IsNullOrEmpty(evt.EventId));
        Assert.False(string.IsNullOrEmpty(evt.InstallationId));
        Assert.False(string.IsNullOrEmpty(evt.Timestamp));
    }

    [Fact]
    public void Record_ConsentMissing_IsCompleteNoOp()
    {
        var (r, q, _) = Make(consented: false);
        r.Record("t", false, "Unknown", "boom", "tool", 1, 1);
        Assert.Equal(0, q.PendingLineCount); // not even queued
    }

    [Fact]
    public void Record_TemplatedSafeMessage_SendsSanitizedText()
    {
        var (r, q, _) = Make();
        r.Record("t", false, "InvalidInput", "Element 12345 does not exist", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("templated", evt.MessageOrigin);
        Assert.Contains("does not exist", evt.SanitizedMessage);
    }

    [Fact]
    public void Record_UnknownErrorCode_NeverSendsText()
    {
        var (r, q, _) = Make();
        r.Record("t", false, "Unknown", "Unhandled exception: boom at C:\\x", "tool", 1, 1);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("exception", evt.MessageOrigin);
        Assert.Null(evt.SanitizedMessage);
    }

    [Fact]
    public void Record_SuccessUnderThresholds_NoEvent()
    {
        var (r, q, _) = Make();
        r.Record("t", true, null, null, "tool", durationMs: 5, responseBytes: 5);
        Assert.Equal(0, q.PendingLineCount);
    }

    [Fact]
    public void Record_SuccessOverDuration_QueuesBottleneck()
    {
        var (r, q, _) = Make(durThreshold: 1);
        r.Record("export_to_excel", true, null, null, "tool", durationMs: 50, responseBytes: 5);
        var evt = q.PeekBatch(10).Events.Single();
        Assert.Equal("bottleneck", evt.Kind);
        Assert.Null(evt.ErrorCode);
    }

    [Fact]
    public void Record_RepeatedFailureSameFingerprint_RaisesAtThreshold_Once()
    {
        var (r, _, _) = Make();
        var raised = new List<int>();
        r.RepeatedFailureDetected += (fp, count) => raised.Add(count);

        for (int i = 0; i < 5; i++)
            r.Record("t", false, "InvalidInput", "Element 1 does not exist", "tool", 1, 1);

        Assert.Single(raised);      // fires exactly once, at the threshold
        Assert.Equal(3, raised[0]); // default ZipPromptFailureThreshold
    }
}
```

- [ ] **Step 2: FAIL. Step 3: Implement `TelemetryEnvironment.cs`**

```csharp
namespace RevitCortex.Core.Telemetry;

/// <summary>Host facts stamped on every event. Filled by the Plugin (or tests).</summary>
public class TelemetryEnvironment
{
    public string PluginVersion { get; set; } = "";
    public string RevitVersion { get; set; } = "";
    public string Target { get; set; } = "";
    public string OsMajor { get; set; } = "";
    public string Locale { get; set; } = "";
}
```

- [ ] **Step 4: Implement `ErrorReporter.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Single telemetry entry point. Consent-gated (complete no-op when
/// EffectiveEnabled is false — events are not even queued), fail-closed on
/// message text, never throws. One instance per process, wired by the host.
/// </summary>
public class ErrorReporter
{
    private readonly TelemetryConfig _config;
    private readonly TelemetryQueue _queue;
    private readonly TelemetrySender? _sender;
    private readonly TelemetryEnvironment _env;
    private readonly object _countersLock = new object();
    private readonly Dictionary<string, int> _failureCounts = new Dictionary<string, int>();

    /// <summary>Raised once per fingerprint per process when the repeated-failure
    /// threshold is hit. UI is owned by the Plugin layer (Plan 3).</summary>
    public event Action<string, int>? RepeatedFailureDetected;

    public ErrorReporter(TelemetryConfig config, TelemetryQueue queue,
        TelemetrySender? sender, TelemetryEnvironment env)
    {
        _config = config;
        _queue = queue;
        _sender = sender;
        _env = env;
    }

    public void Record(string tool, bool success, string? errorCode, string? message,
        string failureStage, long durationMs, long responseBytes)
    {
        try
        {
            if (!_config.EffectiveEnabled) return;

            if (success)
            {
                if (durationMs < _config.BottleneckDurationMs
                    && responseBytes < _config.BottleneckResponseBytes) return;
                Enqueue(BuildEvent("bottleneck", tool, null, null, failureStage,
                    "unknown", "exception", null, durationMs, responseBytes));
                return;
            }

            var normalized = MessageSanitizer.Normalize(message);
            var messageClass = MessageClassifier.Classify(errorCode, message);
            var fingerprint = ErrorFingerprinter.Compute(
                tool, errorCode, failureStage, messageClass, normalized);

            string origin = "exception";
            string? sanitized = null;
            if (errorCode != null && errorCode != "Unknown"
                && MessageSanitizer.TrySanitizeForTransmission(message, out var safe))
            {
                origin = "templated";
                sanitized = safe;
            }

            var evt = BuildEvent("error", tool, errorCode, fingerprint, failureStage,
                messageClass, origin, sanitized, durationMs, responseBytes);
            Enqueue(evt);
            CountFailure(fingerprint);
        }
        catch { /* telemetry must never affect the host */ }
    }

    private TelemetryEvent BuildEvent(string kind, string tool, string? errorCode,
        string? fingerprint, string failureStage, string messageClass, string origin,
        string? sanitized, long durationMs, long responseBytes)
    {
        return new TelemetryEvent
        {
            EventId = Guid.NewGuid().ToString(),
            InstallationId = _config.EnsureInstallationId(),
            Kind = kind,
            Fingerprint = fingerprint ?? ErrorFingerprinter.Compute(
                tool, errorCode, failureStage, messageClass, ""),
            Tool = tool,
            ErrorCode = errorCode,
            FailureStage = failureStage,
            MessageClass = messageClass,
            MessageOrigin = origin,
            SanitizedMessage = sanitized,
            PluginVersion = _env.PluginVersion,
            RevitVersion = _env.RevitVersion,
            Target = _env.Target,
            OsMajor = _env.OsMajor,
            Locale = _env.Locale,
            DurationMs = durationMs,
            ResponseBytes = responseBytes,
            Timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    private void Enqueue(TelemetryEvent evt)
    {
        _queue.Enqueue(evt);
        _sender?.NotifyEnqueued();
    }

    private void CountFailure(string fingerprint)
    {
        int count;
        lock (_countersLock)
        {
            _failureCounts.TryGetValue(fingerprint, out count);
            count++;
            _failureCounts[fingerprint] = count;
        }
        if (count == _config.ZipPromptFailureThreshold)
        {
            try { RepeatedFailureDetected?.Invoke(fingerprint, count); } catch { }
        }
    }
}
```

- [ ] **Step 5: Run tests PASS. Step 6: Build R25+R24, commit** — `feat(telemetry): consent-gated ErrorReporter facade`

---

### Task 10: CortexRouter — route-wide exception capture

Today `Route` has `try…finally` with **no catch** ([CortexRouter.cs:157-196]): an exception from the inline path or dispatcher infrastructure skips audit entirely and escapes as raw JSON-RPC `-32603`.

**Files:**
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs` (dispatch block, ~lines 157-196)
- Test: `src/RevitCortex.Tests/Router/CortexRouterExceptionTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterExceptionTests
{
    private class ThrowingTool : ICortexTool
    {
        public string Name => "throwing_tool";
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public CortexResult<object> Execute(JObject input, CortexSession session)
            => throw new System.InvalidOperationException("kaboom");
    }

    private static CortexRouter CreateRouterWith(ICortexTool tool, AuditLogger audit)
    {
        var session = new CortexSession(new SessionStore());
        var router = new CortexRouter(session, new FakeAnalyzer(), audit);
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, ICortexTool>)field.GetValue(router)!;
        tools[tool.Name] = tool;
        return router;
    }

    [Fact]
    public void Route_ToolThrows_ReturnsStructuredUnknown_AndAudits()
    {
        var auditPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "rc-audit-" + System.Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var router = CreateRouterWith(new ThrowingTool(), new AuditLogger(auditPath));

            var result = router.Route("throwing_tool", new JObject());

            Assert.False(result.Success);
            Assert.Equal(CortexErrorCode.Unknown, result.Error!.Code);
            Assert.Contains("Unhandled exception", result.Error.Message);

            var audit = System.IO.File.ReadAllText(auditPath);
            Assert.Contains("throwing_tool", audit);
            Assert.Contains("\"result\":\"fail\"", audit);
        }
        finally
        {
            try { System.IO.File.Delete(auditPath); } catch { }
        }
    }
}
```

Note: the router runs this tool **inline** in tests (`_dispatcher == null`), which is exactly the uncovered path.

- [ ] **Step 2: Run to verify FAIL** (filter `CortexRouterExceptionTests`). Expected: the exception propagates out of `Route` and the test fails with `InvalidOperationException: kaboom`.

- [ ] **Step 3: Add the catch to `Route`**

In `CortexRouter.Route`, the dispatch block currently ends with `finally { _session.ApproveAll = false; }`. Insert a `catch` between the `try` body and the `finally`:

```csharp
        catch (Exception ex)
        {
            // Route-wide backstop: NOTHING may escape Route unstructured —
            // an escaping exception would skip audit + telemetry and surface
            // as a raw JSON-RPC -32603 (paid-readiness spec, P1 finding).
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Route('{toolName}') unhandled: {ex}");
            result = CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Unhandled exception: {ex.Message}",
                suggestion: "Retry; if it persists, send a support report from the RevitCortex ribbon.");
        }
```

- [ ] **Step 4: Run new test PASS + full router suite green**

```powershell
dotnet test src\RevitCortex.Tests\RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~Router"
```

- [ ] **Step 5: Build R25+R24, commit** — `fix(router): route-wide exception capture so audit/telemetry always run`

---

### Task 11: CortexRouter — telemetry hook

**Files:**
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs` (constructor + after the `LogWithPerf` call at ~line 227)
- Test: `src/RevitCortex.Tests/Router/CortexRouterTelemetryTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Telemetry;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterTelemetryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "rc-rt-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private class FailingTool : ICortexTool
    {
        public string Name => "failing_tool";
        public string Category => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public CortexResult<object> Execute(JObject input, CortexSession session)
            => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Element 12345 does not exist");
    }

    private (CortexRouter router, TelemetryQueue queue) Make()
    {
        Directory.CreateDirectory(_dir);
        var settings = Path.Combine(_dir, "settings.json");
        var config = TelemetryConfig.Load(settings);
        config.MarkConsent(true);
        config = TelemetryConfig.Load(settings);
        var queue = new TelemetryQueue(Path.Combine(_dir, "queue.jsonl"));
        var reporter = new ErrorReporter(config, queue, null, new TelemetryEnvironment());

        var session = new CortexSession(new SessionStore());
        var router = new CortexRouter(session, new FakeAnalyzer(),
            auditLogger: new RevitCortex.Core.Security.AuditLogger(
                Path.Combine(_dir, "audit.jsonl")),
            errorReporter: reporter);
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, ICortexTool>)field.GetValue(router)!;
        tools["failing_tool"] = new FailingTool();
        return (router, queue);
    }

    [Fact]
    public void Route_Failure_RecordsTelemetryEvent()
    {
        var (router, queue) = Make();
        router.Route("failing_tool", new JObject());

        var evt = queue.PeekBatch(10).Events.Single();
        Assert.Equal("failing_tool", evt.Tool);
        Assert.Equal("error", evt.Kind);
        Assert.Equal("tool", evt.FailureStage);
        Assert.Equal("InvalidInput", evt.ErrorCode);
    }
}
```

- [ ] **Step 2: FAIL (no `errorReporter` ctor parameter). Step 3: Wire the reporter**

Constructor (line 73) becomes:

```csharp
    private readonly ErrorReporter? _errorReporter;

    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer,
        AuditLogger? auditLogger = null, ErrorReporter? errorReporter = null)
    {
        _session = session;
        _analyzer = analyzer;
        _auditLogger = auditLogger ?? new AuditLogger();
        _errorReporter = errorReporter;
    }
```

Add `using RevitCortex.Core.Telemetry;` to the file's usings. Immediately AFTER the existing `_auditLogger.LogWithPerf(...)` call (~line 227), add:

```csharp
        try
        {
            // Telemetry rides the same single capture point as the audit log.
            // Cache hits above return earlier on purpose: a cached failure is
            // the same occurrence replayed, counting it would inflate stats.
            _errorReporter?.Record(toolName, result.Success,
                result.Error?.Code.ToString(), result.Error?.Message,
                failureStage: "tool",
                durationMs: stopwatch.ElapsedMilliseconds,
                responseBytes: responseBytes);
        }
        catch { /* telemetry must never change the returned result */ }
```

- [ ] **Step 4: Run test PASS + full Router + Telemetry suites green. Step 5: Build R25+R24, commit** — `feat(telemetry): single router hook next to audit log`

---

### Task 12: SafeErrorMessages + SocketService -32603 hardening

`SocketService.ProcessRequest` ([SocketService.cs:154]) currently serializes raw `ex.Message` into the JSON-RPC error. After Task 10 this catch is truly-exceptional (serialization bugs), but it must still not leak raw text.

**Files:**
- Create: `src/RevitCortex.Core/Results/SafeErrorMessages.cs`
- Modify: `src/RevitCortex.Plugin/Communication/SocketService.cs` (the `catch` in `ProcessRequest`)
- Test: `src/RevitCortex.Tests/Results/SafeErrorMessagesTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Results;

public class SafeErrorMessagesTests
{
    [Fact]
    public void ForInternal_NamesTheExceptionType_NotItsMessage()
    {
        var s = SafeErrorMessages.ForInternal(
            new System.IO.FileNotFoundException("C:\\Users\\mario\\secret.rvt missing"));
        Assert.Contains("FileNotFoundException", s);
        Assert.DoesNotContain("secret.rvt", s);
        Assert.DoesNotContain("mario", s);
    }
}
```

- [ ] **Step 2: FAIL. Step 3: Implement**

```csharp
using System;

namespace RevitCortex.Core.Results;

/// <summary>User-facing text for internal failures: type name only, no raw message.</summary>
public static class SafeErrorMessages
{
    public static string ForInternal(Exception ex) =>
        $"Internal error ({ex.GetType().Name}). Details are in the local trace log.";
}
```

- [ ] **Step 4: Update `SocketService.ProcessRequest` catch**

```csharp
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] ProcessRequest internal failure: {ex}");
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(request.Id, -32603, SafeErrorMessages.ForInternal(ex)));
        }
```

Add `using RevitCortex.Core.Results;` if not already present.

- [ ] **Step 5: Test PASS, build R25+R24, commit** — `fix(socket): internal errors return safe message, full detail to trace`

---

### Task 13: Plugin wiring — consent dialog, Settings toggle, bootstrap

No unit tests here (Revit UI); acceptance is the manual smoke in Task 15. Consent rules from the spec: default OFF, two equal choices, cancel = ask again next startup, one-click withdrawal in Settings.

> **TWO CONSENT-PERSISTENCE PITFALLS (carried from Task 6 review — a reviewer MUST verify both):**
> 1. **Persist consent ONLY through `TelemetryConfig.MarkConsent` (merge-write), NEVER through `CortexSettings.Save()`.** `RevitCortex.Core/Security/CortexSettings.cs` `Save()` blind-rewrites the whole file from a 2-field POCO — routing consent through it would erase every telemetry key (EnableTelemetry, TelemetryConsentAnswered/Version, InstallationId, thresholds) plus LogLevel/DisabledTools. This is the v1.0.36 config-corruption class. The GeneralSettingsPage save block already merge-writes its own keys into the JObject (see Task 1C); add the telemetry toggle keys to THAT merge-write path, or call `TelemetryConfig.Load(...).MarkConsent(...)`. Do not introduce a `CortexSettings.Save()` call.
> 2. **After `MarkConsent`, re-`Load()` before reading `EffectiveEnabled`/`NeedsConsentPrompt` on that flow.** `TelemetryConfig` updates its in-memory `_root` only on a SUCCESSFUL write; on a swallowed write failure the same instance keeps returning stale values. The consent dialog (`PromptConsentIfNeeded`) is safe because it calls `MarkConsent` and returns without re-reading. But if the Settings toggle path saves and then reflects `EffectiveEnabled` back to the UI on the same instance, it must re-`Load()` first (cheap) rather than trust in-memory state.

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/Localization.cs` (add keys to `Table`)
- Create: `src/RevitCortex.Plugin/Telemetry/TelemetryBootstrap.cs`
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs` (`OnStartup` around line 88, `OnShutdown`)
- Modify: `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml` + `.xaml.cs` (toggle)

- [ ] **Step 1: Add localization keys** (inside `Table`, after the `support.*` block, same dictionary style):

```csharp
        // ── Telemetry consent ───────────────────────────────────────────
        ["telemetry.consent_instruction"] = new()
        {
            ["en"] = "Help improve RevitCortex?",
            ["it"] = "Vuoi aiutarci a migliorare RevitCortex?",
        },
        ["telemetry.consent_body"] = new()
        {
            ["en"] = "RevitCortex can send pseudonymous error reports when a command fails: tool name, error type, versions, timing. Never sent: model names, file paths, parameter values, user or machine names.\n\nYou can change this anytime in Settings > General.",
            ["it"] = "RevitCortex può inviare segnalazioni pseudonime quando un comando fallisce: nome del tool, tipo di errore, versioni, tempi. Mai inviati: nomi dei modelli, percorsi file, valori dei parametri, nomi utente o macchina.\n\nPuoi cambiare la scelta in qualsiasi momento da Impostazioni > Generale.",
        },
        ["telemetry.consent_enable"] = new()
        {
            ["en"] = "Enable error telemetry",
            ["it"] = "Attiva la telemetria errori",
        },
        ["telemetry.consent_decline"] = new()
        {
            ["en"] = "Keep it disabled",
            ["it"] = "Lascia disattivata",
        },
        ["telemetry.settings_toggle"] = new()
        {
            ["en"] = "Send pseudonymous error telemetry (no model data)",
            ["it"] = "Invia telemetria errori pseudonima (nessun dato del modello)",
        },
```

- [ ] **Step 2: Create `TelemetryBootstrap.cs`**

```csharp
using System;
using System.IO;
using Autodesk.Revit.UI;
using RevitCortex.Core.Telemetry;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Telemetry;

/// <summary>
/// Builds the process-wide telemetry stack (config, queue, sender, reporter)
/// and owns the first-run consent prompt. Everything here is best-effort:
/// telemetry failures must never affect Revit startup.
/// </summary>
internal static class TelemetryBootstrap
{
    public static ErrorReporter? Reporter { get; private set; }
    public static TelemetryConfig? Config { get; private set; }
    private static TelemetrySender? _sender;

    public static void Init(UIControlledApplication application)
    {
        try
        {
            var config = TelemetryConfig.Load();
            var queue = new TelemetryQueue(
                RevitCortex.Core.Hosting.CortexEnvironment.Current.TelemetryQueuePath);
            var sender = new TelemetrySender(config, queue);
            sender.KnownIssueMatched += m =>
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Known issue matched: {m.IssueId} fixed in {m.FixVersion}");
                // Visual toast/badge lands in Plan 3 (needs the Worker of Plan 2 anyway).

            int revitYear = 0;
            try { revitYear = int.Parse(application.ControlledApplication.VersionNumber); }
            catch { }

            var env = new TelemetryEnvironment
            {
                PluginVersion = typeof(TelemetryBootstrap).Assembly.GetName()
                    .Version?.ToString() ?? "unknown",
                RevitVersion = revitYear.ToString(),
                Target = revitYear > 2000 ? "R" + (revitYear - 2000) : "unknown",
                OsMajor = "Windows " + Environment.OSVersion.Version.ToString(2),
                Locale = Localization.Locale
            };
            // PRIVACY (Task 9 security review F3): OsMajor MUST be derived from
            // Version.ToString(2) (major.minor only, e.g. "Windows 10.0"). It must
            // NEVER be Environment.MachineName or OSVersion.VersionString — a full
            // machine name would violate TelemetryEvent's own no-host-identity
            // contract. A reviewer of Task 13 MUST confirm this line is unchanged.

            var reporter = new ErrorReporter(config, queue, sender, env);
            reporter.RepeatedFailureDetected += (fp, count) =>
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Repeated failure {fp} x{count}");
                // Prompt UI (support-report offer) lands in Plan 3.

            sender.Start();
            Config = config;
            Reporter = reporter;
            _sender = sender;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Telemetry init failed: {ex.Message}");
        }
    }

    /// <summary>First-run consent. Startup runs on the Revit UI thread, so a
    /// TaskDialog is legal here. Cancel/close = not answered, ask next startup.</summary>
    public static void PromptConsentIfNeeded()
    {
        try
        {
            var config = Config;
            if (config == null || !config.NeedsConsentPrompt) return;

            var dlg = new TaskDialog("RevitCortex")
            {
                MainInstruction = Localization.T("telemetry.consent_instruction"),
                MainContent = Localization.T("telemetry.consent_body"),
                CommonButtons = TaskDialogCommonButtons.None,
                AllowCancellation = true,
                TitleAutoPrefix = false
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                Localization.T("telemetry.consent_enable"));
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                Localization.T("telemetry.consent_decline"));

            var r = dlg.Show();
            if (r == TaskDialogResult.CommandLink1) config.MarkConsent(true);
            else if (r == TaskDialogResult.CommandLink2) config.MarkConsent(false);
        }
        catch { /* consent prompt must never block startup */ }
    }

    public static void Shutdown()
    {
        try { _sender?.Dispose(); } catch { } // Dispose = best-effort final flush
        _sender = null;
        Reporter = null;
    }
}
```

- [ ] **Step 3: Wire into `RevitCortexApp`**

In `OnStartup`, BEFORE `_router = new CortexRouter(_session, analyzer);` (line 88) add:

```csharp
            Telemetry.TelemetryBootstrap.Init(application);
```

and change the router construction to:

```csharp
            _router = new CortexRouter(_session, analyzer,
                errorReporter: Telemetry.TelemetryBootstrap.Reporter);
```

AFTER the ribbon/UI setup in `OnStartup` (so the dialog appears over a ready Revit), add:

```csharp
            Telemetry.TelemetryBootstrap.PromptConsentIfNeeded();
```

In `OnShutdown`, add as the first line of the try block:

```csharp
            Telemetry.TelemetryBootstrap.Shutdown();
```

In the existing `catch (Exception ex)` of `OnStartup` (line ~150), record the startup failure:

```csharp
            Telemetry.TelemetryBootstrap.Reporter?.Record("_startup", false, "Unknown",
                ex.Message, failureStage: "startup", durationMs: 0, responseBytes: 0);
```

- [ ] **Step 4: Settings toggle.** In `GeneralSettingsPage.xaml`, add directly below `EnableCodeExecutionCheckBox` (mirror its markup/margins):

```xml
<CheckBox x:Name="EnableTelemetryCheckBox" Margin="0,8,0,0"/>
```

In `GeneralSettingsPage.xaml.cs`:
- constructor: `EnableTelemetryCheckBox.Content = Localization.T("telemetry.settings_toggle");`
- `CortexSettings` class (line ~501): add `public bool EnableTelemetry { get; set; }`
- `LoadSettings()` (~line 308): add `EnableTelemetryCheckBox.IsChecked = settings.EnableTelemetry;`
- save block (~line 396), after the `EnableCodeExecution` line:

```csharp
            bool telemetryChoice = EnableTelemetryCheckBox.IsChecked == true;
            settings["EnableTelemetry"] = telemetryChoice;
            // Saving the page is an affirmative action: stamp consent so the
            // first-run dialog does not re-ask what the user just decided.
            settings["TelemetryConsentAnswered"] = true;
            settings["TelemetryConsentVersion"] =
                RevitCortex.Core.Telemetry.TelemetryConfig.CurrentConsentVersion;
```

- [ ] **Step 5: Build R25 + R24 (Plugin), run FULL test suite, commit** — `feat(telemetry): consent dialog, settings toggle, plugin bootstrap wiring`

---

### Task 14: Documentation alignment (Phase 0 items)

**Files:**
- Modify: `docs/SECURITY.md`
- Modify: `docs/USER_GUIDE.md`
- Modify (OneDrive product folder): `C:\Users\luigi.dattilo\OneDrive - GPA Ingegneria Srl\Documenti\RevitCortex\RevitCortex-SpecificaTecnica-2026-06-16.md`

- [ ] **Step 1: `docs/SECURITY.md`** — add a section "Outbound telemetry (v1.0.4x+)":

```markdown
## Outbound telemetry (v1.0.4x+)

RevitCortex can send pseudonymous error/bottleneck events to
`https://ingest.revitcortex.dev` (`POST /v1/events`). This surface is:

- **Opt-in, default OFF.** Gated by `EnableTelemetry` + `TelemetryConsentAnswered`
  + `TelemetryConsentVersion` in `~/.revitcortex/settings.json`. No event is
  queued before affirmative consent (first-run dialog or Settings toggle).
- **Minimal by construction.** Events carry: tool name, error code/class,
  fingerprint, versions, locale, duration, response size, random installation
  GUID. Never: tool inputs, raw exception text, document titles/paths,
  usernames, machine names, parameter/family/type names, element ids
  (enforced by `MessageSanitizer` fail-closed verdict + unit tests).
- **Fail-safe.** 5 s timeout, offline queue capped at 5 MB (drop-oldest),
  all entry points wrapped: telemetry can never crash or slow Revit.
```

- [ ] **Step 2: `docs/USER_GUIDE.md`** — in the Settings section, add:

```markdown
### Telemetria errori (opzionale, default disattivata)

Al primo avvio RevitCortex chiede se attivare la telemetria errori pseudonima.
Se attivata, quando un comando fallisce viene inviato un evento minimale
(nome tool, tipo errore, versioni, tempi) a ingest.revitcortex.dev. Non vengono
MAI inviati: nomi dei modelli, percorsi, valori di parametri, nomi utente o
macchina. La scelta si cambia in ogni momento da **Impostazioni > Generale >
"Invia telemetria errori pseudonima"**. Con la telemetria disattivata non viene
accodato né inviato nulla.
```

- [ ] **Step 3: SpecificaTecnica (OneDrive)** — RNF-01 "Eccezioni da documentare" list: add

```markdown
- Telemetria errori pseudonima verso ingest.revitcortex.dev (opt-in, default OFF; nessun dato BIM — stesso standard dell'update checker);
```

and RF-07: add

```markdown
- Telemetria automatica errori/bottleneck (opt-in, eventi minimali pseudonimi) con coda offline locale; upload report su endpoint dedicato in arrivo (fase 3).
```

- [ ] **Step 4: Commit repo docs** — `docs(telemetry): SECURITY + USER_GUIDE outbound telemetry surface` (the OneDrive file is outside the repo — just save it).

---

### Task 15: Final verification

- [ ] **Step 1: Full build matrix** (per release rule, all 5 targets must stay green; R23/R26/R27 rarely break if R24+R25 pass, but check before any release)

```powershell
dotnet build -c "Debug R25" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src\RevitCortex.Plugin\RevitCortex.Plugin.csproj
dotnet build src\RevitCortex.Server\RevitCortex.Server.csproj
```

Expected: 0 errors each.

- [ ] **Step 2: Full test suite**

```powershell
dotnet test src\RevitCortex.Tests\RevitCortex.Tests.csproj -c "Debug R25"
```

Expected: all pass, 1 skipped (`RequiresRevitApiFact`), 0 failed. Count must be ≥ the pre-plan baseline (221) + all new telemetry/router tests.

- [ ] **Step 3: Live Revit smoke (manual, from the spec's checklist — record results in the PR/commit message)**

1. Deploy the DEV profile (`deploy-dev.ps1` — never `deploy.ps1` from this worktree), start Revit → prod tab "RevitCortex" AND dev tab "RevitCortex Dev" both present; the dev first-run consent dialog appears with two equal choices; choose "Lascia disattivata".
2. Trigger any failing tool 3× via the dev plugin → `~/.revitcortex-dev/telemetry-queue.jsonl` must NOT exist/grow (consent off = no queueing).
3. Dev Settings > General → enable the toggle, save. Trigger a failing tool → dev queue file gains one event; inspect it: no paths/titles/usernames anywhere.
4. No endpoint is live yet (dev default endpoint is localhost wrangler): verify Revit stays fully responsive while the sender fails silently (5 s timeout, event stays queued).
5. Restart Revit → no consent re-prompt (already answered). Verify `~/.revitcortex/` (prod) files are completely untouched by the dev session.

- [ ] **Step 4: Commit any smoke fixes, then hand off** — Plan 2 (ingest Worker) unblocks the sender; Plan 3 wires the deferred UI (repeated-failure prompt, known-issue toast).
