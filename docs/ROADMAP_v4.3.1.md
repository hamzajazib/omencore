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

### Board `8C9C` — HP OMEN Laptop (AMD Ryzen 7 8845HS + RTX 4070), Not Yet in the Model Database

Surfaced by #191's own diagnostics screenshot: resolves via Family fallback only (Low confidence).
Reporter has AMD Curve Optimizer (-80mV) and AMD CPU Power Limits (STAPM/TCTL) working via PawnIO,
suggesting the AMD SMU backend functions on this board — but no fan-curve or RGB confirmation
exists yet, and no other open issue mentions this exact ProductId (checked via
`gh search issues "8C9C"` before writing this). Not added as a database entry yet — waiting on the
fuller diagnostics export requested in the #191 reply before adding an identity entry that would
otherwise just be guessed at from one screenshot.

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
