using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RevitCortex.Server.Connection;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<RevitConnectionManager>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "MCPRVTT27",
            Version = "0.2.0"
        };
        options.ServerInstructions =
            "MCPRVTT27 connects automatically to the active Revit 2027 session through a local Windows named pipe. " +
            "It is always in automatic mode: commands never open an authorization dialog. Prefer the dedicated architectural " +
            "tools, validate the result after each write, and treat send_code_to_revit as a LAST RESORT when no dedicated tool exists. " +
            "Every write is still executed inside Revit transactions and recorded in the audit log.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
