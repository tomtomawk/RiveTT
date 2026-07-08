# RevitCortex — Licensing client (Fase 1) — Design

**Data:** 2026-07-08
**Stato:** Design in attesa di revisione utente prima del piano d'implementazione
**Branch:** `feature/licensing-phase1` (cut da `feature/trust-cleanup-phase0`)
**Fonte:** questa spec vive nel **repo** (`docs/superpowers/specs/`) perché guida l'implementazione (regola source-of-truth in CLAUDE.md).
**Ambito:** SOLO la parte **client** della Fase 1 licensing. Il backend reale (Keygen/Stripe), l'update-gate firmato del manifest, l'offuscamento e la fattura elettronica sono FUORI SCOPE (Fase 2/3, vedi §8).

> Deriva dalla spec fondazione `2026-07-08-revitcortex-commercial-licensing-design.md` (verificata in `2026-07-08-licensing-spec-verification.md`) e ne rispetta i 4 vincoli d'integrazione con la telemetria (D1–D4).

---

## 1. Obiettivo

Introdurre nel client Revit un **entitlement layer** professionale e "calmo" (trust-first): node-lock per-seat, stati licenza, grace offline, degrado gentile a licenza scaduta — **senza mai far crashare Revit** e senza illudersi di rendere il binario incraccabile. L'autorità resta il server (Fase 2); questa fase costruisce il *client* con il backend astratto dietro interfaccia, così è interamente sviluppabile e testabile ORA (TDD) e il backend reale si collega dopo senza toccare la logica.

## 2. Vincoli fissati (da spec + decisioni utente 2026-07-08)

- **Firma token:** RSA-2048 PKCS#1 (nativo BCL su net48/net8/net10, zero dipendenze). Chiave pubblica embedded nel client.
- **Trial:** è solo uno **stato del token** emesso dal backend (`state="trial"`), il client lo tratta come gli altri (verifica firma + scadenza). Anti-abuso "un trial per user/macchina" = responsabilità backend (fuori scope).
- **Degrado gentile:** licenza non valida oltre grace → **riuso di `CortexRouter` read-only** (i tool read-only passano, le write sono bloccate). Mai crash.
- **Persistenza:** file `license.json` **separato** nel profilo attivo — MAI dentro `settings.json` (vincolo D3, classe bug v1.0.36).
- **Dev bypass:** `CortexEnvironment.Current.IsDev` → gate trasparente (sempre Active, nessun token, nessuna chiamata backend, nessun seat). Vincolo D4.
- **Telemetria separata:** `installationId` (telemetria, GUID pseudonimo) e `licenseId` restano scollegati nel client. Vincolo D1. Il license gate NON tocca il consenso telemetria (D2: telemetria resta opt-in individuale).
- **Fingerprint:** MachineGuid (registry) + 1–2 attributi robusti (BIOS/mobo via WMI dove disponibile), ogni attributo hashato SHA-256 separatamente, soglia di match applicata dal backend. Estendibile. NO MAC address (dato personale, Garante).

## 3. Architettura (split Core / Plugin)

La *logica* sta in `RevitCortex.Core` (netstandard2.0, testabile senza Revit/Windows); la *raccolta hardware* e il *wiring UI* nel `RevitCortex.Plugin` (ha registry/WMI + Revit). Motivo tecnico: Core (netstandard2.0, solo Newtonsoft) NON può leggere registry/WMI.

```
RevitCortex.Core/Licensing/
  ILicenseBackend          interfaccia verso Keygen/Stripe (astratta): Activate/Validate
  FakeLicenseBackend       impl. in-memory per test + dev (emette token RSA di test)
  LicenseToken             modello dei dati firmati (licenseId, state, expiresAt, seatLimit, fingerprintHashes[], issuedAt)
  LicenseState             enum: Active | Trial | Grace | Expired | Invalid
  LicenseTokenVerifier     verifica firma RSA-2048 + parsing del token
  LicenseManager           orchestratore: Evaluate(token, now, lastCheck) → LicenseState (fail-closed)
  ILicenseStore            persistenza astratta del token
  IFingerprintProvider     ritorna il set di attributi hashati (raccolti nel Plugin)
  ISystemClock             astrazione tempo (test deterministici + anti-rollback)

RevitCortex.Plugin/Licensing/
  WindowsFingerprintProvider  MachineGuid (registry) + BIOS/mobo (WMI), SHA-256 per attributo
  FileLicenseStore            license.json nel CortexEnvironment.Current.RootFolder (atomico)
  AntiRollbackClock           ISystemClock + high-water mark ridondato (HKCU + ProgramData)
  LicenseGate                 LicenseState → decisione (allow / read-only / block)

RevitCortex.Plugin/CortexRouter   guard clause additiva in Route() + riuso IsToolReadOnly
RevitCortex.Plugin/RevitCortexApp LicenseBootstrap.Init in OnStartup, gate passato al router
RevitCortex.Plugin/UI             finestra "License & Account" minima
```

