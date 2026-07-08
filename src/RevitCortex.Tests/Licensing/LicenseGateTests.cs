using System;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseGateTests
{
    private static LicenseGate Gate(LicenseState state, bool isDev = false)
        => new LicenseGate(() => state, isDev);

    private static bool IsReadOnly(string toolName)
        => toolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void CurrentState_ReturnsUnderlyingState_WhenNotDev()
    {
        Assert.Equal(LicenseState.Active, Gate(LicenseState.Active).CurrentState());
        Assert.Equal(LicenseState.Expired, Gate(LicenseState.Expired).CurrentState());
    }

    [Fact]
    public void CurrentState_IsDev_AlwaysActive_EvenWhenUnderlyingExpired()
    {
        Assert.Equal(LicenseState.Active, Gate(LicenseState.Expired, isDev: true).CurrentState());
    }

    [Fact]
    public void Decision_ActiveTrialGrace_AllowsEverything()
    {
        foreach (var state in new[] { LicenseState.Active, LicenseState.Trial, LicenseState.Grace })
        {
            var gate = Gate(state);
            Assert.True(gate.Allows("delete_element", IsReadOnly));
            Assert.True(gate.Allows("get_element_parameters", IsReadOnly));
        }
    }

    [Fact]
    public void Decision_ExpiredOrInvalid_BlocksWrite_AllowsReadOnly()
    {
        foreach (var state in new[] { LicenseState.Expired, LicenseState.Invalid })
        {
            var gate = Gate(state);
            Assert.False(gate.Allows("delete_element", IsReadOnly));
            Assert.True(gate.Allows("get_element_parameters", IsReadOnly));
        }
    }

    [Fact]
    public void Decision_IsDev_AllowsWrite_EvenWhenUnderlyingExpired()
    {
        Assert.True(Gate(LicenseState.Expired, isDev: true).Allows("delete_element", IsReadOnly));
    }

    // fix #8: a throwing provider must NOT be masked as Active. It fails CLOSED (Invalid);
    // a write is therefore blocked. (Router-level null-gate is what makes gating opt-in.)
    [Fact]
    public void FaultingProvider_FailsClosed_Invalid_BlocksWrite()
    {
        var gate = new LicenseGate(() => throw new InvalidOperationException("boom"), isDev: false);
        Assert.Equal(LicenseState.Invalid, gate.CurrentState());
        Assert.False(gate.Allows("delete_element", IsReadOnly));
        Assert.True(gate.Allows("get_element_parameters", IsReadOnly));
    }
}
