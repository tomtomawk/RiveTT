using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Server.Connection;

/// <summary>
/// Discovers the currently running MCPRVTT27 Revit process and speaks the local
/// JSON-RPC protocol through a Windows named pipe. No TCP port is opened.
/// </summary>
public sealed class RevitPipeBridge : IDisposable
{
    private readonly TimeSpan _commandTimeout;
    private int _requestCounter;

    public RevitPipeBridge(int commandTimeoutSeconds = 300)
    {
        _commandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
    }

    public async Task<JToken> SendCommandAsync(string method, JObject parameters, CancellationToken cancellationToken)
    {
        var pipeName = RevitSessionDiscovery.FindPreferredPipe();
        if (pipeName == null)
        {
            throw new InvalidOperationException(
                "No MCPRVTT27 Revit 2027 session is available. Open Revit 2027 and wait for the project window to appear.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);
        await using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to the Revit pipe '{pipeName}'.");
        }

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        var id = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
            ["id"] = id
        };
        await writer.WriteLineAsync(request.ToString(Formatting.None)).ConfigureAwait(false);
        var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (line == null) throw new IOException("The Revit pipe closed before returning a result.");

        var response = JObject.Parse(line);
        if (response["id"]?.ToString() != id)
            throw new IOException("The Revit pipe returned a response for a different request.");
        if (response["error"] is JObject error)
        {
            var data = error["data"] as JObject;
            return new JObject
            {
                ["success"] = false,
                ["error"] = data?.DeepClone() ?? new JObject
                {
                    ["message"] = error["message"]?.ToString() ?? "Unknown Revit error"
                }
            };
        }
        return response["result"] ?? JValue.CreateNull();
    }

    public void Dispose() { }
}

internal sealed class RevitSessionRecord
{
    public int ProcessId { get; set; }
    public string PipeName { get; set; } = "";
    public string StartedAtUtc { get; set; } = "";
}

internal static class RevitSessionDiscovery
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MCPRVTT27", "sessions");

    public static string? FindPreferredPipe()
    {
        if (!Directory.Exists(DirectoryPath)) return null;
        var sessions = Directory.EnumerateFiles(DirectoryPath, "*.json")
            .Select(Read)
            .Where(record => record != null && IsRunning(record!.ProcessId))
            .Cast<RevitSessionRecord>()
            .OrderByDescending(record => record.StartedAtUtc, StringComparer.Ordinal)
            .ToList();

        foreach (var stale in Directory.EnumerateFiles(DirectoryPath, "*.json")
                     .Where(path => sessions.All(record => !path.EndsWith(record.ProcessId + ".json", StringComparison.OrdinalIgnoreCase))))
        {
            try { File.Delete(stale); } catch { }
        }
        return sessions.FirstOrDefault()?.PipeName;
    }

    private static RevitSessionRecord? Read(string path)
    {
        try { return JsonConvert.DeserializeObject<RevitSessionRecord>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited && process.ProcessName.StartsWith("Revit", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>Serializes calls because Revit ExternalEvent executes one request at a time.</summary>
public sealed class RevitConnectionManager
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public async Task<JToken> ExecuteAsync(string method, JObject parameters, CancellationToken cancellationToken = default)
        => await ExecuteAsync(method, parameters, 300, cancellationToken).ConfigureAwait(false);

    public async Task<JToken> ExecuteAsync(string method, JObject parameters, int commandTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var bridge = new RevitPipeBridge(commandTimeoutSeconds);
            return await bridge.SendCommandAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }
}
