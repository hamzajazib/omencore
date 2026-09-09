# OmenCore v4.3.1

**Release Date:** TBD — rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-09-09, two days after v4.3.0 shipped.
**Type:** Patch release. Field-report fixes from GitHub issues opened after v4.3.0 (#190, #191)
plus a follow-up on a pending field-confirmation request (#186), alongside the first two steps of
the long-flagged `MainViewModel` decomposition.
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

### Architecture: MainViewModel Decomposition, Step 2

The follow-up from step 1: extracted the GPU power-limit / adapter power-override clamp cluster
(~970 lines — reading the GPU's actual power limit, the power-adapter verdict/explanation, the
adapter override that restarts the GPU driver for a fresh verdict, the related AMD CPU power
clamp, and the automatic clamp-lift watcher) into a new `GpuClampViewModel`, wired in as an
eagerly-constructed `MainViewModel.GpuClamp` property. `DiagnosticsView`'s Power Adapter panel
bindings and its two code-behind hard-casts were repointed one level deeper rather than retargeting
the whole page. Also fixed a pre-existing bug found while mapping the cluster: three event
subscriptions were never unsubscribed anywhere in `Dispose()` — fixed as part of this work, not
left for later. Net: `MainViewModel.cs` 5,636 → 4,665 lines (6,275 → 4,665 combined across both
steps, -26%). Pure structural refactor plus a lifecycle-cleanup fix, no fan/EC/thermal write
*behavior* touched. 2 new tests, 6 migrated; full suite 1425/1425.

---

## Fixed

### Custom Settings Silently Reverting After Restart — A Real Config-Persistence Bug

Two independent users (Discord, board `8BAD`; a comment on
[#191](https://github.com/theantipopau/omencore/issues/191), board `8C9C`) reported saved settings
— a custom fan curve, a custom AMD CPU Power Limit — vanishing after doing something unrelated
elsewhere in the app, "as if it had never been saved." Traced to `ConfigurationService` handing out
a brand-new, detached copy of the config on every load instead of a shared instance — at least
three parts of the app (Settings, System Control/tuning, the main window group) each held their own
stale snapshot, and whichever saved last silently overwrote every field a *different* part had just
changed. Fixed by making config loads merge onto one shared, always-current object instead of
forking a new one each time — no other file needed to change. 4 new tests, including a direct
regression test verified to fail on the old code and pass on the fix. 1430/1430 tests.

### Auto-Update's SHA256 Check Never Actually Matched Our Own Release Notes

Reported via [#192](https://github.com/theantipopau/omencore/issues/192): checking for updates
showed "missing SHA256 in release notes" and refused to auto-install, even though the hash was
genuinely present. Confirmed by fetching the actual published v4.3.0 release body: the hash is
there as a markdown table (`| Artifact | SHA256 |`), but the extraction regex's separator pattern
only accounted for colons and whitespace, not a table cell's `` ` | ` `` boundary — so it silently
matched nothing against every release since the table format was introduced, and the updater
always fell back to manual-download-only regardless of content. Fixed the regex; replaced a test
that claimed to cover this but never actually invoked the method with two that do, including one
against the real three-artifact release-notes table. Doesn't retroactively fix the 4.2.0 → 4.3.0
upgrade that prompted the report (the fix ships in the version after the one being checked from),
but auto-update should work correctly from 4.3.0 onward.

### Board `8BAD` Misnamed a Real 17" Owner's Laptop as "OMEN 15"

A Discord report (OMEN 17 CK-2013nl) showed the app correctly resolving hardware via Exact
ProductId but displaying "OMEN 15 (2021) Intel" — the capability database's `ModelName` never
picked up the "15/17" shared-chassis naming `KeyboardModelDatabase`'s own entry for the same
ProductId already used. Renamed to match; no capability flags changed, and the entry was already
`UserVerified`.

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
- **[#192](https://github.com/theantipopau/omencore/issues/192)** — the SmartScreen/Error 4551 install block (separate from the SHA256 bug fixed above) is likely a Windows AppLocker/CodeIntegrity policy or third-party AV reacting to the unsigned installer, not something in the installer script itself. Asked the reporter to check Event Viewer and try the portable ZIP as a workaround.
- **[#193](https://github.com/theantipopau/omencore/issues/193)** — OMEN key produces no response on an HP OMEN 16-am0000. The attached log shows the v4.3.0/#187 WMI watcher registering cleanly with no errors, but zero further activity across a 90-minute session — inconclusive from an INFO-level log alone. Asked for a diagnostics export's `LastOmenKeyCandidate` field and whether other global hotkeys work, to isolate whether this is OMEN-key-specific.

---
