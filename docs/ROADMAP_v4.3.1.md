# OmenCore v4.3.1 Roadmap

**Status:** In progress. Opened 2026-09-09, two days after v4.3.0 shipped.
**Base version:** v4.3.0
**Predecessor doc:** `docs/ROADMAP_v4.3.0.md` — carried the 4.2.0 → 4.3.0 cycle. That document is now historical record.

---

## Why This Cycle Exists

v4.3.0 shipped 2026-09-07. Two days later, two new GitHub issues came in (#190, #191) and a prior
issue (#186, NVML GPU telemetry) got a promising but still-pending field-confirmation reply from
its original reporter now that a build exists to test against. This cycle starts as a small
patch pass on that batch, following the same "read the actual report, verify against actual
code, fix what's real" discipline as every prior field-report pass.

One item turned out bigger than its originating report, same shape as this project's last few
cycles: triaging #191's "high temperature warnings despite Extreme fan preset" complaint led to
tracing the entire Settings → Notifications toggle chain and finding it was completely
disconnected from the running app — see below.

---

## Done

### Custom Settings Silently Reverting After Restart — Multiple Stale `AppConfig` Snapshots Clobbering Each Other's Saves

**Report:** Two independent users, same underlying symptom. Discord (AlthegarOP, board `8BAD`,
HP OMEN 17 CK-2013nl): "If I create and save a custom fan curve, the profile is no longer displayed
after closing and reopening the app... as if it had never been saved... The fan mode also resets to
AUTO after closing and reopening OmenCore." A comment on
[#191](https://github.com/theantipopau/omencore/issues/191) (RaulMARK17, board `8C9C`, different
user, different board): "the setting I showed you... Custom AMD CPU Power Limit... is never saved,
or at least, I am not really sure how to save it, every time the system is restarted or the app is
restarted, my config is lost... before I had problems with custom fan curves, they got lost too
after a restart."

**Traced, not guessed — this took a dedicated investigation pass to find.** `ConfigurationService.Load()`
deserialized `config.json` into a brand-new, detached `AppConfig` object on *every single call* and
never touched the `Config` property. Because of this, at least three independent,
never-synchronized `AppConfig` references stayed alive for the life of the process:
`ConfigurationService.Config` itself (read directly by `SystemControlViewModel` and
`SettingsViewModel`), `MainViewModel._config` (a *second*, separately-`Load()`-ed object, shared by
reference into `GpuClampViewModel` and `UpdateViewModel`), and `FanControlViewModel`'s
save-time-only fresh loads (accidentally self-healing, since it always re-reads immediately before
writing — the other two paths are not). Any save through the first two paths did a **whole-file
overwrite** using whatever stale snapshot that path had been holding since it was last read,
silently reverting every field a *different* path had changed in the meantime. This explains why
the symptom looked feature-agnostic to both reporters: it doesn't matter which setting you save
first — any subsequent, completely unrelated save from a different tab clobbers it.

**Fix.** `ConfigurationService.Load()` now merges freshly-deserialized disk data onto the *existing*
`Config` object in place (via reflection over `AppConfig`'s 83 top-level properties — far too many
to hand-list and keep in sync as the model grows) instead of manufacturing a new detached object,
and always returns that same shared reference. Every current and future holder of a `Load()`/`Config`
reference converges onto one always-current object automatically — **zero changes needed at any of
the ~15+ individual read/save call sites** across `SettingsViewModel`, `SystemControlViewModel`,
`MainViewModel`, `GpuClampViewModel`, `UpdateViewModel`, or `FanControlViewModel`. A lock guards the
merge and the serialize step in `Save()`, since `PowerAutomationService`'s
`SystemEvents.PowerModeChanged` handler genuinely runs on a background thread and calls `Load()` —
a real concurrent-writer case, not a hypothetical one.

**Confirmed dead code, left alone:** `ConfigurationService.Replace()`/`ResetToDefaults()` both
reassign `Config` to a new reference (which would reintroduce the orphaned-reference problem) —
`Replace()` has zero production callers (only used by three test files as a setup helper);
`ResetToDefaults()` has zero callers at all. Not touched in this pass; low-priority follow-up if
either is ever wired up for real.

**Tests.** Four new tests in `ConfigurationServiceTests.cs`, including one built as a direct
regression test for the reported pattern (two independently-held config snapshots each saving an
unrelated field) — manually verified by reverting the fix and re-running: it fails on the old code
(the second save wipes the first's field back to null, reproducing the exact "vanishes after doing
something unrelated" report) and passes on the fixed code. Full suite: 1430/1430.

**Not a field-validation item.** Pure application-state/persistence logic — no fan/EC/thermal write
path touched, and the fix makes existing writes reach disk correctly rather than changing what gets
written.

**Reply posted** on #191. No direct reply channel for the Discord report; summarized for the user
in conversation.

### GitHub #192 Follow-Up: Auto-Update's SHA256 Extraction Never Actually Matched Our Own Release Notes

**Report:** [#192](https://github.com/theantipopau/omencore/issues/192) — updating 4.2.0 → 4.3.0
showed "Update requires manual download (missing SHA256 in release notes)" in-app, forcing a
manual download that then hit a separate Windows SmartScreen/Error 4551 block during install.

**Confirmed, not assumed.** Fetched the actual published v4.3.0 GitHub Release body and traced
`AutoUpdateService.ExtractHashForAsset` against it line by line. The release notes genuinely
contain the hash — as a two-column markdown table (`| Artifact | SHA256 |`, one row per file, both
filename and hash wrapped in backticks) — but the extraction regex's separator character class
(`[:\s]+`, then `[:\s*` + backtick`]+` for the fallback) only accounts for colons and whitespace.
A markdown table cell boundary is `` ` | ` `` — backtick, pipe, space — none of which that class
matches, so every regex path failed silently against every release since the table format was
introduced, and the updater always fell back to "missing SHA256" regardless of what was actually
in the notes. The existing test claiming to cover this (`ExtractHashFromBody_WithValidHash_ReturnsHash`)
never actually invoked the method at all — it only asserted a substring existed in a hand-written
string, so this had no real coverage.

**Fix.** Extended both regexes' separator character classes to include backtick and pipe, so they
match through a markdown table cell boundary. Replaced the non-test with two real ones that invoke
`ExtractHashForAsset` via reflection against the plain-label format and, more importantly, against
the *exact* real v4.3.0 release-notes table (three artifacts, confirming the per-asset match picks
the right row's hash and not just the first one in the table).

**Not a field-validation item** — pure text-parsing logic, no hardware write path touched, fully
provable from the real release body without needing a reporter's hardware.

**Doesn't retroactively fix #192's own upgrade** — the fix ships in the version *after* the one
being checked from, so anyone still on 4.2.0 or earlier will need one more manual download to get
onto a release with the fix; auto-update should work correctly from 4.3.0 onward. The SmartScreen
Error 4551 block is a separate, likely Windows-policy-or-AV issue — not something in the installer
script itself — flagged for the reporter to check Event Viewer's AppLocker/CodeIntegrity logs;
recorded as still open below.

### GitHub Discord Report — Board `8BAD`'s Capability Entry Misnamed a Real 17" Owner's Laptop as "OMEN 15"

**Report:** Discord (AlthegarOP) — HP OMEN 17 CK-2013nl, resolving via Exact ProductId (`8BAD`,
High confidence) as "OMEN 15 (2021) Intel." The capability profile itself resolves correctly and
is already `UserVerified = true`; only the display name was wrong for a 17" owner.

**Confirmed, not guessed.** `KeyboardModelDatabase.cs`'s own `8BAD` entry already names this board
"OMEN 15/17 (2021-2023) Intel" — the keyboard database already knew this ProductId is shared
across both chassis sizes. `ModelCapabilityDatabase.cs`'s `8BAD` entry just never got the same
naming; it said only "OMEN 15 (2021) Intel." Confirmed no other open/closed issue mentions this
ProductId before editing.

**Fix.** Renamed the capability entry's `ModelName` to "OMEN 15/17 (2021) Intel" to match the
keyboard database's existing, already-correct naming. No capability flags changed.

**Not a field-validation item.** Pure display-honesty fix — the underlying capability profile
(`SupportsFanControlWmi`, `SupportsFanCurves`, `HasFourZoneRgb`) is unchanged and was already
`UserVerified`.

### Architecture: MainViewModel Decomposition, Step 1 — Dead-Code Deletion + `UpdateViewModel` Extraction

`MainViewModel.cs` (6,275 lines) has been flagged across several roadmap cycles as "increasingly
the thing that makes everything else expensive" (see `docs/ROADMAP_v4.2.0.md`'s "MainViewModel
decomposition" note) — it's where the #181 fan/performance link-cascade bug lived, and per that
same note, "extracting actual business logic and bound properties into feature-scoped ViewModels
has not started" despite several sibling sub-VMs (`FanControlViewModel`, `SystemControlViewModel`,
`LightingViewModel`, `MemoryOptimizerViewModel`, etc.) already existing.

Rather than attempting the whole decomposition at once, three parallel investigation passes mapped
the file's remaining structure to find safe, well-bounded first steps. This surfaced something
better than a typical extraction candidate: a ~200-line cluster of Corsair/Logitech/Macro
RGB-device methods turned out to be **confirmed dead code** — zero UI bindings anywhere reference
it, and it's a less-complete duplicate of what `LightingViewModel` already does with the same
underlying service instances. A follow-up planning pass produced an exact, line-verified
implementation plan for two changes, both now shipped:

1. **Deleted the dead RGB cluster** (`DiscoverCorsairDevices`, `ApplyCorsairLighting`,
   `SaveCorsairDpi`, `ApplyMacroToDevice`, `SyncCorsairWithTheme`, `StartMacroRecording`,
   `StopMacroRecording`, `SaveRecordedMacro`, `DiscoverLogitechDevices`, `ApplyLogitechColor`, plus
   ~15 backing fields/properties/commands) — pure risk-free size reduction, since nothing currently
   exercises this code path from the running app. The one real behavior worth preserving — device
   discovery populating the Lighting tab on first open, since `LightingViewModel.CorsairDevices`/
   `LogitechDevices` are live pass-throughs of the same service instances, not copies — is kept via
   a direct, corrected call in `InitializeLightingServicesAsync()`. Also deleted `MacroService.cs`
   itself, confirmed fully dead (`MacroService.PushEvent` had zero callers anywhere in the
   codebase, so a recorded macro could never contain anything regardless).
2. **Extracted the update-checking/installing cluster (~250 lines) into a new `UpdateViewModel`**,
   exposed as an eagerly-constructed `MainViewModel.Update` property — eager rather than the lazy
   pattern used for tab-scoped sub-VMs (`MemoryOptimizer`, `BloatwareManager`, etc.), since this
   cluster's bindings live in always-visible window chrome (title-bar version label, update
   banner), not inside a lazily-created tab view. `MainWindow.xaml`'s 10 binding sites repointed to
   `Update.X` accordingly. `SettingsView.xaml`'s own, unrelated `OpenReleaseNotesCommand` (a
   different command on `SettingsViewModel` that just opens the GitHub releases page) was a
   coincidental name collision, confirmed not to need any change.

**Net effect:** `MainViewModel.cs` 6,275 → 5,636 lines (-10%). Full test suite green (1423/1423, up
from 1415 — 8 new tests for `UpdateViewModel` plus one `MainViewModel` smoke assertion). Verified
the app still launches cleanly on real hardware post-change (a smoke-test launch's
`HardwareWorker.log` shows successful CPU/GPU/memory detection).

**Not a field-validation item.** Pure structural refactor — no fan/EC/thermal/OC/UV write path
touched, and the deleted cluster had no live UI surface to begin with.

**Explicitly not attempted in this pass:** the GPU power-limit/adapter-clamp cluster (~800 lines,
two hard-casts to `MainViewModel` in `DiagnosticsView.xaml.cs` code-behind) — investigated and
found much bigger and more entangled than the update cluster, with two services
(`AdapterPowerOverrideService`, `ApuPowerClampService`) that aren't currently DI-injectable.
Deserves its own dedicated pass once this smaller pattern has had time to prove out; recorded here
rather than attempted blind in the same session. **Done as a follow-up in the same cycle — see
below.**

### Architecture: MainViewModel Decomposition, Step 2 — Extract `GpuClampViewModel`

The cluster deferred above turned out to be exactly as entangled as expected once actually mapped
(three parallel Explore agents plus a Plan agent, same rigor as step 1): ~963 contiguous lines,
two hard-casts to `MainViewModel` in `DiagnosticsView.xaml.cs`, and two services
(`AdapterPowerOverrideService`, `ApuPowerClampService`) with no existing DI seam on `MainViewModel`.
Extracted anyway, following the exact investigate-then-plan-then-approve process from step 1.

**Shipped:** `GpuClampViewModel` (new file), exposed as an eagerly-constructed
`MainViewModel.GpuClamp` property — covers the GPU power-limit reading, the power-adapter
verdict/explanation, the adapter override (GPU driver restart), the AMD CPU power clamp
(STAPM/APU wattage), and the automatic clamp-lift watcher. `DiagnosticsView.xaml`'s 29 bindings and
`DiagnosticsView.xaml.cs`'s two hard-casts were repointed one level deeper (`vm.GpuClamp.X`)
rather than retargeting the whole page's `DataContext`, since the page has four other unrelated
sections that would otherwise need their bindings redone for no benefit.

**Also fixed, confirmed by the user first:** a pre-existing bug found while mapping the cluster —
three event subscriptions (`SystemSuspending`, `SystemResuming`, `PowerStateChanged`) were
subscribed in `MainViewModel`'s constructor but never unsubscribed anywhere in `Dispose()`. The
`PowerStateChanged` subscription now lives inside `GpuClampViewModel` itself (its handler did
nothing but forward into this cluster's own `RunAutomaticClampLiftAsync`); the other two stay on
`MainViewModel`, now correctly unsubscribed.

**Net effect:** `MainViewModel.cs` 5,636 → 4,665 lines (-971, on top of step 1's 6,275 → 5,636 —
combined, 6,275 → 4,665, a 26% reduction across both steps). Full test suite green (1425/1425, up
from 1423 — six existing tests migrated to reach through `vm.GpuClamp`, plus two new: an
eager-construction check and a `Dispose()`-doesn't-throw regression guard for the fixed leak).
Verified the app still launches cleanly on real hardware post-change.

**Not a field-validation item.** Pure structural refactor plus a lifecycle-cleanup fix — no
fan/EC/thermal/OC/UV write *behavior* changed, only where the existing code lives and whether its
event subscriptions get cleaned up.

**Not attempted:** any further MainViewModel decomposition beyond this cluster. Remaining content
(tray quick actions, hotkey handlers, game profiles, RGB scene application, cleanup/restore-point
flows) is left for a future pass if warranted.

### GitHub #191 Follow-Up: The Entire "Notifications" Settings Section Was Non-Functional

**Report:** [#191](https://github.com/theantipopau/omencore/issues/191) — HP OMEN Laptop, board
`8C9C` (AMD Ryzen 7 8845HS + Radeon 780M iGPU + RTX 4070 Laptop GPU), OmenCore v4.3.0. On AC power,
Balanced performance mode, Extreme fan preset active, reporter got a Windows toast "High
Temperature Warning: CPU 87°C" during light (non-demanding) use, and separately saw alerts up to
99°C with the default 95W TDP. Expectation: Extreme fan preset should keep the system cool enough
that this shouldn't happen.

**Traced, not guessed.** The 87°C alert is not the same mechanism as `FanService`'s actual
fan-boosting thermal protection (`CheckThermalProtection`, ramps fans at a configurable 90°C,
forces 100% at 95°C) — that logic never fired at 87°C, correctly, since 87°C is below its own
90°C ramp-start threshold. The toast the reporter saw comes from a completely separate,
notification-only path: `ThermalMonitoringService`, which defaults `CpuWarningThreshold` to
**85°C** (`AppConfig.ThermalMonitoringSettings.CpuWarningC`) — a temperature `FanService`'s own
v2.8.0 comments explicitly call "normal" for a gaming laptop under load, not warning-worthy. So
two independent subsystems in the same codebase disagree, by design, about what counts as "too
hot," and the more sensitive one is the one with no way to see or adjust it.

**The bigger bug found while tracing this:** every toggle in Settings → Notifications — the
master "Enable notifications" switch, Game profile notifications, Mode change notifications, and
Temperature warnings — persists correctly to `AppConfig.Monitoring` via `SettingsViewModel`'s
load/save round-trip, but **none of those four values were ever applied to the real
`NotificationService` instance**, at startup or afterward. Confirmed via a full-repo search: zero
assignments anywhere to `NotificationService.IsEnabled`, `.ShowGameNotifications`,
`.ShowModeChangeNotifications`, or `.ShowTemperatureWarnings` outside the class's own hardcoded
`= true` field initializers. A user who tried to turn off "Temperature warnings" — the exact
remedy for this report — would have found it did nothing, not even after restarting the app,
because the toggle was never wired to anything.

**Fix.** `MainViewModel`'s constructor now applies `_config.Monitoring`'s four notification flags
to `_notificationService` right after it's created (the same place `ThermalMonitoringService`'s
own config-driven thresholds are already applied a few lines below). A new public
`RefreshNotificationPreferences()` method re-applies them on demand; `SettingsViewModel`'s four
notification-toggle setters now call it immediately after `SaveSettings()`, via the same
`Application.Current?.MainWindow?.DataContext is MainViewModel` reach-through
`RefreshLinkFanState()` already uses elsewhere in that file — so toggling any of the four in
Settings takes effect immediately, not just on next launch.

**Not touched:** the 85°C vs. 90°C threshold disagreement itself. Now that the toggle actually
works, a user in this exact situation has a real way to quiet the notification without needing a
default-value judgment call made on their behalf from a single report. Whether
`ThermalMonitoringSettings.CpuWarningC`'s default should be raised (e.g., closer to
`FanService`'s own 90°C ramp-start point, so the two subsystems agree) is recorded below as a
follow-up worth a second look, not decided here — changing a default that ships silently to every
existing user needs more than one report to justify, per this project's evidence-gate discipline.

**Tests.** New `RefreshNotificationPreferences_AppliesConfigTogglesToLiveNotificationService`
(`MainViewModelTests.cs`) constructs a real `MainViewModel`, flips all four config flags to
`false` via reflection, calls the new refresh method, and asserts the live `NotificationService`
actually reflects each one — then flips back to `true` and re-asserts. Full suite: pending final
run, see changelog.

**Not a field-validation item.** Pure notification-preferences wiring — no fan/EC/thermal write
path touched. `FanService.ThermalProtectionEnabled` (the setting that actually gates whether fans
get force-boosted) was never affected by this bug and is unchanged.

**Reply posted** on #191 explaining both mechanisms, confirming the Settings fix, and asking for
a full diagnostics export (session log + `core-control-readiness.txt`) captured near a future
alert, since board `8C9C` is not yet in the model database (resolving via Family fallback, Low
confidence per the reporter's own screenshot) — worth confirming the Extreme fan preset write is
actually landing on this exact board before ruling that out as a contributing factor too.

---

### PR #147 Reviewed: One Real Fix, Two Regressions Found Before Merge

[PR #147](https://github.com/theantipopau/omencore/pull/147) ("perf(ui): reduce unnecessary
UI-thread work") targets [#133](https://github.com/theantipopau/omencore/issues/133)'s long-open
"horrible UI performance" complaint with three changes. Fetched the branch and read the full
files (not just the diff) before considering a merge, same discipline as PR #176 earlier this
project.

**The log-buffer change is correct and worth keeping.** Replacing `string.Join("\n", _logLines)`
(an O(n) rebuild every single log line) with an appended `StringBuilder` that only gets rebuilt
from scratch when the queue actually overflows its 200-line cap is a real, sound improvement.

**Two bugs found in the other two changes, both the same shape** — code that looks like it adds a
pause/resume or change-detection gate, but only actually implements the "off"/"unchanged" half:

1. `TrayIconService.UpdateTrayDisplay`'s new change-detection cache (`_lastCpuTempC` etc.) is only
   ever written inside the single narrow branch for "temp display disabled and icon already at
   base state." With `TrayTempDisplayEnabled` at its default `true`, those fields stay at their
   initial `NaN`/`-1` forever, so the new `valuesChanged` early-return is `true` on literally every
   tick in the default configuration — the optimization the PR is built around never actually
   engages for most users.
2. `DashboardViewModel.SetUptimeTimerEnabled(true)`'s "timer already stopped, start it" branch was
   left as an empty block with only a comment claiming the start call was "deferred to
   `SetUptimeTimerEnabled`" — but that comment sits inside `SetUptimeTimerEnabled` itself, and the
   actual `.Start()` call was deleted, not moved. Once the timer stops once (dashboard hidden), it
   can never restart — `SessionUptime`/`LastSampleAge` freeze for the rest of the session.

Both bugs only show up on the *second* half of a change/no-change or hide/show cycle, which is
likely why the PR's own manual-testing notes didn't catch them — a single one-directional pass
looks correct either way.

**Not merged.** Posted a specific, line-referenced review comment on the PR explaining both bugs
and what a repeat-cycle test would need to show to confirm a fix, same as the PR #176 precedent —
decision to merge is the contributor's/owner's once addressed, not made here.

---

### GitHub #142 Follow-Up: New Hardware Data on an Unresolved 2026 Flagship Board

[#142](https://github.com/theantipopau/omencore/issues/142) — HyperX OMEN MAX Gaming Laptop
16t-ah100, board `8E9A`, brand-new 2026 flagship (reported CPU "290HX Plus", RTX 5090, BIOS F.05,
300W combined platform TDP). Resolves via Family fallback only; a prior reply had asked for specs
and confirmed-working features, and the reporter came back with real (if inconsistent) usage data:
fan control "hit or miss" (sometimes ramps for no reason, sometimes doesn't ramp at all), RGB
limited to a single static red or off (no zones, no other colors), and GPU Power Boost working but
CPU power not scaling with it under combined load.

**Not enough yet to add a database entry** — the fan behavior is inconsistent even on the
reporter's own machine, which points at either a real board-specific WMI command mismatch or a
normal-but-alarming-looking reassert/thermal-authority-switching pattern; can't tell which without
an actual diagnostics export or session log from when it happens. The RGB report is a real, useful
signal on its own (suggests a narrower keyboard interface than the generic OMEN16 4-zone fallback
assumes), and the CPU/GPU power-balancing observation is very plausibly firmware-side platform
behavior on a new 300W-combined-TDP chassis, the same class of "relative boost request to shared
firmware" behavior already documented for GPU Power Boost's wattage ceiling elsewhere.

**Replied** asking for a diagnostics export or session log captured during the erratic fan
behavior, physical confirmation of the keyboard's real zone/color capability, and whether the
CPU/GPU power imbalance also shows up under OMEN Gaming Hub as a baseline comparison. Recorded here
since this is a brand-new flagship platform likely to get more reports before it's well
understood — worth checking back on rather than letting it go quiet.

---

### GitHub #186 Follow-Up: v4.3.0 Is Live, Asked Reporter to Confirm the NVML Fix

[#186](https://github.com/theantipopau/omencore/issues/186)'s reporter (RobRobM, RTX 5080 Laptop,
board `8D41`) confirmed on 2026-09-03 they'd test the NVML GPU-telemetry fix as soon as v4.3.0
was published, since the releases page still showed v4.2.0 at the time. v4.3.0 shipped 2026-09-07.
Pinged them on the issue to let them know a build now exists to test against — no code change
here, just closing the loop on a field-validation request that was blocked on our own release
timing, not on them.

**Update:** RobRobM came back with a genuinely thorough confirmation — side-by-side idle readings
against `nvidia-smi`, a 25-second sustained-load comparison tracking second-for-second, and all
three surfaces (CLI `status`, `monitor`, and the GUI) checked independently. Every item on the
fix's own checklist confirmed. Closed.

While verifying, they separately found that v4.3.0's release-notes SHA256 hashes don't match the
actual published assets (both platforms) — traced it to a likely local-build-vs-CI-rebuild
divergence in the release workflow, not the files themselves being wrong. Split into its own issue,
[#194](https://github.com/theantipopau/omencore/issues/194), since it's unrelated to NVML telemetry
and a release-process problem rather than application code — not investigated further this cycle.

---

### GitHub #190 — Second Independent Report on Board `8E5E`, Already Given a Conservative Entry From #178

[#190](https://github.com/theantipopau/omencore/issues/190) is an auto-attached "Model Identity
Summary" with no accompanying free-text description of a problem — just the app's own diagnostic
output, which already resolves this board correctly: `8E5E` (HP Victus 15-fa2303TX / C2JQ3PA),
Exact ProductId match, added in v4.3.0 from a *different* reporter's issue
([#178](https://github.com/theantipopau/omencore/issues/178)). That entry's own notes already flag
it as conservative pending further field verification — #178's Guided Fan Verification passed
only 3 of 6 tests.

Two different people independently confirming the same exact SKU is useful, but there's nothing to
act on yet since #190 doesn't describe what (if anything) is actually wrong. Replied asking what
they were expecting to see or report, and — since they're on the identical hardware #178 already
partially verified — whether they'd be willing to run the Guided Fan Verification / Max-hold test
themselves so this board's entry can move past "conservative, partially verified" with a second
independent data point.

---

### Discoverability: Fan Control Page Now Explains When a Preset Won't Survive a Restart

Flagged during the config-persistence investigation above but deliberately not bundled into that
fix: the Discord board-`8BAD` report also asked why fans aren't controlled before Windows login,
which turned out to be expected, by-design behavior — `EnableStartupHardwareRestore` defaults off,
so nothing reapplies at boot until the user opts in from Settings. That's a reasonable default, but
the Fan Control page gave no indication a saved preset/curve wouldn't survive a restart, so the
behavior read as a bug rather than an unset toggle.

Added a dismissible hint banner to the Fan Control page's Saved Presets area, gated on
`StartupRestorePolicy.IsEnabled(config, StartupRestoreCategory.Fans)` — the same composed
broad-gate-plus-per-category check the rest of the app already uses for this decision, so the
banner and the actual startup behavior can't drift apart. Text distinguishes whether the broad
"Startup Hardware Restore" toggle is off versus just the Fans category, so the user knows exactly
which Settings control to flip. Mirrors the existing `ShowFanPerformanceInfoBanner` dismissible-
banner pattern (`FanControlViewModel.cs`) exactly: a new `DismissedStartupRestoreHint` flag on
`AppConfig`, a `ShowStartupRestoreHint` gated property, and a `DismissStartupRestoreHintCommand`
that persists the dismissal. 4 new tests. 1434/1434.

### Cleanup: `MainViewModel.ReloadConfiguration()`'s Manual Field Copy Was Dead Weight

Flagged as a safe, deliberately-deferred follow-up during the config-persistence fix above:
`ReloadConfiguration()` manually copied six collection fields (`FanPresets`, `PerformanceModes`,
`SystemToggles`, `LightingProfiles`, `CorsairLightingPresets`, `MacroProfiles`) from a fresh
`Load()` result onto `_config`. Now that `Load()` merges onto the same shared object `_config`
already points at, `cfg` and `_config` are reference-equal by the time that code ran — the six
lines were self-assignment, doing nothing. Removed; `ReloadConfiguration()` now just calls
`Load()` for its merge side effect and re-hydrates the UI collections. 1 new test, asserting the
reference-equality invariant the simplification depends on. 1435/1435.

### Architecture: SystemControlViewModel Decomposition, Step 1 — Extract `SystemMaintenanceViewModel`

With the `MainViewModel` decomposition done, `SystemControlViewModel.cs` (5,610 lines) is now the
largest file in the app — bigger than `MainViewModel.cs` was before this cycle's work on it.
Mapped its full structure before touching anything: most of it (undervolt, GPU OC for both
vendors, CPU/AMD power limits, TCC offset, the tuning-safety rollback/conflict system that ties
all of those together) is tightly interleaved and genuinely hazardous to split blindly — exactly
the kind of thing this project's evidence-gate discipline exists for. Four features weren't part
of that web at all: GPU mode switching (Hybrid/Discrete/Integrated), display panel overdrive, the
OMEN Gaming Hub cleanup wizard, and manual system-restore-point creation — each with its own
exclusively-owned service (`GpuSwitchService`, `OmenGamingHubCleanupService`,
`SystemRestoreService`) and zero references from the fan/EC/undervolt/GPU-OC write paths. Verified
this by grepping every field for exclusive-vs-shared usage before moving anything, not by
assumption.

Extracted into a new `SystemMaintenanceViewModel`, exposed as `SystemControlViewModel.Maintenance`.
Two views bind into this cluster — `AdvancedView.xaml` (GPU mode switch, display overdrive, via
`SystemControl.Maintenance.X`) and `SettingsView.xaml` (the cleanup wizard, via a nested
`DataContext="{Binding DataContext.SystemControl.Maintenance, ...}"` rescope) — both repointed one
level deeper. `MainViewModel`'s game-profile-apply path (`SystemControl.SelectedGpuMode` /
`SwitchGpuModeCommand`) was the only other cross-VM reference and got the same treatment.

Also deleted `SystemControlViewModel.CleanupOmenHubCommand` — confirmed, via a repo-wide grep, to
be dead code with zero bindings or callers anywhere. (Note: `MainViewModel` has its own
identically-named `CleanupOmenHubCommand` backing a completely separate, independently-implemented
cleanup/restore-point feature that predates this work — left untouched; that duplication is a
separate, real finding worth its own look, not bundled into this refactor.)

Pure structural move — every property/method body is copied verbatim, including two pre-existing
latent bugs preserved as-is rather than opportunistically fixed: `CleanupStatus`'s setter never
notified `CleanupStatusText` of changes, and `RunCleanupAsync` never actually sets
`CleanupInProgress = true` (only ever back to `false` in its `finally`), so the "cleanup in
progress" spinner in Settings has likely never shown. Both are now easy to find and fix in
isolation later. Net: `SystemControlViewModel.cs` 5,610 → 5,290 lines. 6 new tests; full suite
1441/1441. `SystemOptimizerViewModel`, `SettingsViewModel`, and the rest of
`SystemControlViewModel` itself (still 5,290 lines) remain candidates for further passes.

### GitHub #193 Follow-Up: OMEN Key WMI Events Were Being Discarded, Not Missed

The reporter (HP OMEN 16-am0000, board `8D2F`) ran an independent, OmenCore-free
`Register-WmiEvent -Class hpqBEvnt` listener at our request — the same technique that isolated
#187 on a different board — and it received the exact expected `EventID=29`/`EventData=8613` on
every single physical OMEN key press. That's conclusive: the OS/firmware was delivering the event
correctly the whole time, so the bug had to be in OmenCore's own handling, not a driver/firmware
issue.

Traced it to two places in `OmenKeyService`, both dating from well before this cycle:

1. `OnWmiEventArrived` unconditionally discarded every WMI OMEN-launch event (`eventData=8613`)
   whenever the keyboard hook was active, on the theory that the hook would already catch the
   physical key so the WMI copy was just a duplicate to suppress. `IsHookActive` only proves the
   hook *object* is registered, though — not that this board's OMEN key produces any
   keyboard-observable VK/scan code at all. On this board it doesn't: the hook was active the
   whole session with zero candidates ever logged (`LastOmenKeyCandidate: none recorded`), so the
   WMI event — the only real signal this hardware exposes — was being thrown away every time.
2. `StartWmiEventWatcher` skipped starting the watcher at all when the hook was active and the
   experimental Fn+P profile-cycle feature was disabled. Less consequential in practice since that
   feature defaults to enabled, but still a real gap for anyone who'd turned it off, with no other
   fallback.

Removed both. The shared debounce timer between the keyboard-hook and WMI code paths
(`_lastKeyPressTicks`) already prevents a double-fire on boards where both paths genuinely catch
the same physical press — hook fires first (synchronous, no WMI round-trip), so a same-press WMI
event a few milliseconds later lands inside the debounce window that fire already opened. No new
gate needed to replace the ones removed. Updated the one existing test that had locked in the old
(buggy) skip-on-hook-active behavior; full suite 1441/1441.

Reported as fixed, pending the reporter's confirmation once they're on a build with this change —
matching this project's standard of not calling a hardware-interaction fix "done" purely from
code-tracing alone.

### Cross-Project Review: What "Ohman" (a Similar HP OMEN/Victus Tool) Does Differently

Reviewed a smaller open-source alternative, [Ohman](https://github.com/P4R1H/Ohman), specifically
its `docs/research.md` write-up of the same HP WMI BIOS mailbox this project drives, to see whether
any of its documented hardware behavior or findings pointed at a real gap here rather than assuming
either project's approach is automatically right. Checked four specific claims against OmenCore's
actual code before acting on any of them:

- **Firmware forgets a user-set fan level after ~120s** — already handled. `WmiFanController`'s
  countdown-extension heartbeat (every 5s) and `FanService`'s independent 30s force-reapply timer
  are both well inside that window. Nothing to change.
- **Polling the GPU at all — even at a background cadence — can keep a discrete GPU out of its
  deepest idle power state**, artificially raising chassis temperature with nothing running. Real,
  unaddressed gap: `HardwareMonitoringService`'s cadence tiers (2s/5s/10s) are keyed on UI
  visibility only, never on actual GPU load. Fixed below.
- **System-design-data bytes can derive real per-board defaults for unverified boards** (thermal
  policy version, SW-fan-control support, default power limits) instead of guessing from a
  same-family template. OmenCore already parses this exact byte structure (`HpWmiBios.cs`) but
  never wired it into the actual unverified-board fallback path. Real gap, confirmed — narrowly
  addressed below (see "Unverified-Board Fan Control Now Respects Firmware's Own SystemDesignData").
- **Battery charge limit** — Ohman's negative result (no such command exists in the mailbox; HP's
  own app uses a separate, UWP-gated WinRT path unreachable from an ordinary app) isn't contradicting
  or duplicating anything in flight here; nobody had recorded this before. Filed away as a "don't
  re-investigate this" note, not an open item.

Also cross-referenced Ohman's board list (itself sourced from Linux's `hp-wmi` driver) against two
currently-open boards: it independently confirms `8C9C` (#191) as a "Victus 16 S/R (2023-2024)"
board and `8D2F` (#193) as a 2023-2025 OMEN 16 board — used the former to give `8C9C` a real,
conservative database entry below instead of leaving it on Family fallback.

### GPU Telemetry Now Backs Off Further Once Confirmed Idle

`HardwareMonitoringService.GetEffectiveCadenceInterval()` already had three cadence tiers (2s
active / 5s idle / 10s tray-only), all keyed on UI window visibility. None of them account for
whether the GPU itself has anything to report. Added a fourth, narrower check: once 3 consecutive
samples show the GPU at ≤1% utilization and ≤12W package power, cadence backs off to once every 2
minutes — but only from the existing idle/tray-only branches, never from the active-window or
overlay-realtime cases, so this can't make the app feel slow while someone is actually watching it.
Resets to the normal tier instantly on the very next reading that shows real GPU activity, so a
workload starting up is never delayed. Verified this doesn't touch fan-curve responsiveness at all:
`FanService`'s control loop reads temperatures through its own independent `_thermalProvider` call
with its own separate adaptive-polling logic, entirely decoupled from this dashboard/UI-facing
telemetry pipeline. 3 new tests.

### Four-Zone Keyboard Lighting Gets a Real Keyboard Visual (Like Ohman/OmenMon)

Asked directly: could the four-zone RGB editor look like Ohman's or OmenMon's drawn keyboard?
Checked what already existed first rather than assuming a gap. `KeyboardMapEditor.xaml` /
`KeyboardMapViewModel.cs` already do exactly this — a physically-drawn, click-select,
rubber-band-drag-select keyboard, with a "measured from your actual hardware" vs "inferred from
HP's device table" honesty banner that neither reference project's screenshots show. It's arguably
already ahead here, just not where most users would find it: `IsFixedGridEditorVisible`/
`HasMeasuredKeyMap` gate it to per-key RGB hardware only (a narrow slice — OMEN MAX-class Darfon
boards), and the per-key-without-a-measured-map fallback is a uniform 6×14 grid, not a drawn
keyboard shape either. The genuinely common case — four-zone boards, most OMEN/Victus laptops —
had four plain rectangles with text listing roughly which keys sit in each zone
("TAB Q W E R T", "F5-F8 6 7 8 Y U I H J K B N M", etc.), not a keyboard at all.

`KeyboardMapViewModel` itself wasn't a good fit to extend for this: by design it draws from a
device's own reported lamp/key geometry (explicit in its own doc comment — "IT IS BUILT FROM THE
LAYOUT, NOT FROM LAMPARRAY, and the difference is the whole point"), and four-zone hardware
exposes no per-key positions to read at all. Forcing a "measured from the device" abstraction to
serve a "no device data, synthetic standard layout" case would have fought the class's actual
design intent — the codebase's own established pattern here is closer to "add a new view for new
evidence, don't force-fit old data flows" (see `KeyboardMapViewModel`'s own comment: "This exists
alongside the 6 x 14 grid editor rather than replacing it").

Instead, added a small, self-contained `FourZoneKeyboardLayout` — pure and side-effect free (no
device, no I/O), generating a standard TKL laptop keyboard shape row by row (unit-width keys,
cumulative X position, matching common ANSI key-width conventions: Tab 1.5u, Caps 1.75u, Enter
2.25u, etc.) with a slim right-side utility column for Del and an inverted-T arrow cluster. Each
key is tagged to a zone (1-4) using the *exact* key lists the old text schematic already
documented (Zone 1 = Esc/F1-F4/`1-5/Tab-G/Shift-V, Zone 2 = F5-F8/6-8/Y-M-ish, etc.) rather than
an even proportional split — a couple of those hints overlapped at their edges (both the old Zone
3 and Zone 4 text mentioned backslash/bracket keys), so the boundaries here make the closest
clean, consistent choice instead of reproducing that ambiguity. The one key that can't cleanly
belong to one zone on real hardware — the space bar, which physically spans Zones 1-3 — is shown
as Zone 2, the middle of its span, since this data model can only carry one zone per key.

Replaced the four-rectangle schematic in `LightingView.xaml` with this keyboard, reusing
`KeyboardMapEditor`'s exact Canvas/ItemsControl/Viewbox rendering approach for visual consistency
between the app's two keyboard views. Zero changes to the underlying zone-coloring logic: every
key's fill binds straight through to the existing `Zone1Brush`..`Zone4Brush` properties via a
`DataTrigger` on its zone index, and clicking a key routes through one new one-line dispatch
command (`SetZoneColorByIndexCommand`) to whichever of the existing
`SetZone1ColorCommand`..`SetZone4ColorCommand` already back the color pickers — the same proven
`RelativeSource AncestorType=ItemsControl` → `DataContext.X` binding pattern the per-key grid
editor already uses elsewhere in the same file.

5 new tests for the layout generator (every zone has a key, every key has a valid zone index, spot
checks against the old schematic's own key lists) — including a bounds-check against the canvas
that caught a real bug before it shipped: the canvas-width calculation reserved only 1 unit for
the utility column, but the arrow cluster (◄▼►) needs 3 keys side by side, so the right edge of
the keyboard was being clipped. Fixed by reserving the correct 3 units. Full suite 1455/1455.

Not independently visually verified against a live render: doing so means launching the actual
hardware-controlling executable (fan/RGB write access, admin elevation), which needs a UAC prompt
neither this environment's browser tooling nor its desktop-automation tooling can click through —
verified instead via the XAML compiling cleanly, a geometry unit-test suite with real bounds
checking (which already caught one real bug), and using the exact binding pattern already proven
working elsewhere in the same file. Worth a look on a real machine before calling this done.

### Unverified-Board Fan Control Now Respects Firmware's Own SystemDesignData

The deferred half of the Ohman review, scoped narrowly rather than as the full rewrite originally
flagged. `CapabilityDetectionService.LoadModelCapabilities()` already falls through to
`ModelCapabilityDatabase.GetCapabilitiesByFamily()` for any board with no entry of its own —
cloning a same-family template's flags, including whatever that template guessed about fan
control. `HpWmiBios` already queries and decodes HP's `SystemDesignData` block (`Default 0x28`)
during its own initialization, well before capability refinement runs, and one of its fields —
`IsSwFanControlSupport` — is a direct, firmware-authored statement of whether the board supports
software fan control at all. That's a stronger signal than a template guess, so
`RefineCapabilitiesFromModel()` (Phase 11, which already applies model-database overrides to the
live `DeviceCapabilities`) now checks it: if the firmware itself denies software fan control,
fan control is forced to monitoring-only regardless of what the family template assumed.

Scoped deliberately narrow to keep this safe:
- **Only for unverified boards** (`ModelConfig.UserVerified == false`). A hand-verified board's
  flags came from a real person's hardware and stay authoritative over a generic byte heuristic,
  full stop — this never runs for one, even if `SystemDesignData` disagrees.
- **Only narrows, never grants.** The check can turn a template's "fan control works" into "it
  doesn't" when firmware says so; it can never turn a template's "no" into a "yes." Under-claiming
  a capability is safe (worst case: a user files a report that turns out to work); over-claiming
  one from an unverified assumption is not.
- **Only the one field with an unambiguous, already-decoded meaning.** `SystemDesignData` also
  carries a GPU-mode-switch bitmask that could in principle narrow `HasMuxSwitch` the same way,
  but OmenCore's own decode only captures the raw byte (`GpuModeSwitch`) without an independently
  confirmed bit-to-meaning mapping — the only interpretation available (1 iGPU-only / 2 Hybrid /
  4 Discrete / 8 Advanced Optimus) comes from Ohman's own OGH-decompile research, not evidence
  this project has verified itself. Left alone rather than trusting a borrowed, unconfirmed
  bitmask in a capability-gating path; a real candidate for later if that mapping gets independently
  confirmed.

4 new tests, covering: the firmware-denies-support case actually disables fan control; a
`UserVerified` board is left untouched even when `SystemDesignData` disagrees; a firmware
confirmation changes nothing; and no `SystemDesignData` reading at all (WMI unavailable) falls
back to today's existing model-database-only behavior rather than guessing. Full suite 1450/1450.

### Board `8C9C` Given a Real Database Entry Instead of Family Fallback

[#191](https://github.com/theantipopau/omencore/issues/191)'s board (Victus, Ryzen 7 8845HS +
Radeon 780M + RTX 4070) was resolving via Family fallback (Low confidence) with no entry of its
own. Cross-referencing Ohman's board list (see above) confirmed it as a "Victus 16 S/R
(2023-2024)" board sharing a firmware generation with the already-verified-conservative `8BD4`
entry. Added `8C9C` to both `ModelCapabilityDatabase` and `KeyboardModelDatabase` with flags
inherited from `8BD4` — the firmware generation is now evidence-backed, but the individual
capability flags (GPU boost, undervolt, exact fan behavior) remain unconfirmed on this specific
board and are left conservative (`UserVerified = false`) pending a real diagnostics export or
Guided Fan Verification run. 2 new tests.

---

## Investigated, Not Yet Actioned

### Possible Future Pass: `ThermalMonitoringService`'s CPU/GPU Warning-Threshold Defaults

Surfaced while tracing #191 above. `ThermalMonitoringSettings.CpuWarningC`/`GpuWarningC` default
to 85°C — a value `FanService`'s own v2.8.0-era code comments already argue, from prior user
feedback, is normal gaming-laptop operating temperature, not something worth alerting on (that
lesson was applied to `FanService`'s own 90°C ramp-start threshold at the time, but evidently never
carried over to this separate, independent notification subsystem). Now that the Settings toggle
to disable these notifications actually works, this is lower priority than it was — but the
default is still arguably nagging out of the box for any user whose CPU boosts into the mid-80s
under normal short bursts, which is most modern gaming-laptop CPUs. Worth revisiting if more
reports of the same shape come in; not changed here on the strength of one report, per the
evidence-gate discipline against making a default-value judgment call for every existing user from
a single data point.

### Board `8C9C` — Database Entry Added, Full Capability Survey Still Open

Superseded by the entry above ("Board `8C9C` Given a Real Database Entry Instead of Family
Fallback") — kept here only for the evidence trail that predates it. Surfaced by #191's own
diagnostics screenshot: was resolving via Family fallback only (Low confidence). Reporter has AMD
Curve Optimizer (-80mV) and AMD CPU Power Limits (STAPM/TCTL) working via PawnIO, suggesting the
AMD SMU backend functions on this board, but no fan-curve or RGB confirmation existed yet, and no
other open issue mentioned this exact ProductId (checked via `gh search issues "8C9C"` at the
time). Rather than continuing to wait on a full diagnostics export before adding *any* entry,
independent cross-reference to Linux's `hp-wmi` board table (via the Ohman review) gave enough
evidence to add a conservative, firmware-generation-only entry — the fan-curve/RGB/GPU-boost
confirmation this note originally asked for is still genuinely open and still worth getting.

---

## Standing Rules (unchanged, carried from v4.3.0)

- **Evidence gate.** Fan/EC/thermal/OC/UV *behavior* changes need field validation before
  shipping. Architecture, performance, display-honesty, and pure-UI items do not.
- **Search every other open/closed issue mentioning the same ProductId before adding or editing a
  board entry** — established as a mandatory step last cycle after two near-misses; still in
  force.
- **One item at a time, verified before moving on.** Build clean, full suite green.
- **Update this document as you go.** Check items off only once verified, with a one-line note on
  what changed and which files.
