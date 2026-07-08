using System;
using System.Security.Cryptography;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Builds the process-wide licensing stack (store, fingerprint, clock, verifier, backend,
/// manager, gate) and owns the cached <see cref="LicenseGate"/>. Best-effort, mirroring
/// TelemetryBootstrap: any failure leaves <see cref="Gate"/> null, which the router treats
/// as "no gating" — licensing must never affect Revit startup. In dev the gate is
/// transparent (always Active). Hard enforcement arrives with the real backend in Fase 2.
/// </summary>
internal static class LicenseBootstrap
{
    // Fase 1 backend keypair (runtime-generated). In Fase 2 the client keeps ONLY the
    // public half of the real backend key; the private half never ships. Kept static so
    // the whole client path (activate -> verify -> gate) works end-to-end for dev/smoke.
    private static readonly RSA _fakeKey = RSA.Create(2048);

    /// <summary>Embedded PUBLIC key parameters. static readonly (fix #16) — a runtime
    /// keypair is not a compile-time constant. Fase 1 placeholder: replace with the real
    /// backend RSA-2048 public key in Fase 2.</summary>
    public static readonly RSAParameters EmbeddedPublicKey = _fakeKey.ExportParameters(false);

    public static LicenseGate? Gate { get; private set; }
    public static LicenseManager? Manager { get; private set; }
    public static ILicenseBackend? Backend { get; private set; }
    public static IFingerprintProvider? Fingerprint { get; private set; }

    public static void Init(CortexEnvironment env)
    {
        try
        {
            if (env.IsDev)
            {
                // Dev: transparent gate, no token, no store, no fingerprint, no backend. D4.
                Gate = new LicenseGate(() => LicenseState.Active, isDev: true);
                return;
            }

            var storePath = System.IO.Path.Combine(env.RootFolder, "license.json");
            var store = new FileLicenseStore(storePath);
            var fingerprint = new WindowsFingerprintProvider();
            var clock = new AntiRollbackClock(
                () => DateTime.UtcNow,
                new RegistryHighWaterMarkStore(),
                new ProgramDataHighWaterMarkStore());
            var verifier = new LicenseTokenVerifier(EmbeddedPublicKey.Modulus!, EmbeddedPublicKey.Exponent!);
            var backend = new FakeLicenseBackend(_fakeKey);
            var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
            manager.Refresh();

            Gate = new LicenseGate(() => manager.State, isDev: false);
            Manager = manager;
            Fingerprint = fingerprint;
            Backend = backend;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] License init failed: {ex.Message}");
            Gate = null;
            Manager = null;
            Backend = null;
            Fingerprint = null;
        }
    }
}
