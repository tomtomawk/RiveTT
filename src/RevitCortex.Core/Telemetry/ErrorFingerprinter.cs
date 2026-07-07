using System.Security.Cryptography;
using System.Text;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Stable bug identity: SHA256(tool|errorCode|failureStage|messageClass|normalizedMessage),
/// first 16 hex chars. Two occurrences of the same bug (differing only in element
/// ids/paths/numbers) produce the same fingerprint because the caller passes a
/// normalized message (see <see cref="MessageSanitizer.Normalize"/>), not the raw text.
/// </summary>
public static class ErrorFingerprinter
{
    public static string Compute(string tool, string? errorCode, string failureStage,
        string messageClass, string normalizedMessage)
    {
        var input = tool + "|" + (errorCode ?? "") + "|" + failureStage + "|"
            + messageClass + "|" + normalizedMessage;
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
