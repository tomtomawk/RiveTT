using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RiveTT.Core.Tools;

/// <summary>
/// Explicit read branches of an otherwise writable tool. Unknown actions stay locked.
/// The action is top-level unless UseDataEnvelope explicitly mirrors the runtime.
/// A nested action must never mask a write in a differently shaped runtime.
/// DefaultAction must match the runtime's default, and reads must not use a transaction
/// that commits changes. This is part of the same permission boundary as ToolSafety.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ReadOnlyActionsAttribute : Attribute
{
    public ReadOnlyActionsAttribute(string defaultAction, params string[] actions)
    {
        DefaultAction = defaultAction;
        Actions = actions;
    }

    public string DefaultAction { get; }
    public string[] Actions { get; }
    public bool UseDataEnvelope { get; set; }

    public bool Matches(JObject input)
    {
        // Only opt in when the runtime uses precisely the same envelope precedence.
        if (!UseDataEnvelope && input["data"] is JObject) return false;
        var source = UseDataEnvelope ? input["data"] as JObject ?? input : input;
        var token = source["action"];
        if (token != null && token.Type != JTokenType.Null && token.Type != JTokenType.String)
            return false;
        var action = token?.Value<string>() ?? DefaultAction;
        return Actions.Contains(action, StringComparer.OrdinalIgnoreCase);
    }
}
