# OmenCore v4.4.0 Roadmap

**Status:** In progress. Opened 2026-09-16, one day after v4.3.1 shipped.
**Base version:** v4.3.1
**Predecessor doc:** `docs/ROADMAP_v4.3.1.md` — carried the 4.3.0 → 4.3.1 cycle. That document is now historical record.

---

## Why This Cycle Exists

v4.3.1 shipped 2026-09-15. The next day, two new GitHub issues came in: a routine model-support
request (#197) and a field report (#198) that looked routine on the surface — "Model Verification"
in the title — but whose body described real, safety-adjacent fan/thermal behavior (wild
temperature swings, fans stuck at max, suspected bad RPM readback). Read the actual diagnostics
export rather than triaging from the title alone, per this project's standing discipline, and it
turned into the most substantial open item of the two by a wide margin — see below.

---

## Open Investigations

### #198 — Victus 16-r0xxx (board `8BBE`, Intel i7-13700H + RTX 4060): CPU temperature source unreliability, suspected root cause of both reported symptoms

**Report.** jblanc55: "Temperature reports fluctuate wildly, going from ~40c to ~95c and triggering
a thermal emergency/max fans event. Often the fans remain at max despite temps dropping back to a
safe range, and won't drop back down despite me trying to switch profiles and fan modes (I don't
think it's getting a correct RPM reading, either.) ... usually corrects itself after a few
minutes." Reporter switched from OGH and says the behavior has gotten *worse* over a couple weeks
of using OmenCore, not better — worth keeping in mind, though nothing in the evidence below points
at anything OmenCore actively regressed; more likely a pre-existing board quirk OGH's own thermal
management was masking or handling differently.

**Identity, checked first.** `identity-resolution-trace.txt` confirms board `8BBE` resolves to
`Unknown Victus Model (FAMILY_VICTUS)` via Family fallback, Low confidence — correctly *not* the
AMD-only `8C2F` profile that a sibling board with the same `16-r0` WMI name pattern uses. That
guard (`RequiredCpuVendor = AMD` on the `8C2F` entry, added after
[#172](https://github.com/theantipopau/omencore/issues/172), the *other* open report on this exact
board) is confirmed working as designed here — this Intel machine is not silently inheriting a
Ryzen capability profile. Ruled out, not the bug.

**What the session log actually shows** (`OmenCore_20260915_134957.log`, all times local
`-06:00`), reconstructed as one coherent sequence rather than read as isolated lines:

```
14:07:45.48  WARN  CPU temp fallback timed out after 500ms — disabling fallback for 30s
14:07:47.59  WARN  CPU thermal authority switched: LHM Fallback -> ACPI Thermal Zone
14:07:51.88  WARN  🥶 CPU temperature appears frozen at 27.9°C for 699 readings
                    (load=8%, power=28.5W, load swing during hold=36%)
14:07:52.37  INFO  ✓ Fan max mode: enabled
14:07:52.37  WARN  External fan reset suspected - Max mode re-applied after sustained drop
                    (levels=25/28 floor=50, rpm=n/a)
14:07:59.39  INFO  ✓ Temps normalized (38°C) - thermal protection released
14:07:59.39  INFO  Restoring fan control to BIOS auto mode ... MAX mode reset sequence completed
```

Reading this as cause-and-effect rather than a list of unrelated warnings: LibreHardwareMonitor
(the CPU-temperature fallback source) timed out and got disabled for 30s, OmenCore's thermal
authority selector switched to ACPI Thermal Zone as the next source in line — and that source then
reported a **literally frozen 27.9°C for 699 consecutive polls** while CPU load swung by 36%, which
`WmiBiosMonitor` already has a dedicated detector for (the 🥶 warning) but nothing downstream
currently *acts* on that detection — the frozen value is still fed to thermal-protection logic and
the UI as if it were live. Separately, an "external fan reset suspected" recovery heuristic fired
in the same ~1-second window, based on Max-mode telemetry showing `rpm=n/a` — i.e. the same kind of
missing/unreliable readback the reporter suspected. In *this specific instance* thermal protection
released cleanly 7 seconds later and fan control was restored to Auto without incident — so this
exact log excerpt is not itself proof of the "stuck for minutes" symptom, but it is strong
circumstantial evidence for the same underlying cause: **this board's CPU-temperature read path is
not reliable, and multiple downstream systems (thermal protection, the Max-mode external-reset
heuristic, RPM-readback confidence) all make decisions that assume it is.**

**What this is not (checked, ruled out):**
- Not a capability-database identity bug — confirmed above.
- Not the `8C2F`/AMD-profile crossover bug from #172 — that guard works correctly here.
- Not (as far as one session's logs show) a case where thermal-protection itself fails to release —
  it released correctly in the captured window.

**What's still open, and what would move this forward:**
- Why does LibreHardwareMonitor time out for CPU temp on this specific board/CPU combination
  (13th Gen Intel i7-13700H)? Is this reproducible, or a one-off?
- Why does the ACPI Thermal Zone fallback report a frozen value instead of a real one here — is it
  reading a zone that genuinely doesn't track the CPU (same class of bug fixed in v4.2.0 for a
  *different* symptom shape — multi-zone boards latching onto a chassis sensor — worth checking if
  this is the same root cause resurfacing on a board with a different zone layout), or is the zone
  itself just slow/cached at the ACPI level?
  - Does WMI BIOS temperature (`HpWmiBios.GetTemperature()`, command `0x23`) work at all on this
    board? If so, it should likely be preferred over both LHM and ACPI Thermal Zone rather than
    falling through to a zone known to freeze — needs checking why the fallback chain reached ACPI
    at all instead of a working WMI path, if one exists.
  - Does the frozen-sensor detector ever have a real recovery action available (force a re-probe,
    fall through to the next source in the chain), or does it only log today? If only logging,
    that's the most likely next actionable fix.
- A reproduction of the "stuck for minutes, doesn't respond to profile switches" version of the
  symptom, ideally with diagnostics captured *during* the stuck state rather than after — the one
  session log analyzed above happened to resolve in 7 seconds.

**Requested from reporter:** a fresh diagnostics export captured *while* fans are stuck at max
(not after), and — if comfortable — a second export from a session with Intel XTU's service fully
stopped (the diagnostics already flag `ExternalController: Intel XTU` blocking MSR access for
undervolt; unrelated to this specific bug but worth ruling out as a confound on the same machine
since XTU is known to interfere with other vendor tools' hardware access on some systems).

**Not touched this cycle yet.** No code change made — this needs the additional evidence above
before any fix (a forced fallback-chain preference for WMI BIOS temp where available, and/or giving
the frozen-sensor detector a real recovery action) can be scoped safely, per this project's
evidence-gate convention for anything touching thermal-protection decision-making.

### #197 — HP Victus 15-fb3xxx (board `8DD2`, 2025 AMD)

Reported without a diagnostics export. Currently resolves via WMI-model-name-pattern match
(`15-fb3`) to the existing conservative profile documented in the database's `#148` notes — Low
confidence, no exact ProductId entry yet. Reporter states no RGB keyboard on this unit, which is
useful and specific enough to act on for a future keyboard-database entry once an exact capability
entry exists. Straightforward next step once a diagnostics export arrives: promote `8DD2` to an
exact entry inheriting the pattern-matched profile's flags (same low-risk pattern used for `8C9C`,
`8BAB`, etc. this past cycle), rather than leaving it on name-pattern matching indefinitely.

---

## Standing Rules (unchanged, carried from v4.3.1)

- **Evidence gate.** Fan/EC/thermal/OC/UV *behavior* changes need field validation before shipping.
  Architecture, performance, display-honesty, and pure-UI items do not.
- **Search every other open/closed issue mentioning the same ProductId before adding or editing a
  board entry** — mandatory since two near-misses several cycles back; still in force. (Caught
  `#172` as the sibling report on `8BBE` this way before assuming #198 was a fresh identity issue.)
- **Read the actual diagnostics/logs before triaging a report by its title.** #198's title read
  as routine "model verification"; the body and attached logs described something much more
  substantial. Same discipline that caught #191 was a real data-loss bug last cycle, not a one-off
  complaint.
- **One item at a time, verified before moving on.** Build clean, full suite green.
- **Update this document as you go.** Check items off only once verified, with a one-line note on
  what changed and which files.
