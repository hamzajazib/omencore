# OmenCore v4.3.1

**Release Date:** TBD — rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-09-09, two days after v4.3.0 shipped.
**Type:** Patch release. Field-report fixes from GitHub issues opened after v4.3.0 (#190, #191)
plus a follow-up on a pending field-confirmation request (#186), alongside the first step of the
long-flagged `MainViewModel` decomposition.
**Base Version:** v4.3.0
**Tracking doc:** `docs/ROADMAP_v4.3.1.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

---

## Added

### Architecture: MainViewModel Decomposition, Step 1

`MainViewModel.cs` (6,275 lines) has been flagged across several cycles as the thing that makes
everything else expensive to change. First step at decomposing it: deleted a confirmed-dead
~200-line Corsair/Logitech/Macro RGB cluster (duplicated, more completely, by the existing
`LightingViewModel`, with zero UI bindings anywhere pointing at it), and extracted the
update-checking/installing cluster (~250 lines) into a new `UpdateViewModel`, wired in as an
eagerly-constructed `MainViewModel.Update` property. Also deleted `MacroService.cs`, itself fully
dead code. Net: `MainViewModel.cs` 6,275 → 5,636 lines. Pure structural refactor, no fan/EC/thermal
write path touched. 8 new tests; full suite 1423/1423. See the roadmap for the full trace and why
the GPU power-limit/adapter-clamp cluster was deliberately left for its own follow-up pass.

---

## Fixed

### The Entire "Notifications" Settings Section Did Nothing

Reported indirectly via [#191](https://github.com/theantipopau/omencore/issues/191) ("high
temperature warnings even with Extreme fan preset active"). Every toggle in Settings →
Notifications — the master switch, Game profile notifications, Mode change notifications, and
Temperature warnings — saved correctly but was never applied to the app's actual notification
service, at startup or after being changed. Turning any of them off had no effect, even after
restarting the app. `MainViewModel` now applies these settings to the live service at startup, and
immediately whenever one is changed in Settings — no restart required. 1 new test.

Separately: the "High Temperature Warning" toast in that report is a display-only notification
(`ThermalMonitoringService`, default 85°C) and is completely independent from the fan-boosting
thermal-protection logic that actually reacts to heat (`FanService`, default 90°C ramp / 95°C
emergency) — the fan behavior itself was not affected by this bug, and turning off "Temperature
warnings" (now that it actually works) does not disable that safety protection.

---

## Investigated, Not Yet Actioned

- **[#186](https://github.com/theantipopau/omencore/issues/186)** — NVML GPU telemetry fix from v4.3.0. Reporter confirmed they'll test now that a v4.3.0 build is published; awaiting their results.
- **[#190](https://github.com/theantipopau/omencore/issues/190)** — second independent report on board `8E5E`, already given a conservative entry in v4.3.0 from a different reporter's issue (#178). No problem described yet; asked for specifics or a Guided Fan Verification run to help fully confirm the entry.
- **[#142](https://github.com/theantipopau/omencore/issues/142)** — new field data on an unconfirmed 2026 flagship board (`8E9A`, HyperX OMEN MAX, RTX 5090): fan control "hit or miss," RGB limited to static red only, CPU/GPU power not scaling together under combined load. Not enough yet for a database entry — asked for a diagnostics export, physical RGB-zone confirmation, and an OMEN Gaming Hub baseline comparison for the power question.
- **[PR #147](https://github.com/theantipopau/omencore/pull/147)** — reviewed in full before considering a merge. The log-buffer `StringBuilder` change is correct and worth keeping, but two bugs found in the other two changes: the tray-icon change-detection cache never actually populates in the default configuration (so the optimization never engages for most users), and the dashboard uptime timer can never restart once paused once (a hard freeze of `SessionUptime`/`LastSampleAge` for the rest of the session). Posted a specific review comment; not merged as-is.
- **Board `8C9C`** (HP OMEN, AMD Ryzen 7 8845HS + RTX 4070) — not yet in the model database, resolving via Family fallback. Waiting on a fuller diagnostics export before adding an entry.
- **`ThermalMonitoringService`'s 85°C default CPU/GPU warning threshold** — arguably low relative to `FanService`'s own 90°C ramp-start point and its documented "85°C is normal" conclusion. Not changed on the strength of one report; see roadmap.
- **The GPU power-limit/adapter-clamp cluster in `MainViewModel`** (~800 lines) — scoped as a decomposition follow-up, deliberately not attempted alongside the smaller `UpdateViewModel` extraction above; see roadmap.

---
