using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Meta;

[ToolSafety(true, false)]
public sealed class GetServerCapabilitiesTool : ICortexTool
{
    public string Name => "get_server_capabilities";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report the effective MCPRVTT27 execution, safety, response, and document capability contract.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var caps = session.Capabilities;
        return CortexResult<object>.Ok(new
        {
            connector = "MCPRVTT27",
            revitVersion = 2027,
            runtime = ".NET 10 / Windows x64",
            transport = "named_pipe_current_user",
            executionMode = "automatic",
            confirmationRequired = false,
            // There is no read-only mode in MCPRVTT27. Per-response,
            // execution.toolReadOnly classifies the tool that answered; it is not a
            // session state. The old field name was "readOnly", which read as a
            // server-wide lock and led to writes being believed impossible.
            writesAllowed = true,
            readOnlyModeExists = false,
            executionFields = new
            {
                toolReadOnly = "classification of the tool that produced this response",
                toolDestructive = "the tool can delete or overwrite model data",
                writesAllowed = "session-wide: always true, MCPRVTT27 has no read-only mode",
                cached = "the response was served from the tool result cache"
            },
            dryRunDefault = true,
            unitPolicy = new
            {
                inputs = "lengths in mm, angles in degrees",
                outputs = "project display units, with an explicit unit and the Revit internal value " +
                          "(internalValue, in ft/ft2/ft3) on numeric parameters",
                elevation = "create_wall ignores locationLine.z (baseLevelId governs); " +
                            "create_door/create_window locationPoint.z is an absolute project elevation " +
                            "unless zMode=relativeToLevel"
            },
            parameterNames = new
            {
                localization = "parameter and schedule-field names resolve in English or in the document " +
                               "language (Mark/Repere, Level/Niveau, Width/Largeur, Type Name/Nom du type)",
                unresolved = "reported in unresolvedParameterNames with suggestions, never as an empty column"
            },
            auditLogPath = CortexEnvironment.Current.AuditLogPath,
            responseModes = new[] { "summary", "idsOnly", "details" },
            selectionScopes = new[]
            {
                "elementIds", "selectionToken", "savedSelectionName",
                "selection", "last_filter", "active_view", "whole_model"
            },
            document = new
            {
                locale = session.DetectedLocale,
                hasWorksets = caps.HasWorksets,
                hasPhases = caps.HasPhases,
                hasDesignOptions = caps.HasDesignOptions,
                hasLinkedModels = caps.HasLinkedModels,
                enabledDynamicTools = caps.EnabledTools.OrderBy(name => name).ToArray()
            },
            // Stated limitations save an agent a fruitless search of the whole tool
            // catalogue. Everything listed here was looked for, and not found, during
            // real sessions.
            lifecycleLimitations = new[]
            {
                "edit_family (opening the family document) is not exposed: Document.EditFamily deadlocked from this ExternalEvent dispatcher. To change a family, edit the .rfa outside Revit and reload it with load_family.",
                "Rebar propagation is not exposed by the Revit API on any supported version; propagate_rebar only reports that.",
                "Adding a member to a group cannot be done in place: edit_group_members ungroups and recreates the type, and the other instances keep the old definition. REMOVING a member is different — it is Revit's exclusion, applied to that instance only, with the type untouched.",
                "There is no API to restore an excluded group member: do it from the Revit ribbon (Restore Excluded Members) after selecting the instance.",
                "Stairs are created by component (straight runs + automatic landings) through create_stair. Sketched stairs, spiral runs and winders are not exposed."
            },
            discoveryHints = new[]
            {
                "A blank project comes from create_document(templatePath, targetPath) — save_as_document duplicates the OPEN model instead.",
                "open_document switches the active document; every later call targets it and all caches are flushed.",
                "Vertical circulation: create_stair between two levels, then a railing via its railingTypeId.",
                "System types (walls, floors, ceilings, roofs, railings, stairs, title blocks) are NOT loadable families: enumerate them with list_system_types, duplicate them with duplicate_system_type.",
                "There is no 'create similar' tool: copy_elements with an offset re-hosts the copy and does the same job, including across levels (level constraints are recomputed).",
                "To split a room without a physical wall, use create_room_separation_line.",
                "Instances of ONE group type may legitimately differ: an excluded member, or a grouped wall whose height follows its own level constraints. manage_model_groups reports memberCount and hasExcludedMembers per instance — never assume the first instance holds the full definition.",
                "Each group instance owns its OWN copies of the members, with their own element ids: ids read from one instance mean nothing in another.",
                "Category labels are localized and sometimes ambiguous (French Revit names the viewport category 'Fenetres ', like windows): prefer the OST_* codes returned as categoryBic."
            }
        });
    }
}