Ogni unità ha scopo unico e interfaccia netta. `LicenseManager` (il cervello) si testa al 100% in Core con `FakeLicenseBackend` + clock finto + fingerprint finto.

## 4. Macchina a stati (`LicenseManager.Evaluate`)

```
Evaluate(token, now, lastOnlineCheck):
  1. nessun token in store                          → Invalid   (mai attivato)
  2. firma RSA non valida / token manomesso         → Invalid   (fail-closed)
  3. fingerprint locale non ⊇ quello del token      → Invalid   (macchina diversa / clonato)
  4. state=="trial"  e now ≤ expiresAt              → Trial
  5. state=="active" e now ≤ expiresAt              → Active
  6. now > expiresAt e (now − lastOnlineCheck) ≤ 10gg → Grace   (lease offline valido)
  7. now > expiresAt e oltre i 10gg                 → Expired
  8. clock rollback (now < highWaterMark − ~1h)     → grace revocato, forza Expired finché non c'è check online
```

| Stato | Tool consentiti | UI |
|---|---|---|
| Active / Trial | tutti | "attiva/trial, scade il …" |
| Grace | tutti (lease offline valido) | "licenza offline valida fino a GG; connettiti per rinnovare" |
| Expired / Invalid | solo read-only | messaggio chiaro + link rinnovo; write → Fail(LicenseExpired) |

**Fail-closed sulla validità, fail-open entro il grace**: firma rotta/manomissione → subito Invalid; scadenza recente entro 10gg → Grace (lavora ancora, non punitivo). Direzioni del "fail" deliberatamente opposte.
**Anti-rollback (punto 8):** high-water mark = massimo istante mai visto, salvato ridondato in HKCU + ProgramData; se l'orologio torna indietro oltre tolleranza (~1h per drift NTP/DST) il grace è revocato. Si prende il max dei due store; MAI HKLM (privilegi + trigger AV).
**IsDev:** bypassa tutto → sempre Active.

## 5. Persistenza — `license.json` (vincolo D3)

File separato nel profilo attivo: prod `~/.revitcortex/license.json`, dev `~/.revitcortex-dev/license.json`. MAI in `settings.json` (che è merge-write della telemetria → corruzione = classe bug v1.0.36).

```json
{
  "token": "<base64 del token firmato dal backend>",
  "lastOnlineCheckUtc": "2026-07-08T10:00:00Z",
  "highWaterMarkUtc": "2026-07-08T10:00:00Z"
}
```

