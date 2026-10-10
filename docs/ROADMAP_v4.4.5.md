# OmenCore v4.4.5 Roadmap

**Status:** planning (opened 2026-10-10, after v4.4.1 shipped).
**Theme:** fold the best of the field evidence, forks and open issues into one release without
loosening the evidence gate. Anything unconfirmed on hardware stays labelled as such.

## 1. Fork and PR review

| Source | What is in it | Decision |
|---|---|---|
| `bobshmo/OmenCore-Prophecy` (4 commits on 4.4.1) | Fan Max no longer restored at startup without an opt-in; Quiet/Auto now update the resume preset (stale-Max-on-resume bug); tests for both | **Take.** OmenCore-authored MIT code, real root cause, matches the "fans stuck high/low after resume" family. Port by hand with tests. |
| same fork | Native NVIDIA power backend: VBIOS MAX override via driver registry `romOverride00`, CURRENT via NVAPI, voltage/clock tuning, mVolt import, 22 PCI maps (~3,500 lines in `src/Prophecy.Integration`) | **Approved, with conditions (section 3).** Derives from `timmyy123/nvidia-power-control`, which ships no licence; both authors have agreed to inclusion with credit. The override lifts limits well past OEM spec, so it ships opt-in with the fork's safety checks. |
| same fork | Extra Victus CPU limits (STAPM/Fast/Slow/APU/skin), HP TPP / PCF GPU max / PLGPU controls | **Re-implement independently** from HP WMI/SMU facts, gated by model, Test Apply and auto-revert. Facts only, no code copy. |
| same fork | TuningView rewritten into tabs (GPU power, GPU tuning, CPU, Victus, Profiles, Device) | **Reference only.** The layout idea is worth a pass; the XAML is coupled to the Prophecy backend. |
| `saikiranworks/omencore` (24 commits, Linux, MIT) | Reliability mode (diagnostics store, watchdog, single-writer lock), daemon/user-prefs store, keyboard animation engine and idle/suspend backlight handling, live NVIDIA GPU power telemetry on Linux, udev rule for hp-wmi, Avalonia theme/Longevity tab | **Take selectively.** udev rule, kbd suspend/idle scripts, single-writer lock and reliability diagnostics first. Avalonia theme churn and the committed `.avalonia-build-tasks` files are excluded. Branch is 348 commits behind; port, do not merge. |
| `mbilykov/omen-fanctl` (MIT, Python daemon, #189) | Gaming Hub factory fan tables for 8D87, decompiled algorithm | Feeds `docs/189-AUTOMATIC-FAN-CURVE-DESIGN.md`; automatic curve stays default-off behind `SupportsAutomaticFanCurve`. |
| PR #147 (Neutron-0) | UI-thread reduction | Still conflicting. Port the idea by hand against current `main` if the author does not rebase. Already credited. |
| `hamzajazib`, `iamabhishekt`, `Neutron-0/NEO-omenCore`, `WoofahRayetCode`, `ujjawalkaushik1110`, older forks | Identical to or behind `main` | Nothing to merge. |

## 2. Release scope

### A. Fan control
- Port the Fan Max startup opt-in and resume-preset fix (`RestoreMaxFanOnStartup`, `ResolveStartupFanName`, preset update in `ApplyQuietMode`/`ApplyAutoMode`) with the fork's tests plus a startup log line.
- Investigate "fans pinned at ~2000 RPM in Balanced/Performance and not responding to heat" (Discord WilliamM404, #143 8DCD, #132, #131). Needs diagnostics; check latched level, watchdog and Max-flag lifecycle first.
- #222 (8E35): WMI fallback latency (~7 s) and EC failsafe stick. Measure the WMI path, keep calls off the UI thread, and consider a short-interval read-only poll.
- #189 automatic fan curve: implement the read-only `0x2C/0x2F` wrappers and the evaluation engine against the supplied factory tables, default off.

### B. RGB
- **#212 (8BD4, one-zone with numpad):** the 4.4.1 zone-count fix reaches hardware (`ZoneCount=1`) but readback is still black. Next suspects: single-zone payload layout and colour offset in the 128-byte table, and whether this board needs the per-zone command rather than the table. Use the reporter's redacted log; ask for one A/B run per hypothesis.
- #179/#151/#87 (8D41/8D87 Darfon `0D62:54BF`, HID iface 3): per-key path routing from the Jeremy/tempestnano work.
- #218 (878A), #14 (16-b0xxx): keep backlight-only handling, confirm single-mode keyboards are not offered colour writes.
- Dynamic Lighting banner and Primax experimental path: collect confirmations before widening.

### C. GPU power, boost and tuning
- Read-only first (**started**: `NvmlPowerPolicy`, in the diagnostics export; UI display next): live GPU power/limit telemetry through NVML and the NVAPI already in `OmenCore.Core` (`NvapiService`), shown beside the HP TGP/PCF state. Fixes the "TGP ceiling inherited from OGH" confusion (#181, #123, #142).
- HP-side controls (PCF GPU max, PLGPU, TPP) only where a board entry and a readback exist.
- Overclock/undervolt: keep Test Apply with 30 s auto-revert and exit revert; no new write path ships without readback.

### D. Board database
New or changed entries need the evidence gate (diagnostics export plus a verification run):
`8E9F` (#224), `8E9A` (#142, export now attached), HyperX OMEN 15-gb0xxx (#223), `8D2F` Linux (#219), `8E35` (#222, #84), `8BD4` (#212), `8E10` (#171), `8C58` (#76, #149), `8E41` (#99), `8D40` (#145), `8A44` (#158/#159), `8A26` (#66), `8D41` (#26/#60/#123/#181).

### E. Updater and installer
- In-app update now relaunches (`/RELAUNCH=1`, done after 4.4.1). Confirm end to end on a real update, including the UAC prompt.
- Reporters still see "inferred, not yet verified" after updating. Flip `UserVerified` per board only after the reporter confirms each feature.

### F. Linux
- Port from the saikiranworks fork: udev rule for hp-wmi, kbd suspend/idle scripts, single-writer lock, reliability diagnostics.
- #219 / #84 / #26: kernel limits, so document the distro/kernel steps and the hp-wmi allowlist, no code workaround.

### G. User requests (Discord, Rave-TZ, 2026-10-10)
- **Speed profiles (Unleashed etc.):** already in Tuning/Performance modes; Unleashed is gated by board. Check the labels and the board gating read clearly, no new backend.
- **OMEN key reassignment:** `OmenKeyService` already offers ShowQuickPopup/ShowWindow/CyclePerformance/CycleFanMode/ToggleMaxCooling/LaunchExternalApp/DoNothing. Gap: no "run a command/script" and no way to see the key is captured. Add both.
- **Disable HP telemetry/services:** the only path today is the destructive OGH cleanup (`sc delete`). Add a **reversible** option: stop and set the HP analytics/helper services (`HpTouchpointAnalyticsService`, `HPAppHelperCap`, `HPDiagsCap`, ...) to Disabled, remember each original start type, one-click restore. Never touch services OmenCore needs.

## 3. NVIDIA power unlock: approved for 4.4.5
The Discord thread (RTX 5060 raised from 90 W to 115-140 W) shows real demand. `timmyy123`
(nvidia-power-control) and `bobshmo` (Prophecy) have both agreed to its inclusion, with credit
(maintainer-confirmed 2026-10-10).

Conditions for shipping it:
1. **Written permission on record.** The upstream repo has no licence file, so get each author's
   approval written down (an issue comment, or a LICENSE/NOTICE in their repo) and link it from
   `THIRD-PARTY-NOTICES`. Credit both in `CONTRIBUTORS.md` and the changelog.
2. **Opt-in "advanced" feature, default off.** Warn plainly: limits above OEM spec, thermal and
   power-delivery risk, may void warranty. Windows only, laptop RTX 40/50 only, one NVIDIA GPU.
3. **Keep the fork's safety model:** driver and VBIOS identity validation, no-op CURRENT write to
   validate, registry backup before any MAX override, rollback on readback mismatch, writes only
   while the GPU is idle, CURRENT never above live MAX, reboot-state checks, one writer at a time.
4. **Use OmenCore's own Test Apply / exit-revert pattern** so a bad value does not survive a crash.
5. **Do not bundle** NVFlash, ROMs, driver binaries or personal profiles. `LLT.NvAPIWrapper.Net`
   is LGPL-3.0: keep it as a replaceable DLL with notices.
6. **Evidence gate:** nothing is called confirmed beyond what was run on hardware (the fork's author
   exercised an RTX 5060 laptop; the other 10 models are identification-only).
7. Land read-only telemetry and the HP-side controls first, then the write path behind the gate.

Related power tooling:
- **AMD CPU:** already covered. `RyzenSmu`/`AmdPmTable` (PawnIO) is the RyzenAdj equivalent, with
  STAPM/Fast/Slow/Tctl and Curve Optimizer, and it refuses unmapped CPUs. Widening it means mapping
  more SMU layouts from PM-table exports (Ryzen 7 260, 9 8940HX).
- **AMD GPU:** no equivalent. The nearest tool, MorePowerTool, is Windows-only and AMD has blocked
  it for RDNA 3 and later. Mobile Radeon limits are enforced by the vBIOS/EC, so there is nothing
  to port.
- **Intel:** PL1/PL2 and undervolt are separate from this and already handled in the existing tuning code.
- **mVolt (b00nz):** optional external profile editor. Link to it; do not bundle.

## 4. Housekeeping
- Add `bobshmo` and `saikiranworks` (already listed) to `CONTRIBUTORS.md` on merge; credit `mbilykov` for #189.
- Reply on #224, #223, #222, #212, #142 asking only for what is missing.
- Keep `docs/V4.4.5_IMPLEMENTATION_STATUS.md` once work starts; update `CLAUDE.md` open threads.

## 5. Status (2026-10-11): release candidate
- **In the build (software-tested, see `CHANGELOG_v4.4.5.md` for what is still unconfirmed):** Fan Max startup opt-in
  and resume-preset fix; updater relaunch; reversible HP telemetry switch; read-only NVML GPU power policy; OMEN key
  action reload, all actions and program/arguments; faster spike response (#222); auto-hidden taskbar fix; boards
  `8E9F`, `8EEC`, `8A13`; Darfon `0D62:30BF` keyboard route for `8E9F`; Linux keyboard suspend hook and single fan
  writer; GPU engine counters replaced by one category read; memory breakdown in the diagnostics export; RGB payload
  probe for #212; opt-in NVIDIA power unlock (Resolve VBIOS, MAX, CURRENT, Victus guard, update-proof state);
  opt-in factory fan curve for `8D87` (#189).
- **Open, waiting on people:** #212 (probe result), #222 (PM-table dump), #142/8E9A (export), 8BB3 (export), #221
  (reporter reply on the temperature source), Linux per-key RGB (tester).
- **PR #147** reviewed and declined as written (it drops the uptime timer start and can stall the tray); the author
  was invited to rework the tray change-detection alone.
- **Not taken from saikiranworks' fork:** the world-writable fan PWM udev rule, because any local user could stop
  the fans.
