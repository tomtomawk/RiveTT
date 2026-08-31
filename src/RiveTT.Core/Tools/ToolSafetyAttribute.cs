using System;

namespace RiveTT.Core.Tools;

/// <summary>
/// Declares the safety contract for a RiveTT tool: what it is allowed to touch, and
/// whether it can be asked to describe the change instead of making it.
///
/// ReadOnly is a PERMISSION BOUNDARY since the ribbon write lock, not just metadata:
/// the router refuses any tool with ReadOnly=false while the session is locked.
///
/// SupportsDryRun exists because the router used to infer a preview from the CALLER's
/// request instead of the tool's behavior. A tool that never reads dryRun executed
/// normally and the response was still stamped dryRun:true, mutated:false — the agent
/// was told the model was untouched right after it had been changed. The flag is
/// declared here, next to the other two, because this is the one place the router
/// already consults before executing anything.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ToolSafetyAttribute : Attribute
{
    public ToolSafetyAttribute(bool readOnly, bool destructive = false, bool supportsDryRun = false)
    {
        ReadOnly = readOnly;
        Destructive = destructive;
        SupportsDryRun = supportsDryRun;
    }

    public bool ReadOnly { get; }
    public bool Destructive { get; }

    /// <summary>
    /// True when the tool actually READS <c>dryRun</c> and returns a preview instead of
    /// applying the change. False makes the router refuse <c>dryRun: true</c> outright
    /// rather than executing the write and calling it a preview.
    /// </summary>
    public bool SupportsDryRun { get; }
}

public sealed class ToolSafetyInfo
{
    public ToolSafetyInfo(bool readOnly, bool destructive, bool supportsDryRun = false)
    {
        ReadOnly = readOnly;
        Destructive = destructive;
        SupportsDryRun = supportsDryRun;
    }

    public bool ReadOnly { get; }
    public bool Destructive { get; }
    public bool SupportsDryRun { get; }
}

public interface IToolSafetyAware
{
    ToolSafetyInfo GetToolSafety();
}
