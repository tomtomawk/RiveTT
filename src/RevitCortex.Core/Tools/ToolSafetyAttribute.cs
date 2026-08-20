using System;

namespace RevitCortex.Core.Tools;

/// <summary>
/// Declares the safety contract for a RevitCortex tool.
/// ReadOnly and Destructive are metadata available to routing and auditing.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ToolSafetyAttribute : Attribute
{
    public ToolSafetyAttribute(bool readOnly, bool destructive = false)
    {
        ReadOnly = readOnly;
        Destructive = destructive;
    }

    public bool ReadOnly { get; }
    public bool Destructive { get; }
}

public sealed class ToolSafetyInfo
{
    public ToolSafetyInfo(bool readOnly, bool destructive)
    {
        ReadOnly = readOnly;
        Destructive = destructive;
    }

    public bool ReadOnly { get; }
    public bool Destructive { get; }
}

public interface IToolSafetyAware
{
    ToolSafetyInfo GetToolSafety();
}
