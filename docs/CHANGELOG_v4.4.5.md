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

- **Faster fan response to a temperature spike** (#222, 8E35). A custom curve was evaluated every 5 s, and a
  needed fan increase waited for a ramp-up timer that only started on one pass and was checked on the next, so
  a brief jump (alt-tab, a game loading) was answered about 7 s late and often after the heat had gone. A rise of
  8 C or more over the last applied temperature now bypasses the tick and the ramp-up delay, and a ramp-up delay
  no longer than the tick no longer costs an extra tick. Ramp-down, the dead-zone and long custom delays are
  unchanged. *Pending confirmation on the reporter's 8E35.* Tests: `FanCurveResponseTests`.

## Performance

- **GPU load fallback reads one counter set instead of ~290.** When NVAPI has no GPU load (a hybrid laptop with the
  dGPU asleep, or Afterburner shared memory), OmenCore read Windows' "GPU Engine" utilisation through one
  `PerformanceCounter` per process and engine: 292 on an idle desktop, about 144 ms per poll in a benchmark. It now
  uses a single `ReadCategory` call (about 2 ms) with the same maths, in both the main monitor and the hardware
  worker. Output is unchanged. Found from the footprint in #221's export. Tests: `GpuEngineLoadSamplerTests`.

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

- **Board entries from field exports** (all unverified until a reporter confirms): `8E9F` HyperX OMEN MAX
  16-ak1xxx (#224) and `8EEC` HyperX OMEN 15-gb0xxx (#223). Each records only what the export measured (thermal
  policy, fan count, keyboard topology) and leaves curves, undervolt and RGB off.

- **Linux: keyboard backlight suspend/resume hook** (`scripts/linux/omencore-kbd-suspend.sh`, optional,
  documented in the Linux guide). From saikiranworks' fork. Its world-writable fan PWM udev rule was left out
  on purpose: it would let any local user stop the fans.

- **NVIDIA laptop GPU power unlock** (Tuning, **off by default**). MAX is stored in the NVIDIA driver's
  `romOverride` registry value (restart needed); CURRENT is applied live through the driver's internal call and
  resets on reboot. Safety checks come from the backend: supported GPU and trusted driver build only, registry
  backup, readback with rollback, idle-only CURRENT writes, CURRENT never above the live MAX. OmenCore adds an
  enable switch, a confirmation before every write, and on Victus laptops sets HP performance mode and pauses
  the fan engine around the CURRENT write (Fan Max is preserved); if that preparation fails the write is not
  sent. Backend by timmyy123 (nvidia-power-control) and bobshmo (OmenCore-Prophecy), included with their
  permission; see `THIRD-PARTY-NOTICES.md`. *Pending hardware confirmation in OmenCore itself* (its author
  reported it working on an RTX 5060 laptop at 140 W). Tests: `NvidiaPowerServiceTests`.

## Planned for this release (see the roadmap)

- 8BD4 single-zone keyboard (#212), 8E35 SMU table (#222), new boards 8E9A and
  HyperX OMEN 15, per-key keyboard routing for 8D41/8D87, automatic fan curve (#189, default off).
- Linux reliability and keyboard work from saikiranworks' fork.

## Contributors

- **bobshmo**: the Fan Max startup/resume fix, and the Prophecy NVIDIA power work being brought in.
- **timmyy123**: the NVIDIA power-control research that Prophecy builds on.
- **saikiranworks**: Linux reliability, keyboard and daemon work.
- **Rave-TZ** and **Yak**: the requests and report behind the telemetry switch and the updater fix.
- **mbilykov**: the Gaming Hub fan-curve research behind #189.
- **VoRtam4**: the 8BD4 retest logs on #212.
