# OmenCore v4.4.0

**Release Date:** TBD — just started, nothing shipped yet. Rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-09-16, one day after v4.3.1 shipped.
**Type:** TBD — currently two field reports, one a straightforward model-database addition and one
a real, still-open fan/thermal-telemetry investigation. Scope will grow from here the same way past
cycles have; see the roadmap for full detail as it develops.
**Base Version:** v4.3.1
**Tracking doc:** `docs/ROADMAP_v4.4.0.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

---

## Added

### Diagnostics Now Capture All Four HP WMI Temperature Sensor Indices

Toward resolving the `0x23` sensor-index question below with real evidence instead of guessing:
every diagnostics export now includes `wmi-temperature-sensors.txt`, a read-only probe of all four
documented indices for HP's WMI temperature command (`0x23`), labeled against both the IR/Ambient/
PCH/VR mapping two independent research efforts found and the CPU/GPU indices OmenCore itself uses.
Writes nothing to the firmware; exists purely so a future report can show what every index actually
returns on that board.

---

## Fixed

### A Frozen ACPI Thermal Zone Could Win CPU-Temperature Authority Back Forever, Causing an Unbounded Flip-Flop

[#198](https://github.com/theantipopau/omencore/issues/198) reported wildly fluctuating CPU
temperatures (~40°C to ~95°C) and fans stuck at max not responding to profile switches, on a Victus
16-r0xxx (board `8BBE`). Traced through the actual session log rather than guessing: the board's
ACPI Thermal Zone was pinned at a stuck 27.9°C for the entire session while LibreHardwareMonitor
tracked real, load-correlated readings from 56-95°C. `WmiBiosMonitor` already had a "frozen sensor"
detector for exactly this, but once it fired, the resulting recovery bypass disabled outlier
rejection *entirely* — with no check that a new reading had actually changed from the one that
triggered the freeze warning. The same stuck 27.9°C sailed through every poll, won CPU-temperature
authority, lost it a couple of readings later to a separate WMI/fallback-mismatch check, and won it
right back the very next poll — an unbounded flip-flop between a frozen wrong value and a real one.
Since thermal protection reacts to whichever value is briefly authoritative, this is what read to
the user as both "wildly fluctuating temperature" and fan behavior that wouldn't settle down.

Fixed by extracting the acceptance decision into a pure, testable `ShouldAcceptAcpiCpuReading`
method: the recovery bypass now only applies to a reading that has genuinely moved since the one
that triggered the freeze warning — a repeat of the exact stuck value is held to the normal outlier
check like any other tick, so a proven-frozen zone can never re-win authority by repeating the
value that proved it frozen. 5 new tests, including one confirmed to fail against the old logic
and pass against the fix. Full suite green.

**Not fully closed.** This fixes the flip-flop mechanism with high confidence from real log
evidence, but doesn't explain *why* this board's primary WMI BIOS temperature path was rejected in
the first place, forcing the fallback chain down to LHM and then ACPI — that deeper question (see
roadmap) is still open, and this fix hasn't been confirmed on the reporter's actual hardware yet.
Treat as implemented-pending-confirmation.

### Board `8DD2` (HP Victus 15-fb3xxx) Given an Exact Database Entry

[#197](https://github.com/theantipopau/omencore/issues/197) confirmed the exact ProductId that an
earlier pattern-matched entry (`#148`) had been waiting on. Same flags, inherited verbatim — this
is an identity-confidence upgrade (exact match instead of WMI-name-pattern match), not a widened
capability claim. Reporter's "no RGB keyboard" observation matches `#148`'s own report. 1 new test.

### Temperature-Warning Toast Now Says It's Informational, Right Where the Confusion Happens

Two independent reports ([#191](https://github.com/theantipopau/omencore/issues/191),
[#142](https://github.com/theantipopau/omencore/issues/142)) showed the same real confusion: the
"High Temperature Warning" toast read as if it were tied to fan behavior or a BIOS TCC/thermal
limit. It's neither — it's a separate, informational-only threshold with no connection to
`FanService`'s real thermal-protection ramp. Both reports got the same explanation in a GitHub
reply after the fact; the toast and in-app notification history now say so directly, at the moment
the confusion would actually happen, instead of only after someone asks.

### Log Buffer No Longer Rebuilds the Entire Displayed Buffer on Every Single Log Line

Picked up standalone from community [PR #147](https://github.com/theantipopau/omencore/pull/147) —
reviewed in full last cycle; its other two changes had real bugs (a tray-icon cache that never
populates, a dashboard uptime timer that can't restart once paused) and it was never merged as-is,
but this one change was correct. `MainViewModel`'s in-app log view rebuilt its whole displayed
buffer with `string.Join` on every single incoming log line; now appends incrementally to a
`StringBuilder` and only pays for a full rebuild on the rarer tick where the 200-line cap is
actually exceeded. Output is byte-identical to the old behavior in every case — a performance
change, not a behavior change. 5 new tests, extracted into a pure testable method since the
original code was wrapped in a WPF `Dispatcher.BeginInvoke` this test suite has no shim for.

---

## Investigated, Not Yet Actioned

- **[#198](https://github.com/theantipopau/omencore/issues/198) — remaining question after the fix above.** Why did this board's WMI BIOS CPU-temperature path get rejected in the first place this session, forcing the fallback chain all the way down through LHM to a broken ACPI zone? Board has no exact database entry yet (resolves via generic Family fallback; the `RequiredCpuVendor` guard correctly prevents it from inheriting the AMD-only `8C2F` profile a sibling board with the same WMI name pattern uses — verified working as intended, not a suspect here).
- **HP WMI command `0x23`'s sensor-index semantics — now backed by two independent sources, and diagnostics can finally collect the evidence.** Flagged last cycle from Ohman's decompiled OGH device-library strings (`0=IR, 1=Ambient, 2=PCH, 3=VR`). An unrelated, independently-researched community report on `#189` (board `8D87`) reverse-engineered the same firmware command and confirmed index 0 = IR used as Gaming Hub's own fan-curve input — and found no Linux ACPI zone exposes the same reading on that board. The new `wmi-temperature-sensors.txt` diagnostic (above) is the first step toward answering whether OmenCore's own CPU/GPU indices (currently 1/2, per OmenMon's convention) are right for every board this project supports, or only some — no code changed on the indices themselves yet, deliberately, pending real per-board evidence.
- **Opt-in automatic software fan-curve controller for boards where firmware Auto under-cools (`#189`).** Deferred last cycle for "a dedicated design pass"; now considerably de-risked — the reporter has since built and shared a working reference daemon (`omen-fanctl`) on top of an already-extracted factory fan-curve table and WMI payload format. Still needs OmenCore's own model-allowlist, curve-validation, and crash-safe-recovery design before any code lands.
- **Tray icon's refresh-rate menu targeting the wrong display when docked**, and **board `8E35`'s family possibly not applying any power-limit change on a Performance-mode switch** — both carried forward from v4.3.1, still blocked on hardware/reporter evidence neither cycle has had.

---
