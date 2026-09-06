# OmenCore v4.3.0

**Release Date:** TBD — rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-08-30, immediately after v4.2.0 shipped.
**Type:** Feature release. Started as a v4.2.1 patch cycle (field-report fixes from GitHub issues
opened after v4.2.0, #178–#182) that grew into the `OmenCore.Core` extraction and the first slice
of a Windows CLI — folded together into one v4.3.0 release rather than shipping a separate patch
first.
**Base Version:** v4.2.0
**Tracking doc:** `docs/ROADMAP_v4.3.0.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

**Fixed, by area:** [Fan, Performance & Thermal Safety](#fan-performance--thermal-safety) · [RGB & Keyboard Lighting](#rgb--keyboard-lighting) · [Linux](#linux) · [Windows Stability & Core](#windows-stability--core) · [Model Database & Hardware Identity](#model-database--hardware-identity) · [GitHub Backlog Housekeeping](#github-backlog-housekeeping)
**Added:** [Automation Rules Engine](#automation-rules-engine) · [Architecture & Tooling](#architecture--tooling)

---

## Fixed

### Fan, Performance & Thermal Safety

#### Quiet Safety Monitor Could Silently Switch Performance Mode When Fan/Performance Linking Was On

Reported on [#181](https://github.com/theantipopau/omencore/issues/181) (OMEN Max 16 ah0xxx, RTX 5090): "some transient CPU spikes made OmenCore enable max fan mode, which made the laptop's fans deafening and inherently enabled Performance completely disregarding the fact it was on Quiet earlier."

Traced to a real interaction bug. The Quiet Safety Monitor (on by default, triggers at 90°C) calls `FanService.ApplyMaxCooling()` specifically to keep the user on their chosen power profile while forcing fans to Max — its own log message says "Quiet power mode retained." But `ApplyMaxCooling()` unconditionally raises the same `PresetApplied` event a normal user-initiated Max Fan click would, and `MainViewModel.OnFanPresetApplied` treats every `PresetApplied` event as fan-mode-changed-by-something, cascading into a performance-mode switch via `FanPerformanceLinkMapper` whenever Fan/Performance linking is enabled — completely undoing the "power mode retained" guarantee for anyone using that combination.

Fixed by giving `FanService.PresetApplied` a real payload (`FanPresetAppliedEventArgs`, replacing the old plain `string`) carrying a `SuppressLinkedProfileSync` flag, and threading a `suppressLinkedProfileSync` parameter through `ApplyMaxCooling(...)` down to that event. The Quiet Safety Monitor is now the one caller that passes `suppressLinkedProfileSync: true`; every user-initiated Max Fan trigger (button, hotkey, OMEN key) still passes the default `false` and keeps cascading into the linked performance-mode switch exactly as before. `MainViewModel.OnFanPresetApplied` checks the flag before running the link-sync block — every other consumer (tray icon, sidebar, dashboard) is unaffected and still correctly shows Max fan state regardless. 2 new tests confirming the flag reaches `PresetApplied` correctly in both the suppressed and default cases, plus 3 existing tests updated for the new event-argument type. Full suite: 1376/1376.

#### Power Automation Silently Overwrote Manual Fan/Performance Selections on Every Startup

Re-diagnosed from v4.2.0's #177 triage ("custom fan curve not restored on restart"), traced to its exact cause this time: with Power Automation enabled, `MainViewModel` force-applied the configured AC/Battery preset on *every* startup, unconditionally — not just when the power source had actually changed. A user who manually picked a different preset mid-session, then closed and reopened the app on the same power source, had that manual choice silently discarded and replaced every time, by design rather than by bug.

Fixed by teaching `PowerAutomationService` to persist the AC/Battery state it last confirmed (`PowerAutomationSettings.LastKnownAcState`) and compare it against the freshly-detected state at the start of each session. The startup profile apply now only fires when the power source is known or suspected to have actually changed since the app last had control (including while it was closed) — otherwise it's a no-op, and the user's last manual selection (already restored earlier in the same startup sequence) stands. Settles the "who owns the active profile" question the roadmap had flagged as a precondition for adding more automation trigger types. New regression test simulates two sequential app sessions against the same config to pin the "unchanged power source → skip" behavior down. Full suite: 1381/1381.

#### GitHub #128 Follow-Up: `88EC` Performance Mode Switches Did Nothing At All

A fresh diagnostics export confirmed a genuine bug live in the field: `⚠️ Performance mode 'Balanced': nothing was applied (Direct EC writes disabled for model 'HP Victus 16-e0xxx')`. Direct EC writes are (correctly) disabled on this board, but it never got the `AllowDecoupledWmiThermalPolicyFallback` flag already shipped for four sibling boards (`8DCD`, `8C30`, `878C`, `8600`) to route mode switches through the WMI thermal-policy path instead — so switching modes had no effect at all. Same one-flag fix applied here. The same bundle also confirmed the WMI fan-write path itself works correctly (a real thermal-protection event fired and boosted fans as designed), and surfaced a cross-board CPU-thermal-authority-switching pattern (20 switches/session, ~1°C apart) matching an earlier one-off observation on a different board — recorded for a future dedicated look, not fixed blind.

### RGB & Keyboard Lighting

#### Built-In "Night Mode"/"Work" RGB Scenes Silently Overrode Manually-Configured Lighting on a Schedule

Reported via Discord ("snowfall hateall", board 8D87/88F7, OMEN MAX 16-ak003nr): "the rgb light bar randomly turned orange without any command from the user. It is configured to be off, yet randomly switched to orange without warning." The attached log showed the actual mechanism at the exact moment it happened: `[INFO] Scheduled time triggered scene 'Night Mode'` at 22:00:26, applying `#331100` (a dark amber/orange) to all 4 keyboard zones — not a hardware fault or random glitch.

Both built-in "Night Mode" (`#331100` @ 30% brightness) and "Work" (`#FFFFFF` @ 80%, weekdays) scenes shipped with a baked-in `ScheduledTime`, and `RgbSceneService.CheckScheduledScenes()` fires any scene carrying one unconditionally, gated only by `IsSchedulingEnabled` — a property that defaults `true` and has **zero UI surface anywhere in the app**: no toggle to disable scheduling, no way to see a scene has a schedule, no editor to change or clear one. Every OmenCore user, out of the box, had their keyboard lighting silently changed to dim amber every night at 10PM and to white every weekday morning at 9AM, regardless of whatever they'd manually configured — including having explicitly turned lighting off.

Fixed by removing the default `ScheduledTime`/`ScheduledDays` from both built-in scenes. Both remain fully selectable from the scene list exactly as before — only the silent, un-opt-out-able default schedule is gone. The scheduling engine itself (`CheckScheduledScenes`, `IsSchedulingEnabled`) is left in place for a future release that gives it a real UI. New regression test (`BuiltInScenes_NeverShipWithASilentDefaultSchedule`) asserts no built-in scene ships with a schedule. Not a field-validation item — this only removes an unrequested default write path; it adds no new one.

#### Corsair DPI Editor Could Show "Success" for a Write That Never Reached the Mouse

Found while working the roadmap's "decide and be honest" item on Corsair's RGB.NET-backed provider (`CorsairICueSdk`). Two related honesty gaps:

`DiscoverDevicesAsync`/`GetDeviceStatusAsync` hardcoded `BatteryPercent = 100` for every Corsair device with the comment "RGB.NET doesn't expose battery info" — a fabricated full-charge reading, not a placeholder. `CorsairDeviceStatus`'s own display logic already only shows a battery line `if (BatteryPercent > 0)`, so the fix is just using that existing convention honestly: `BatteryPercent = 0` now correctly shows nothing instead of a fake 100%.

The bigger issue: `ApplyDpiStagesAsync` presents a real confirmation dialog — "This will change the hardware DPI settings on the selected device" — then, on this backend, logs "DPI configuration not supported via RGB.NET" and returns without writing anything. Nothing in the call chain checked for that: the ViewModel updated the device model, saved config defaults, and updated the saved DPI profile as if the write had succeeded, with no failure ever surfaced to the user. `ApplyDpiStagesAsync` now returns `Task<bool>` end to end (interface → `CorsairSdkStub`/`CorsairICueSdk`/`CorsairHidDirect` → `CorsairDeviceService` → both ViewModel call sites), matching the same "return true only if the write actually reached the device" contract `ApplyLightingAsync` already used. A failed apply now shows an explicit "DPI Settings Not Applied" dialog instead of silently updating state. 3 new/strengthened tests plus 2 test fakes updated for the new signature.

Corsair macro upload has the identical "not supported, no-op" shape on every backend including the real HID-direct one, but is never called from any ViewModel — no UI action reaches it, so it's dead code rather than a live honesty bug. Left alone; noted in the roadmap as needing an actual implementation decision, not a quick fix. Full suite: 1377/1377.

#### GPU Power Boost Card Always Showed "+15W" Even When Extended Was Selected, With No Ceiling Caveat

Follow-up to [#181](https://github.com/theantipopau/omencore/issues/181)'s non-linking half: the "EXTRA POWER" badge on the GPU Power Boost card was a hardcoded `"+15W"` string regardless of which level was actually selected, so choosing Extended (documented elsewhere in the same file as "+25W or more") still showed the Maximum level's number. Now bound to a per-level `GpuPowerBoostWattageText` property (Minimum/base → `+0W`, Medium → `Custom`, Maximum → `+15W`, Extended → `+25W`), matching the level-aware wording a different summary property in the same ViewModel already used elsewhere. Also added a caveat callout on the card itself explaining that these are relative boost requests to shared firmware, not an absolute wattage guarantee, and suggesting a full OMEN Gaming Hub close if the observed wattage doesn't match. Pure UI/display-honesty change — no hardware-write path touched, no field validation needed.

#### RGB Page's "Control Ownership" Card Could Show "Confirmed" With No Real Keyboard Backend

Found by actually driving the app and looking at the Lighting page (a live-machine look, not just a code read) — on a desktop PC with no HP hardware at all, the "Control ownership" card showed "HP Keyboard (None)" as its summary text, right next to a green "Confirmed" ownership badge, and the "OMEN Keyboard" status chip at the top of the page was highlighted as if active.

Traced to `KeyboardLightingService.IsAvailable` including `_ecAvailable` unconditionally, while `BackendType` (which correctly produced "None" in this exact case) only ever reports an EC backend when the user has explicitly opted into the experimental EC keyboard-write path (`IsExperimentalEcEnabled`). `_ecAvailable` only means "we can talk to *an* embedded controller via PawnIO" — true on almost any modern PC for basic power management, regardless of whether it's an HP OMEN keyboard-controlling EC. `IsAvailable` now applies the same experimental-opt-in gate `BackendType` already used, so the two agree. 3 new tests (`KeyboardLightingServiceAvailabilityTests.cs`, using the uninitialized-object + reflection pattern already established elsewhere in this test project, since the class's constructor needs real hardware access objects). Full suite: 1380/1380.

#### Keyboard RGB "Did Not Verify" Status Didn't Surface Its Own Fix Suggestion

Traced from a Discord report ("keyboard rgb aint changing") plus its attached diagnostics: on this board (HP OMEN 16-wd0xxx, `8BA9`), the WMI ColorTable keyboard-lighting write is accepted but its color readback never verifies, and `KeyboardLightingService`'s telemetry shows 0% WMI success across the whole session. The code already had the right troubleshooting suggestion — "Try enabling 'Experimental EC Keyboard' in Settings if RGB doesn't change" — but it was only ever written to the log file, never to the same status text the Lighting page already shows the user (`KeyboardRestoreStatusText`, which said only "...did not verify..." with no next step). `LightingViewModel.ApplyKeyboardColorsAsync` now appends the hint to that visible status text whenever WMI has zero verified successes, instead of leaving it log-only. Pure UI/display fix — no lighting write path changed.

### Linux

#### GPU Telemetry Never Queried NVML, So a Real NVIDIA GPU Read as 0°C/Unavailable

**Report:** [#186](https://github.com/theantipopau/omencore/issues/186) — OMEN Max 16-ah0xxx (board `8D41`, RTX 5080). `omencore-cli status` reported `GPU Temperature: 0°C` / `GPU Telemetry: unavailable` and the GUI showed `0°`, `0% usage`, `Power: 0 W`, with the adapter shown by raw PCI ID (`NVIDIA GPU (0x2c59)`) — while `nvidia-smi` read the exact same GPU correctly (41°C, 24W) in the same second, unprivileged, no root needed.

Traced to a real gap, not a driver/permissions problem as the reporter already suspected: OmenCore's Linux GPU telemetry only ever checked `hwmon` (`/sys/class/hwmon/*/name == "nvidia"`, which the proprietary NVIDIA driver typically doesn't register — unlike `amdgpu`/`nouveau`) and the OMEN EC's GPU thermal register. NVML — the library `nvidia-smi` itself is built on, and the correct way to read an NVIDIA GPU on Linux regardless of hwmon exposure — was never referenced anywhere in the codebase.

Added `NvmlInterop` (`OmenCore.Linux/Hardware/NvmlInterop.cs`), a P/Invoke wrapper around `libnvidia-ml.so.1` with a custom `NativeLibrary` resolver (systems with only the runtime driver package installed, no `-dev` symlink, often only have the versioned `.so.1`, which .NET's default Linux library probing doesn't try). Wired in as the first-priority source in `LinuxTelemetryResolver.GetGpuTemperature` — ahead of hwmon and EC, since it's authoritative for NVIDIA GPUs. Also fixed **`MonitorCommand`**, which turned out to have its own third, independent `hwmon.GetGpuTemperature() ?? ec.GetGpuTemperature()` chain bypassing the shared resolver entirely (same underlying bug, a second time); it now routes through `LinuxTelemetryResolver` like `StatusCommand`/`DiagnoseCommand` already did. `status`/`monitor` now also show GPU name, power draw, and utilization from NVML. Per the reporter's own suggestion, `diagnose`'s Notes now surface *why* GPU telemetry is unavailable (NVML load/init failure reason, not just "unavailable") when the whole fallback chain is exhausted.

3 new tests (`NvmlInteropTests.cs`) confirm NVML absence fails closed (null, not a thrown exception) with a diagnosable reason. Linux suite: 28/28 (up from 25/25).

**GUI (`OmenCore.Avalonia`) parity landed in the same cycle, as a follow-up commit.** `LinuxHardwareService.cs` turned out to be a *third*, fully independent GPU-telemetry implementation — its own hwmon-only temperature read, and its own `0x2c59`-style raw-PCI-ID name fallback (`FormatGpuName`), completely separate from the CLI's `LinuxTelemetryResolver`. `GetStatusAsync` now tries `NvmlInterop.TryGetPrimaryGpu()` first for temperature (falling through to the existing hwmon read unchanged when NVML isn't available), and populates `GpuUsage`/`PowerConsumption` from it — both fields already existed on the `HardwareStatus` DTO and were already bound in `DashboardViewModel`, but were dead on real Linux hardware (only ever populated in the Windows-side mock data path), which is exactly the `0% usage`/`Power: 0 W` the report showed. `ReadGpuNameAsync` now prefers NVML's actual product name over the PCI-ID fallback. `HasStatusChanged` now also compares `GpuUsage`/`PowerConsumption`.

