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