Il **token** contiene i dati firmati (licenseId, state, expiresAt, seatLimit, fingerprintHashes[], issuedAt); il client non si fida di nulla fuori dalla firma. `lastOnlineCheckUtc`/`highWaterMarkUtc` sono metadati locali per il grace, non fidati per la validità: alterarli può solo *accorciare* il grace (grazie all'anti-rollback), non estenderlo.

`FileLicenseStore`: `Load()`→`LicenseToken?` (null se assente/illeggibile, mai crash); `Save()` atomico (write-temp + rename); I/O sempre in try/catch; high-water mark scritto anche in `HKCU\Software\RevitCortex`, si prende il massimo.

## 6. Gate nel router (additivo)

`LicenseGate` traduce lo stato in decisione; `Route()` la consulta come guard clause accanto a quelle esistenti:

```
Route(toolName, input):
  1. tool esiste?          → no: Fail(InvalidInput)
  2. tool disabilitato?    → sì: Fail(InvalidInput)
  3. [NUOVO] license gate:
       stato = _licenseGate?.CurrentState()   // cache in memoria, valutato all'avvio + refresh; null gate = no gating
       se stato ∈ {Expired, Invalid} AND !IsToolReadOnly(toolName):
           → Fail(PermissionDenied, "License expired…", suggestion rinnovo)
  4. RequiresDocument / IsDynamic / ReadOnlyMode …  (guard esistenti, invariate)
  5. dispatch normale
```

**Refresh dello stato:** `LicenseGate.CurrentState()` restituisce uno stato **cache-ato in memoria**, calcolato una volta all'avvio (`LicenseBootstrap.Init`) e ri-valutato (a) su azione esplicita dell'utente nella finestra License (Attiva/Aggiorna) e (b) quando il backend reale arriverà, al refresh online periodico (Fase 2). In Fase 1 NON si ri-valuta a ogni chiamata di `Route()` (sarebbe I/O su ogni tool); una licenza che scade *durante* una sessione già avviata ha effetto al prossimo avvio o al prossimo Aggiorna — accettabile per Fase 1, coerente col comportamento "mid-session takes effect next start" già scelto per il consenso telemetria.

**Guard dedicata (non solo `ReadOnlyMode=true`)** per avere un `errorCode`/messaggio dedicato (`LicenseExpired` + link) e non confondere il read-only *scelto dall'utente* con quello *imposto dalla licenza*. **Riusa `IsToolReadOnly`** — nessuna nuova classificazione. `_licenseGate` è param opzionale nullable del costruttore (null = comportamento odierno → retro-compatibile con i test esistenti). Se `CurrentState()` lancia (non dovrebbe, è wrappato) il gate non blocca entro il grace assunto. Il **support-report** è un `IExternalCommand` (non passa da Route) → sempre generabile. Nulla del dispatch/telemetria/guard esistenti cambia.

## 7. Wiring, UI, testing

**Bootstrap** (`RevitCortexApp.OnStartup`, come la telemetria): `LicenseBootstrap.Init(env)` costruisce lo stack; in dev gate trasparente; router riceve `licenseGate:` (nullable). Init best-effort: se fallisce, gate null → nessun blocco (enforcement duro arriva col backend reale in Fase 2).

**UI minima:** voce "License & Account" (stile SettingsWindow): stato, scadenza, giorni grace residui, licenseId troncato, pulsanti "Attiva" (inserisci chiave → `ILicenseBackend.Activate`) e "Aggiorna". In Fase 1 `FakeLicenseBackend` emette un token firmato di test. Messaggi degrado localizzati IT/EN (spec §9.2).

**Testing TDD (Core, con fake):**
- `LicenseManager.Evaluate` — tutte le transizioni (8 punti) con clock finto.
- `LicenseTokenVerifier` — valido passa; firma manomessa/troncata/chiave errata → Invalid (coppia RSA di test nei fixture).
- `LicenseGate` — Active/Trial/Grace→allow; Expired/Invalid + write→block, + read-only→allow; IsDev→allow.
- `FileLicenseStore` — round-trip; assente/corrotto→null (mai eccezione); scrittura atomica.
- Router — guard test: write bloccata a Expired, read-only passa, gate null = invariato.
- `WindowsFingerprintProvider` — non unit-testabile a fondo (HW reale) → contratto leggero + skip stile `[RequiresRevitApiFact]`.

Build R25 + R24 obbligatorie a ogni step.

## 8. Fuori scope Fase 1 (esplicito)

- Backend reale Keygen, webhook Stripe, endpoint cloud, e-fattura A-Cube → Fase 2/3.
- Update-gate firmato del manifest (Ed25519 su `latest.json` + URL pre-firmato entitlement-gated) → è la parte (a) della Fase 1 della spec fondazione, ma questo giro è "solo client node-lock/stati/gate". Task separato successivo (l'`UpdateChecker` oggi verifica solo SHA-256, non una firma — confermato).
- Offuscamento (Obfuscar leggero) → separato.
- Fingerprint completo 4–6 attributi → si parte da MachineGuid + 1–2, estendibile.

## 9. Antivirus & firewall

- **Firewall:** la parte client Fase 1 è quasi tutta **locale** (fingerprint, verifica RSA, stati, file) → firewall irrilevante. Il **grace offline 10gg** è la rete-safety-net se la validazione è bloccata. Le chiamate al backend reale (Fase 2) sono HTTPS/443 in uscita **da Revit.exe** (il plugin gira nel processo Revit → l'AV/firewall vede "Revit che si connette", non un processo nuovo), dominio da documentare per allowlist aziendali. Il bridge TCP MCP è loopback puro (127.0.0.1) — i firewall normali non lo toccano; solo EDR molto aggressivi ispezionano il loopback (rischio preesistente, non introdotto qui).
- **Antivirus:**
  - Lettura ID hardware (WMI) è un pattern che l'euristica AV/EDR può associare a fingerprinting malevolo → **preferire MachineGuid via registry** (read-only, benigno, lo leggono mille software); WMI con cautela e **omettibile** (la soglia server-side tollera l'assenza). Altra ragione per la scelta A del fingerprint.
  - Anti-rollback: scritture solo in **HKCU** + cartella ProgramData nostra; **MAI HKLM o percorsi di sistema** (privilegi elevati + trigger AV).
  - Le mitigazioni AV vere sono a valle: **code signing** dei binari (Fase 3) contro SmartScreen/quarantena, e **niente offuscamento pesante/anti-tamper** (classico trigger di falsi positivi — già escluso da spec e verifica). Obfuscar leggero non peggiora la reputazione.

## 10. Criteri di accettazione

- Lo stato licenza è calcolato deterministicamente e testato per tutte le 8 transizioni.
- Token con firma non valida/manomessa → Invalid (nessun falso "valido").
- Oltre il grace, i tool write sono bloccati con messaggio chiaro; i read-only e il support-report restano disponibili; Revit non crasha mai per il licensing.
- `license.json` è separato da `settings.json`; nessun blind-rewrite; assente/corrotto non crasha.
- In dev (`IsDev`) il gate è trasparente: nessun token, nessun blocco, nessuna chiamata backend.
- `installationId` (telemetria) e `licenseId` restano scollegati; il consenso telemetria non è toccato.
- Build R23–R27 verdi; suite verde.
