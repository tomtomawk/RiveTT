namespace RevitCortex.Core.Licensing;

/// <summary>
/// Client-resolved entitlement state. Fail-closed: the default (0) value is Invalid,
/// so an uninitialized or corrupt state is never mistaken for a valid one.
/// NOTE: the numeric order is NOT semantically comparable with &lt; / &gt;; the only
/// contract is default(LicenseState) == Invalid.
/// </summary>
public enum LicenseState
{
    Invalid = 0,
    Expired = 1,
    Grace = 2,
    Trial = 3,
    Active = 4
}
