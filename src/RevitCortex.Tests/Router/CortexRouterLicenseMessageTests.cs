using System.Reflection;
using Xunit;

namespace RevitCortex.Tests.Router;

/// <summary>
/// Localization is internal and the test assembly has no InternalsVisibleTo
/// (see CortexRouterHashStabilityTests for the same constraint) — reached via reflection.
/// </summary>
public class CortexRouterLicenseMessageTests
{
    private static readonly System.Type LocalizationType =
        System.Type.GetType("RevitCortex.Plugin.UI.Localization, RevitCortex.Plugin")!;

    private static string T(string key)
    {
        var m = LocalizationType.GetMethod("T",
            BindingFlags.Public | BindingFlags.Static, binder: null,
            types: new[] { typeof(string) }, modifiers: null)!;
        return (string)m.Invoke(null, new object[] { key })!;
    }

    private static string T(string key, params object?[] args)
    {
        var m = LocalizationType.GetMethod("T",
            BindingFlags.Public | BindingFlags.Static, binder: null,
            types: new[] { typeof(string), typeof(object?[]) }, modifiers: null)!;
        return (string)m.Invoke(null, new object?[] { key, args })!;
    }

    [Fact]
    public void GateBlockedKey_IsTranslated_AndInterpolatesToolName()
    {
        var msg = T("license.gate_blocked", "create_level");
        Assert.NotEqual("license.gate_blocked", msg);   // a translation exists
        Assert.Contains("create_level", msg);           // {0} interpolated
    }

    [Fact]
    public void GateSuggestionKey_IsTranslated()
    {
        var s = T("license.gate_suggestion");
        Assert.NotEqual("license.gate_suggestion", s);
        Assert.NotEqual("", s);
    }
}
