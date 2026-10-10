# OmenCore v4.4.5

**Release Date:** TBD
**Type:** In development. Folds in field evidence, fork contributions and user requests received after v4.4.1.
**Roadmap:** `docs/ROADMAP_v4.4.5.md`

Nothing below is called confirmed unless a reporter has verified it on real hardware. Items marked
*pending* are implemented and tested in software only.

## Fixed

- **Saved Fan Max no longer comes back by itself.** A saved Max preset was restored at launch under the
  ordinary "restore fans" switch, and choosing Quiet or Auto left the old Max as the active preset, so
  sleep/resume reapplied full-speed fans. Startup now maps a saved Max to Auto unless
  **Settings > Startup Hardware Restore > Restore saved Fan Max** is on, and direct Quiet/Auto calls now
  replace the preset that resume reapplies. Found and first fixed in bobshmo's OmenCore-Prophecy fork.
  *Pending hardware confirmation.* Tests: `StartupRestorePolicyTests`, `FanServiceSuspendTests`.
- **In-app update now reopens OmenCore.** The installer's launch entry was skipped for silent installs, so
  an update from inside the app left it closed even though the message promised a restart. The updater now
  passes `/RELAUNCH=1` and the installer starts the new version. *Pending a real end-to-end update.* A UAC
  prompt on relaunch is expected (the app requires administrator rights). Reported by Yak on Discord.

- **OMEN key action now applies immediately.** The action was read once at startup, so picking a different
  one in Settings did nothing until OmenCore was restarted. It is re-read on every key press. Test:
  `OmenKeyActionReloadTests`.
- **OMEN key can now be set to every action the service supports.** Settings only offered four of the nine
  actions. It now also offers Cycle Performance, Cycle Fan Mode, Toggle Max Cooling, Do Nothing and
  **Launch program or script**, with fields for the path and optional arguments. Requested on Discord
  (Rave-TZ).

## Added

- **Reversible HP telemetry switch** (Advanced). Stops and disables HP's analytics and diagnostics
  services (`HpTouchpointAnalyticsService`, `HPDiagsCap`, `HPAppHelperCap`, `HPSysInfoCap`,
  `HPNetworkCap`), saving each service's original start type so **Restore** puts it back exactly. Services
  you had already disabled are never re-enabled, and nothing OmenCore needs is touched. Unlike the OMEN
  Gaming Hub cleanup, nothing is deleted. Needs administrator rights. Requested on Discord (Rave-TZ).
  Tests: `HpTelemetryServiceControlTests`.
- **GPU power policy, read-only.** Tuning shows the limit the NVIDIA driver is enforcing right now, its
  default, allowed range and live draw, and notes when the enforced limit is below the driver maximum
  (an OEM/firmware policy holding the GPU down, as in #181, #123, #142). The same line is in the
  diagnostics export. It never wakes a sleeping dGPU. Tests: `NvmlPowerPolicyTests`.

## Planned for this release (see the roadmap)

- NVIDIA laptop power unlock (MAX/CURRENT), opt-in and gated, with the permission of the original authors.
  Read-only telemetry and the HP-side controls land first.
- 8BD4 single-zone keyboard (#212), 8E35 fan latency and SMU table (#222), new boards 8E9F, 8E9A and
  HyperX OMEN 15, per-key keyboard routing for 8D41/8D87, automatic fan curve (#189, default off).
- Linux reliability and keyboard work from saikiranworks' fork.

## Contributors

- **bobshmo**: the Fan Max startup/resume fix, and the Prophecy NVIDIA power work being brought in.
- **timmyy123**: the NVIDIA power-control research that Prophecy builds on.
- **saikiranworks**: Linux reliability, keyboard and daemon work.
- **Rave-TZ** and **Yak**: the requests and report behind the telemetry switch and the updater fix.
- **mbilykov**: the Gaming Hub fan-curve research behind #189.
- **VoRtam4**: the 8BD4 retest logs on #212.
