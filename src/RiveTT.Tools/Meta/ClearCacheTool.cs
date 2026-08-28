using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Meta;

/// <summary>
/// Admin: drops every cached tool result. Use for live debugging when a
/// tool is suspected of returning stale data, or to reset hit-rate
/// telemetry between experiments.
/// </summary>
[ToolSafety(true, false)]
public class ClearCacheTool : IRiveTTTool
{
    public string Name => "clear_cache";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Drops every entry from the tool-result cache. Returns the entry count just before flushing.";

    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var before = session.Cache.GetStats().EntryCount;
        session.Cache.InvalidateAll();
        return RiveTTResult<object>.Ok(new
        {
            cleared = before,
            message = before == 0
                ? "Cache was already empty."
                : $"Cleared {before} cache entries.",
        });
    }
}
