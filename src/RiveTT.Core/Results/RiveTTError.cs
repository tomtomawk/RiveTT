using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace RiveTT.Core.Results;

public class RiveTTError
{
    [JsonConverter(typeof(StringEnumConverter))]
    [JsonProperty("code")]
    public RiveTTErrorCode Code { get; }

    [JsonProperty("message")]
    public string Message { get; }

    [JsonProperty("suggestion", NullValueHandling = NullValueHandling.Ignore)]
    public string? Suggestion { get; }

    [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, object>? Context { get; }

    public RiveTTError(RiveTTErrorCode code, string message,
        string? suggestion = null, Dictionary<string, object>? context = null)
    {
        Code = code;
        Message = message;
        Suggestion = suggestion;
        Context = context;
    }
}
