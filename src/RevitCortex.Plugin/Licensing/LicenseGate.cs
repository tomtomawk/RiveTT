using System;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Plugin-side glue between Core license evaluation and the router. Holds a CACHED
/// <see cref="LicenseState"/> exposed via a provider delegate (computed at bootstrap +
/// on explicit refresh, NOT per Route call). In dev the gate is transparent (always
/// Active). A throwing/faulting provider fails CLOSED (Invalid), NOT open — the router's
/// null-gate guard is what makes gating opt-in, so licensing never crashes Revit while
/// still not silently masking a fault as a valid license.
/// </summary>
public sealed class LicenseGate
{
    private readonly Func<LicenseState> _stateProvider;
    private readonly bool _isDev;

    public LicenseGate(Func<LicenseState> stateProvider, bool isDev)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _isDev = isDev;
    }

    public bool IsDev => _isDev;

    /// <summary>Cached state. Dev is always Active; a faulting provider fails closed to
    /// Invalid (default(LicenseState)).</summary>
    public LicenseState CurrentState()
    {
        if (_isDev) return LicenseState.Active;
        try { return _stateProvider(); }
        catch { return LicenseState.Invalid; }
    }

    /// <summary>
    /// Block only when the state is Expired or Invalid AND the tool is NOT read-only.
    /// Everything else is allowed. <paramref name="isReadOnly"/> is the router's own
    /// IsToolReadOnly classifier — no new classification here.
    /// </summary>
    public bool Allows(string toolName, Func<string, bool> isReadOnly)
    {
        var state = CurrentState();
        if (state != LicenseState.Expired && state != LicenseState.Invalid)
            return true;
        return isReadOnly(toolName);
    }
}
