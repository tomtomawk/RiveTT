namespace RiveTT.Core.Tools;

/// <summary>
/// A tool that accepts <c>async: true</c>: the router starts it as a background job (the Revit event keeps
/// running, the call returns a job id at once) instead of waiting for it. See <c>JobRegistry</c>.
/// </summary>
public interface IAsyncCapableTool
{
}

/// <summary>
/// A tool answered on the calling thread, never queued behind a running Revit event, so it can report on
/// that very event (get_job_status). It must not touch the Revit API.
/// </summary>
public interface IInlineTool
{
}