**Not a field-validation item** in the evidence-gate sense — this is a read-only telemetry addition, no fan/EC/thermal/OC/UV write path touched. It is, however, genuinely unverifiable from this environment (no Linux machine with an NVIDIA GPU to run it against) — the P/Invoke bindings match NVML's documented, ABI-stable public API, and both build targets compile clean, but an actual run against real hardware is the real verification still needed.

#### Switching Max → Auto Under Load Could Leave Both Fans Dead and Cause a Thermal Shutdown

**High-severity safety fix.** [#183](https://github.com/theantipopau/omencore/issues/183) (OMEN MAX 16-ak0xxx, board `8D87`, Ryzen AI 9 HX 375 + RTX 5080): switching the fan profile from `max` to `auto` via `omencore-cli fan --profile auto` while under a full gaming load left both fans at 0 RPM indefinitely — they never resumed as temperatures kept climbing — and the laptop thermally shut down shortly after.

Traced to `LinuxEcController.SetFanProfileViaAcpiHwmon`: on this board, `pwm_enable=2` ("firmware auto") is a policy flag telling the firmware to take over, not a guarantee it actually does — degraded ACPI on this board (kernel logs showed `WMAA`/`WHCM`/`WQB*` method aborts) let the write report success while the firmware's own fan-curve handler never resumed driving `pwm1` upward. Nothing else was watching: `omencore-cli` is a one-shot command, not a monitored daemon.

Fixed by polling fan RPM for a few seconds after every Auto-mode write on this code path; if both fans are still at 0 while CPU or GPU temperature is above a conservative 85°C safety bar, automatically falls back to Max mode (the exact write path the reporter confirmed reaches ~6000 RPM reliably on this board) instead of reporting Auto as applied. `RestoreAutoMode()` — also called directly by the fan-curve daemon (`Daemon/FanCurveEngine.cs`) — was refactored to route through the same fixed code path instead of duplicating the unprotected writes, so the daemon gets the same protection. No new/unverified write path was introduced — the fallback only ever reuses a mode already proven to work on this exact board.

#### Capability Classifier Could Name the Wrong Control Mechanism in Its "Full Control" Reason Text

Found while investigating [#127](https://github.com/theantipopau/omencore/issues/127) (board `8D26`, "can't control anything despite full-control classification"): `LinuxCapabilityClassifier`'s reason text for `FullControl` checked `hasHwmonFanAccess` first — but that flag never contributes to `hasManualFanControl` becoming true at all (it's an independent, hwmon-only signal that on its own only grants `ProfileOnly`, per the class's own existing design and tests). A board where `hasEcAccess` is what actually made the classification `FullControl`, but which also happens to expose `hasHwmonFanAccess` (an unrelated, independent flag), got told "Manual fan control is available through hp-wmi hwmon pwm/fan targets" instead of the true reason, "...through legacy EC access" — exactly the mismatch #127's own `diagnose` output shows. The same mismatch was independently confirmed on a second board in [#126](https://github.com/theantipopau/omencore/issues/126).

Reordered the reason-selection to check the actual contributing flags (`hasEcAccess` → fan target files → fan output files) and dropped the irrelevant hwmon branch from this reason chain entirely. Pure diagnostic-text correctness fix — no capability classification or control behavior changed, only which sentence explains it. 2 new regression tests; Linux suite: 30/30 (up from 28).

### Windows Stability & Core

#### OMEN Key WMI Watcher Could Register Successfully Yet Never Receive a Single Event

**Report:** [#187](https://github.com/theantipopau/omencore/issues/187) — HP Victus 16-e0054nl, board `88EE`. Exceptionally well-isolated report: OmenCore's WMI event watcher logged a clean registration (`✓ WMI event watcher started`) and then never logged anything else across two full test sessions of physically pressing the OMEN key — while the reporter's own `Register-WmiEvent -Class hpqBEvnt` (identical event class, **no WHERE clause**) received the same key press instantly and reliably every time, printing exactly `EventID=29`/`EventData=8613` once captured.

Root cause: `StartWmiEventWatcher` subscribed with a WQL `WHERE eventId = 29 AND eventData = 8613`-style server-side filter. `OnWmiEventArrived` *also* independently re-extracts and re-validates `eventId`/`eventData` itself and fails closed on anything that doesn't match exactly — the server-side WQL filter was fully redundant, and on this board's `hpqBEvnt` schema, the server-side numeric-literal WHERE-clause match was silently never evaluating true (a plausible ACPI-WMI-mapped-class type quirk), even though the same values read back correctly once an event instance was captured and inspected.

Fixed by subscribing to the class only (`SELECT * FROM hpqBEvnt`, matching the reporter's own proven-working test exactly) and letting the existing, already-correct client-side filtering in `OnWmiEventArrived` do all the real work — safe because that handler takes no user-visible action on any BIOS event (fan/thermal/power included) until *after* its own `eventId==29`/`eventData==8613` check passes. Also added `EnablePrivileges = true` to the WMI connection scope defensively, in case a privilege gap rather than (or in addition to) a query-type mismatch was involved.

Not field-validated against real hardware — this environment can't reproduce a physical OMEN key press. Framed as implemented-pending-confirmation; the failure mode if this theory is wrong is "still doesn't fire," not a regression, since the client-side safety net is unchanged.

#### OSD Toggle-Hotkey Cleanup Could Throw a Null-Reference During Shutdown

Found while auditing a diagnostics bundle for GitHub #184: `[WARN] OSD: Hotkey cleanup encountered an error: Value cannot be null. (Parameter 'window')` appeared during shutdown, right before the app finished exiting.

`OsdService.UnregisterToggleHotkey()` re-derived the window handle via `new WindowInteropHelper(Application.Current.MainWindow)` — `Application.Current.MainWindow` can already be null by the time this cleanup runs, and `WindowInteropHelper`'s constructor throws exactly this exception when passed null. The fix uses `_hotkeySource.Handle` instead — `HwndSource.Handle` is guaranteed to be the same hwnd the hotkey was originally registered against, so this is strictly more correct as well as null-safe. Caught and logged rather than crashing either way, so this was silent/cosmetic in practice, not a functional bug — fixed anyway since the correct fix was small and unambiguous once traced.

#### In-Process Telemetry Fallback Could Crash the Whole App on Hybrid AMD+NVIDIA Hardware

**High-severity stability fix**, found via a recurring test-suite crash (3 of 4 full runs this session), not a field report. Full test runs kept aborting with an unrecoverable `System.AccessViolationException` in `LibreHardwareMonitor.Hardware.Gpu.AmdGpu.Update()` → `AtiAdlxx.ADL2_Adapter_DedicatedVRAMUsage_Get` — a native, corrupted-state exception no C# `catch` block can intercept, so it kills the whole process outright.

`OmenCore.HardwareWorker` (the out-of-process telemetry host) already quarantines exactly this: on startup, if both an AMD and an NVIDIA GPU are detected, it disables AMD ADL telemetry entirely rather than risk this exact crash. But `LibreHardwareMonitorImpl` — the in-process fallback `ThermalSensorProvider`/`FanService`/`HardwareMonitoringService` use when the out-of-process worker isn't available — had no equivalent protection on its own local `Computer` object. This isn't hypothetical: the #184 diagnostics bundle (a real hybrid AMD+NVIDIA laptop) shows `WmiBiosMonitor` switching to this exact "LHM Fallback" CPU-temperature authority repeatedly during normal use.

Added `QuarantineHybridAmdGpuTelemetryIfNeeded()` to `LibreHardwareMonitorImpl`, mirroring the worker's own detection, applied at both call sites that dispatch GPU updates. CPU/fan/memory/storage telemetry and the NVIDIA GPU (a separate, already-hardened NVML path) are unaffected — only the specific AMD ADL call this instability traces to is skipped. Full suite: 1380/1380, confirmed clean across repeated runs after the fix (pre-fix: crashed 3 of 4 full runs at this exact spot).

### Model Database & Hardware Identity

Model capability fallback logic and a batch of exact-ProductId additions, all from real field reports:

- **Fallback logic was optimistic instead of conservative.** Traced from [#182](https://github.com/theantipopau/omencore/issues/182) (board `8603`): the two fallback paths used when a board has no exact match (`DefaultCapabilities`, `GetCapabilitiesByFamily`) defaulted to claiming most advanced write-capable features as supported — `GetCapabilitiesByFamily` specifically cloned whichever board happened to be first in the dictionary for that family as a template. Both now only assume WMI BIOS fan-mode switching and OEM performance profiles; everything with its own write path defaults to false until a real board entry confirms it. 2 new regression tests, including one that checks every `OmenModelFamily` value.
- **`8E10`** — HP OMEN 17-db1xxx, two independent reports ([#130](https://github.com/theantipopau/omencore/issues/130) 17-db1180ng/RTX 5070; [#171](https://github.com/theantipopau/omencore/issues/171) 17-db1012nt/RTX 5060). Was resolving via OMEN17 family fallback with a "Model not in database" warning. `MaxFanLevel` is pinned at **45**, not the sibling boards' nominal 55 — #171's own Guided Fan Verification measured this board's real Max-hold ceiling at 45; using 55 would have made the Max-mode floor check permanently unwinnable and introduced a known, already-tracked reassert-loop bug on this exact board (see "Self-Caught Mistake" in `docs/ROADMAP_v4.3.0.md` for the full story — this was corrected before it shipped, not after). `HasMuxSwitch`/`SupportsGpuPowerBoost` are conservatively `false`, not inherited from the CPU-generation-sibling `16-ap0xxx` boards. The reporter's separate complaint about dynamic RGB scene streaming (Rainbow/Wave) not working is this board generation's own static-color-only ColorTable protocol limitation, not something a database entry changes.
- **`8D26`** — HP OMEN 16-ap0xxx ([#188](https://github.com/theantipopau/omencore/issues/188)), AMD Ryzen AI 7 350 + Radeon 860M + RTX 5070. Was resolving via a fuzzy name-pattern match to `8D24`; reporter confirms the hardware already works under that fallback. Added as its own exact entry (in both `ModelCapabilityDatabase` and `KeyboardModelDatabase`) with the identical profile `8D24`/`8E35` already use.
- **`88D2`** notes updated to cite [#146](https://github.com/theantipopau/omencore/issues/146) (fans stuck at 100% until an app restart) alongside the existing [#132](https://github.com/theantipopau/omencore/issues/132) entry — not a capability-flag fix, needs a fresh diagnostics export captured during the actual misbehavior to trace further.
- **`8E5E`**, **`8603`**, **`8BA9`** — see "Three New Model Database Entries" under Added, below; grouped there since they landed alongside the automation-trigger work rather than a dedicated field-report pass.

### GitHub Backlog Housekeeping

Audited the backlog of older open issues against the current codebase and closed 10 that already had a real, unconditional fix landed in an earlier cycle but were never replied-to or closed on GitHub: [#121](https://github.com/theantipopau/omencore/issues/121)/[#125](https://github.com/theantipopau/omencore/issues/125)/[#129](https://github.com/theantipopau/omencore/issues/129) (boards `8A43`/`8C3F` given exact entries; AMD Ryzen CPU-temperature worker-preference fix), [#135](https://github.com/theantipopau/omencore/issues/135)/[#138](https://github.com/theantipopau/omencore/issues/138)/[#139](https://github.com/theantipopau/omencore/issues/139)/[#140](https://github.com/theantipopau/omencore/issues/140) (boards `8C30`/`8DCD`/`88EE` given exact entries with working performance-mode/fan-control profiles), [#137](https://github.com/theantipopau/omencore/issues/137) (board `8BCD`'s ACPI-WMAA-abort downgrade, already shipped), [#141](https://github.com/theantipopau/omencore/issues/141) (Fn+F2/OMEN-key VK-scan-code collision, already pinned by a regression test), and [#144](https://github.com/theantipopau/omencore/issues/144) (board `8A18` exact entry). Several others got evidence-based replies without a code change ([#124](https://github.com/theantipopau/omencore/issues/124), [#128](https://github.com/theantipopau/omencore/issues/128), [#131](https://github.com/theantipopau/omencore/issues/131), [#134](https://github.com/theantipopau/omencore/issues/134), [#142](https://github.com/theantipopau/omencore/issues/142), [#143](https://github.com/theantipopau/omencore/issues/143)).

---

## Added

### Automation Rules Engine

#### Temperature, Idle, Process, and WiFi Network Triggers

Discovered mid-cycle while scoping "extend `PowerAutomationService` with time-of-day/lid-close/charger-connect triggers" (roadmap "Not Yet Started"): that framing was stale. A separate, more general rule engine (`AutomationService` + a real "Automation Rules" editor in Settings) already ships time-window and AC-power triggers today. `AutomationService`'s backend has actually supported **seven** trigger types since v2.3.0 (Time, Battery, ACPower, Temperature, Process, Idle, WiFiSSID), but the UI/validator only ever exposed three — the other four were implemented and functional but deliberately held back as "not shipped yet," with no comment on record explaining which ones were actually safe to promote.

Reviewed all four gated types before touching anything: **Temperature** and **Idle** were complete and correct — promoted immediately. **WiFiSSID** had a real bug — its fallback path didn't actually match the configured SSID at all, just checked whether *any* wireless interface was up. **Process** had a different real bug: its trigger checks `ProcessMonitoringService.ActiveProcesses`, which only ever contains processes registered via `TrackProcess()` — and that's only ever called from `GameProfileService` for configured Game Profiles, so a Process-trigger rule for any other executable would silently never fire.

Promoted **Temperature and Idle** first with matching UI fields in the Settings → Automation Rules editor. **Process and WiFiSSID promoted as a same-cycle follow-up**, once each one's real bug was fixed:
- **WiFiSSID's** primary lookup (the obsolete `"root\WlanApi"` WMI namespace) and its broken fallback are both replaced by a new `OmenCore.Utils.WlanSsidHelper`, a P/Invoke wrapper around `wlanapi.dll`'s Native Wifi API — the same API the Windows network flyout itself is built on. Fails closed when no interface is connected.
- **Process's** trigger now gets its executables registered via `AutomationService.EvaluateRules` calling `TrackProcess()` for every enabled Process-trigger rule each tick (via a new pure `GetProcessTriggerExecutableNames` helper) — idempotent and never un-tracks, so it can't undo tracking `GameProfileService` still relies on.

All 7 backend trigger types are shipped as of this pass. 6 new/updated validator tests plus a regression guard for the new helper.

#### Lid-Close Automation Trigger

Completes the `ROADMAP_v2.5.0.md` §7 ask (time-of-day / lid-close / charger-connect) — the first two already shipped via `AutomationService` above; this adds the third, genuinely-missing one: a new `TriggerType.LidState`.

Lid state has no poll-on-demand Win32 API the way AC/battery does — Windows only pushes lid transitions, via `WM_POWERBROADCAST` to a real window's message queue. New `OmenCore.Utils.LidSwitchMonitor` runs a tiny, invisible message-only window on its own dedicated background thread (no WPF dependency), registers for `GUID_LIDSWITCH_STATE_CHANGE`, and caches the latest lid state for `AutomationService`'s regular poll to read — fails closed until a real notification arrives or on a desktop with no lid at all. New "Lid state" field in Settings → Automation Rules. 7 new tests: 4 for the pure broadcast-interpretation logic, 3 for the validator.

Not verified against real hardware — but unlike a fan/EC write, the failure mode here is strictly "the rule doesn't fire," never a hardware-unsafe state, so this ships as implemented-pending-confirmation rather than held back.

#### Three New Model Database Entries From Field Reports

- **[#178](https://github.com/theantipopau/omencore/issues/178)** — HP Victus 15-fa2303TX (C2JQ3PA), board `8E5E`. Added using the reporter's own fan-verification diagnostic (WMI fan-level control responds, but RPM readback is level-estimated rather than a real tachometer). Single-zone, static-color-only keyboard backlight per the reporter.
- **[#182](https://github.com/theantipopau/omencore/issues/182)** — HP OMEN 17-cb0xxx (i9-9880H + RTX 2080), board `8603`. Gives this board a fixed database entry instead of depending on the now-fixed-but-still-generic family fallback.
- **Discord (GHOST), 2026-09-02** — HP OMEN 16-wd0xxx (i7-13620H + RTX 4060), board `8BA9`. Was resolving only as "Unknown OMEN16 Model" via family fallback.

All three entries are conservative and unverified pending further field confirmation.

### Architecture & Tooling

#### `OmenCore.Core` — a standalone class library for the hardware/service layer

`OmenCoreApp.csproj` was `<UseWPF>true</UseWPF>` with the entire service and hardware layer compiled directly into the WPF application assembly, blocking three separate wishlist items (a Windows CLI, a local HTTP/named-pipe control API, any future headless/service-mode operation).

Moved `Models/`, `Hardware/`, and nearly all of `Services/` (194 files total) into a new `src/OmenCore.Core/OmenCore.Core.csproj` — no `UseWPF`. `OmenCoreApp` now references it as a `ProjectReference`. Nine files stayed behind because they're real WPF/window couplings (`ToastNotificationService.cs`, `OsdService.cs`, `HotkeyService.cs` + `RuntimeHotkeyCoordinator.cs`, `CurveRecoveryService.cs`, `MacroService.cs`, `DiagnosticExportService.cs` + `ModelReportService.cs` + `ModelIdentityResolutionSummary.cs`).

A handful of files had a hidden WPF/WinForms coupling a first pass wouldn't catch (`Application.Current?.Dispatcher`, `System.Windows.Forms.SystemInformation.PowerStatus`, a bare `App.Logging`/`App.Current` reference relying on C# nested-namespace lookup) — see `docs/ROADMAP_v4.3.0.md` for the two new abstractions (`UiThreadMarshaller`, `PowerStatusHelper`) and the `AppHost` singleton relocation that resolved them. Full suite: 1380/1380, unchanged from before the move — a pure structural extraction.

#### Windows CLI — `status` / `fan` / `performance` / `keyboard` / `monitor` / `config` / `daemon`

New `omencore-cli` console app (`src/OmenCore.Cli`), built directly on `OmenCore.Core` — no duplicated hardware logic. `status [--json]` reports model/board ID, EC and fan-controller availability, live fan RPM/duty, and current performance mode. `fan`/`performance` apply presets through the same `FanService`/`PerformanceModeService` calls the GUI makes. `keyboard --color <hex>` applies a static color via the same `KeyboardLightingService.ApplyEffect` the GUI uses. `monitor [--interval ms]` redraws live telemetry until Ctrl+C. `config --show`/`--get`/`--set` reads/writes a curated subset of settings. `daemon --profile <name>` runs a fan preset in the foreground with the continuous monitor loop actually running; `daemon --status` checks whether the GUI app is already running first. Foreground-only — no Windows Service or Scheduled Task self-installation.

Command parsing verified end-to-end. `config --show`/`--get` and `daemon --status` were run for real against the live config (neither touches hardware). **`status`/`fan`/`performance`/`keyboard`/`monitor`/`daemon --profile` not yet verified against real hardware** — that needs an actual elevated run. See `docs/ROADMAP_v4.3.0.md` for the full bootstrap trace.

#### Package-Reference Cleanup on `OmenCoreApp.csproj`

Removed nine packages (`CUE.NET`, `HidSharp`, `LibreHardwareMonitorLib`, `NAudio`, `NvAPIWrapper.Net`, `RGB.NET.Core`, `RGB.NET.Devices.Corsair`, `System.Management`, `System.ServiceProcess.ServiceController`) now reached only transitively via the `OmenCore.Core` project reference. Full solution build and test suite confirmed clean.

---

## Investigated, Not Yet Actioned

- **[#179](https://github.com/theantipopau/omencore/issues/179)** — Linux per-key RGB for OMEN MAX 16-ak0xxx (board `8D87`) via direct HID (`0D62:54BF`, interface 3). Excellent, detailed field data — a real feature addition (new Linux HID backend), not a quick fix. Scoped for a future pass.
- **[#180](https://github.com/theantipopau/omencore/issues/180)** — "Doesn't start with Windows, config not saving." One sentence, no diagnostics, no repro steps. Needs a diagnostics export or repro steps before it's actionable.
- **[#181](https://github.com/theantipopau/omencore/issues/181)** GPU Power Boost wattage — architectural, not a code bug: OmenCore and OGH both send relative *boost steps* to the firmware, not absolute wattages, so the actual ceiling is firmware-determined and can be influenced by whatever OGH last configured. The UI-clarity half of this is now fixed (see "Fixed" above); still needs the reporter to test with OGH fully closed to isolate the wattage-ceiling question itself further.
- **PR [#176](https://github.com/theantipopau/omencore/pull/176)** — re-reviewed 2026-08-30. The process-monitoring fix from 2026-08-29 is real and correct, but two bugs from the 2026-08-19 review (keyboard "effect-freeze," iGPU Curve Optimizer gating) are **still broken**, with the keyboard bug relocated a second time. Branch is also now stale against `main`. Recommend against merging as-is; decision still pending owner call.
- **[#189](https://github.com/theantipopau/omencore/issues/189)** — exceptionally well-researched proposal (decompiled OMEN Gaming Hub's fan-control algorithm, validated a working prototype on real `8D87` hardware) for an opt-in software fan-curve controller on boards where firmware Auto under-cools in Performance mode. A genuinely new feature, not a bug fix — recorded in full in `docs/ROADMAP_v4.3.0.md` for a dedicated design pass rather than rushed in. Reporter has confirmed they're continuing to develop the standalone prototype and will share more data.
- The local HTTP/named-pipe control API — unblocked by the Core extraction, not started.
- Class-level capability defaults audit (the ~150 named board entries in `ModelCapabilityDatabase.cs` haven't been checked for silent reliance on the class-level `= true` defaults) — see `docs/ROADMAP_v4.3.0.md`.
- **[#126](https://github.com/theantipopau/omencore/issues/126)** — same capability-classifier reason-text bug as #127 (fixed, see "Linux" above), plus a bigger, unactioned question about `LinuxEcController.HasEcAccess` using a denylist rather than an allowlist for EC-write safety. See `docs/ROADMAP_v4.3.0.md`.
- **Discord (GHOST), 2026-09-02** — "OMEN key also not working" on board `8BA9`. The attached `LastOmenKeyCandidate` is **not evidence of a bug** — this exact (VK, scan) pair was already diagnosed as a real brightness-key/OMEN-key collision on a different board (GitHub #141) and is deliberately rejected by design. Needs a clean re-test before this is actionable — see `docs/ROADMAP_v4.3.0.md`.
- **Discord (PRIMUS_626), 2026-09-02** — "can't do anything is Tuning" on HP Victus 16-e0xxx (board `88ED`). Not a bug: `SupportsUndervolt=false` on this board is an intentional, already-documented conservative default. GPU OC via NVAPI should still be usable. See `docs/ROADMAP_v4.3.0.md`.
- **[#123](https://github.com/theantipopau/omencore/issues/123)** — GPU TGP capped at 80W on Linux for the OMEN Max 16-ah0xxx / RTX 5080 (board `8D41`; independently confirmed on a second BIOS/distro). Traced and confirmed **not actionable in OmenCore's own code** — see `docs/ROADMAP_v4.3.0.md` for the full trace.

---

*(Further entries added as work lands.)*
