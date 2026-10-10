# OmenCore v4.4.5 Roadmap

**Status:** planning (opened 2026-10-10, after v4.4.1 shipped).
**Theme:** fold the best of the field evidence, forks and open issues into one release without
loosening the evidence gate. Anything unconfirmed on hardware stays labelled as such.

## 1. Fork and PR review

| Source | What is in it | Decision |
|---|---|---|
| `bobshmo/OmenCore-Prophecy` (4 commits on 4.4.1) | Fan Max no longer restored at startup without an opt-in; Quiet/Auto now update the resume preset (stale-Max-on-resume bug); tests for both | **Take.** OmenCore-authored MIT code, real root cause, matches the "fans stuck high/low after resume" family. Port by hand with tests. |
| same fork | Native NVIDIA power backend: VBIOS MAX override via driver registry `romOverride00`, CURRENT via NVAPI, voltage/clock tuning, mVolt import, 22 PCI maps | **Do not merge code.** Derives from `timmyy123/nvidia-power-control`, which ships **no licence** (all rights reserved by default) and whose repo is full of driver-patching/vulnerable-driver tooling. The override also lifts limits far past OEM spec (hardware/warranty risk). See section 3. |
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
- Read-only first: live GPU power/limit telemetry through the NVML/NVAPI already in `OmenCore.Core` (`NvapiService`), shown beside the HP TGP/PCF state. Fixes the "TGP ceiling inherited from OGH" confusion (#181, #123, #142).
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

## 3. NVIDIA power unlock: open decision
The Discord thread (RTX 5060 raised from 90 W to 115-140 W) shows real demand, but the only working backend is unlicensed and registry/VBIOS-based. Options, in order of preference:
1. Ship read-only telemetry plus HP-side controls (section 2C).
2. Ask `timmyy123` and `bobshmo` for an explicit MIT-compatible licence; if granted, revisit with an opt-in "advanced" gate, Test Apply, reboot-state checks and clear damage/warranty warnings.
3. Point users to the fork, credited, as a separate build. Do not bundle it.

## 4. Housekeeping
- Add `bobshmo` and `saikiranworks` (already listed) to `CONTRIBUTORS.md` on merge; credit `mbilykov` for #189.
- Reply on #224, #223, #222, #212, #142 asking only for what is missing.
- Keep `docs/V4.4.5_IMPLEMENTATION_STATUS.md` once work starts; update `CLAUDE.md` open threads.
