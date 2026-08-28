using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Meta;

[ToolSafety(true, false)]
public class SayHelloTool : IRiveTTTool
{
    public string Name => "ping_revit";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Say Hello";
    public RiveTTResult<object> Execute(JObject input, RiveTTSession session)
    {
        var message = input["message"]?.ToString() ?? "Hello from RiveTT!";

        return RiveTTResult<object>.Ok(new
        {
            message,
            locale = session.DetectedLocale,
            toolCount = "RiveTT is running"
        });
    }
}
