namespace RevitCortex.Core.Licensing;

/// <summary>
/// Outcome of an <see cref="ILicenseBackend.Activate"/> / Validate call. Success carries
/// a signed wire token; failure carries a human-readable error. Class (not record) for net48.
/// </summary>
public class LicenseActivationResult
{
    public bool Success { get; }
    public string? Token { get; }
    public string? Error { get; }

    private LicenseActivationResult(bool success, string? token, string? error)
    {
        Success = success;
        Token = token;
        Error = error;
    }

    public static LicenseActivationResult Ok(string token) =>
        new LicenseActivationResult(true, token, null);

    public static LicenseActivationResult Fail(string error) =>
        new LicenseActivationResult(false, null, error);
}
