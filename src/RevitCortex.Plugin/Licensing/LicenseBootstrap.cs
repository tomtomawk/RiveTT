using System;
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
    public static LicenseGate? Gate { get; private set; }
    public static LicenseManager? Manager { get; private set; }
    public static ILicenseBackend? Backend { get; private set; }
    public static IFingerprintProvider? Fingerprint { get; private set; }

    public static void Init(CortexEnvironment env)
    {
        try
        {
#if DEBUG
            // DEBUG: real manager + DevLicenseBackend, EVEN for the dev profile. D4
            // (IsDev => transparent) is deliberately suspended in Debug so the gate can be
            // exercised live. Debug builds never ship. env.RootFolder keeps dev/prod profiles
            // separate (dev => ~/.revitcortex-dev).
            var store = new FileLicenseStore(System.IO.Path.Combine(env.RootFolder, "license.json"));
            var fingerprint = new WindowsFingerprintProvider();
            var clock = new AntiRollbackClock(
                () => DateTime.UtcNow,
                new RegistryHighWaterMarkStore(),
                new ProgramDataHighWaterMarkStore());
            var keyStore = new FileDevKeyStore(System.IO.Path.Combine(env.RootFolder, "dev-license-key.json"));
            var nodeLock = new FileDevNodeLockStore(System.IO.Path.Combine(env.RootFolder, "dev-node-lock.json"));
            var devPub = keyStore.PublicOnly();
            var verifier = new LicenseTokenVerifier(devPub.Modulus!, devPub.Exponent!);
            var backend = new DevLicenseBackend(keyStore, nodeLock);
            var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
            manager.Refresh();
            Gate = new LicenseGate(() => manager.State, isDev: false);
            Manager = manager;
            Fingerprint = fingerprint;
            Backend = backend;
#else
            // RELEASE before the real backend: fail-closed-honest. No FakeLicenseBackend (it
            // accepts any key). Gate null => NO gating => app runs full (like today's prod), but
            // WITHOUT a fake licensing authority in a production binary. Real enforcement later.
            Gate = null;
            Manager = null;
            Backend = null;
            Fingerprint = null;
#endif
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
