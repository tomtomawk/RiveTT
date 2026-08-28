using Newtonsoft.Json;
using System.Collections.Generic;

namespace RiveTT.Core.Results;

public class RiveTTResult<T>
{
    [JsonProperty("success")]
    public bool Success { get; }

    [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
    public T? Data { get; }

    [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
    public RiveTTError? Error { get; }

    private RiveTTResult(bool success, T? data, RiveTTError? error)
    {
        Success = success;
        Data = data;
        Error = error;
    }

    public static RiveTTResult<T> Ok(T data)
        => new(true, data, null);

    public static RiveTTResult<T> Fail(RiveTTErrorCode code, string message,
        string? suggestion = null, Dictionary<string, object>? context = null)
        => new(false, default, new RiveTTError(code, message, suggestion, context));
}
