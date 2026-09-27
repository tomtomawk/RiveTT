using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RiveTT.Server.Connection;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<RevitConnectionManager>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "RiveTT",
            // Read from the assembly, not typed here: a literal is a second place to
            // forget when Directory.Build.props moves, and this number is what an MCP
            // client shows the user as "the connector version".
            Version = ConnectorVersions.McpServer
        };
        options.ServerInstructions =
            "RiveTT connects automatically to the active Revit session (2026.5+ or 2027) through a local Windows named pipe. " +
            "It is always in automatic mode: commands never open an authorization dialog. " +
            "BUT every Revit session starts READ-ONLY: tools that can modify the model are refused with " +
            "PermissionDenied until a human presses Ecriture in the RiveTT ribbon panel (Add-Ins tab). " +
            "No tool can lift that lock, dryRun included; read execution.writesAllowed, or " +
            "get_server_capabilities.readOnlyMode, and ask the user to unlock rather than retrying. " +
            "Prefer the dedicated architectural tools, validate the result after each write, and treat " +
            "send_code_to_revit as a LAST RESORT when no dedicated tool exists. " +
            "Every write is still executed inside Revit transactions and recorded in the audit log. " +
            // The map below is here because clients load tool schemas on demand: in the field
            // session of 2026-09-24 only 7 of ~200 tools were used, and a dozen that would have
            // prevented errors were never looked up. These instructions are always read.
            "TOOL MAP (load by exact name). See: capture_view (the view as an IMAGE, after every significant " +
            "write), get_selected_elements (geometry, closed loops). Check: list_warnings, check_model_health, " +
            "validate_dwelling (built dwellings vs the agency rules), validate_spec (a design as JSON, no Revit, " +
            "BEFORE modelling). Families: describe_family before placing (origin, visible footprint, Z rule). " +
            "Create in batch: create_point_based_element (key, levelName, familyName+typeName, findHost, " +
            "zMode relativeToLevel for furniture), create_line_based_element, create_room, create_floor. " +
            "Stairs: create_stair. Walls to roof: attach_walls. Tags: tag_rooms (tagTypeId, onePerParameter). " +
            "Areas: manage_area_plans (regulatory area; a sum of rooms is not a SHAB). Script: send_code_to_revit " +
            "(transactionMode readonly works while locked; fromScript + edits corrects one line; Section(...) " +
            "keeps what passed). execution.sessionUnchanged: true means the versions and mode are those of the " +
            "previous response; " +
            "documentTitle and revitProcessId are on every response - check them before a write.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
