# OmenCore v4.4.5

**Release Date:** TBD (release candidate, built and tested, not yet tagged)
**Type:** Feature and field-report release. Folds in field evidence, fork contributions and user requests received after v4.4.1.
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
  passes `/RELAUNCH=1` and the installer starts the new version. **This takes effect from the update after 4.4.5:** 
  4.4.1's updater does not send the flag, so updating 4.4.1 to 4.4.5 from inside the app will still leave OmenCore closed 
  (start it again by hand). *Pending a real end-to-end update.* A UAC
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

- **Maximised window no longer covers an auto-hidden taskbar.** Windows reports the whole screen as the work area
  when the taskbar auto-hides, so a maximised OmenCore sat on top of it. A 2 px strip on the taskbar's edge is now
  left free so it still pops up. Requested on Reddit (Mega_duck_duck). *Pending confirmation on an auto-hide setup.*
  Tests: `TaskbarAutoHideTests` (geometry only).

- **Quieter log with RTSS running.** The "conflicting application" warning was repeated every minute for as long as RivaTuner was open; it is now logged once per change.

## Performance

- **Diagnostics export shows where the memory sits.** `resource-footprint.txt` gains a "[Memory Breakdown]" block: GC heap by generation (with fragmentation), committed and pinned counts, private bytes minus the managed heap (a native estimate), loaded assemblies and the eight largest modules. Prompted by #221, where the main app held ~280 MB private. Tests: `MemoryBreakdownTests`.

- **GPU load fallback reads one counter set instead of ~290.** When NVAPI has no GPU load (a hybrid laptop with the
  dGPU asleep, or Afterburner shared memory), OmenCore read Windows' "GPU Engine" utilisation through one
  `PerformanceCounter` per process and engine: 292 on an idle desktop, about 144 ms per poll in a benchmark. It now
  uses a single `ReadCategory` call (about 2 ms) with the same maths, in both the main monitor and the hardware
  worker. Output is unchanged. Found from the footprint in #221's export. Tests: `GpuEngineLoadSamplerTests`.

## Added

- **Factory fan curve in Performance mode, 8D87 only, experimental and off by default** (#189). On the OMEN MAX 16-ak0xxx the firmware's own Auto parks the fans at ~3,400/3,600 RPM with the CPU at 92-99 C. HP's Gaming Hub covers this with a software curve; mbilykov extracted its tables and validated a standalone version. OmenCore now has that curve (CPU, GPU and the IR chassis sensor, highest wins, CPU-to-GPU level pairing from the board's own `0x2F` table, 92 C emergency to 100%). Turn it on in Settings > Startup Hardware Restore. It runs only while Performance mode is active and the fans are on Auto; a user preset, Max, fan diagnostics, thermal protection or sleep make it stand down without touching the fans, and switching it off (or leaving Performance mode) hands the fans back to BIOS Auto. Three failed writes in a row also hand them back. *Implemented from the reporter's numbers; not yet run by OmenCore on an 8D87.* Tests: `FactoryFanCurveTests`, `AutomaticFanCurveControllerTests`, `FanServiceSuspendTests`.

- **Linux: single fan writer.** The daemon and `omencore-cli fan` set/curve/boost commands now share one lock (`/run/omencore/writer.lock`, or the temp folder when not root), so a CLI command no longer races the daemon's curve engine and gets overwritten seconds later. The second caller is refused with who holds it; the kernel drops the lock if the owner crashes. `omencore-cli diagnose` shows "Fan writer: free / held by ...". Idea from saikiranworks' fork, written fresh. Tests: `WriterLockTests`.

- **RGB payload probe** (Diagnostics > Keyboard, advanced) for boards whose keyboard ignores the standard colour command (#212, 8BD4). It sends five candidate single-zone colour tables one at a time (pure red, 4 s apart), logs each with the firmware readback, and the owner reports which numbered step lit the keyboard. Step 1 is the control (what 4.4.1 sends). These are hypotheses to test, not fixes. Tests: `ColorTableProbeTests`.

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
  16-ak1xxx (#224), `8E9A` HyperX OMEN MAX 16t-ah100 (#142; firmware reports 3 fans, GPU power verified, per-key keyboard routed as a guess), `8EEC` HyperX OMEN 15-gb0xxx (#223) and `8A13` OMEN 16-b1xxx (#225; four-zone colour confirmed by eye). `8E9F` keyboard (Darfon `0D62:30BF`) now
  routes to the per-key backend as an unverified guess (#224). Each records only what the export measured (thermal
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
  permission; see `THIRD-PARTY-NOTICES.md`. The panel follows bobshmo's order: Resolve VBIOS (needs an `nvflash64.exe`
  you place next to OmenCore.exe, or pick a ROM dump), Apply MAX, restart, Apply CURRENT. Driver and VBIOS
  validation is stored in ProgramData, so an update does not discard it (the bug bobshmo just fixed in
  v4.4.1-prophecy.5). *Pending hardware confirmation in OmenCore itself* (its author
  reported it working on an RTX 5060 laptop at 140 W). Tests: `NvidiaPowerServiceTests`.

## Needs field confirmation

Everything here is implemented and tested in software. Please report what you see.

- **8BD4 owners (#212):** run Diagnostics > Keyboard > RGB Payload Probe and say which numbered step lit the keyboard.
- **8E9F owners (#224):** try the Lighting page; the Darfon `0D62:30BF` route is a guess from HP's device list.
- **8D87 owners (#189):** try the factory fan curve in Performance mode and compare temperatures with firmware Auto.
- **8E9A owners (#142):** try the Lighting page (the keyboard route is a guess) and tell us the keyboard's HID ids (Device Manager).
- **8A13 owners (#225):** MUX is still off for this board; a later build will offer a test.
- **NVIDIA laptops:** the power unlock has been run by its authors, not yet through OmenCore. MAX needs a restart; CURRENT resets on reboot.
- **Everyone:** an in-app update from 4.4.1 should reopen OmenCore (a UAC prompt is expected); an auto-hidden taskbar should
  stay reachable with OmenCore maximised; a saved Max fan preset should no longer return after a restart or sleep.

## Not in this release

- 8BD4 single-zone keyboard fix (#212): waits on the probe result.
- 8E35 SMU table (#222): needs a PM-table dump. 8BB3 (OMEN Transcend 16): needs an export.
- Linux per-key RGB for the 2025 MAX boards (#179, #151, #87): Windows already drives these; the Linux port waits on a tester.
- The automatic fan curve for boards other than 8D87 needs each board's own tables and a hardware check.

## Contributors

- **bobshmo**: the Fan Max startup/resume fix, and the Prophecy NVIDIA power work being brought in.
- **timmyy123**: the NVIDIA power-control research that Prophecy builds on.
- **saikiranworks**: Linux reliability, keyboard and daemon work.
- **Rave-TZ** and **Yak**: the requests and report behind the telemetry switch and the updater fix.
- **mbilykov**: the Gaming Hub fan-curve research behind #189.
- **VoRtam4**: the 8BD4 retest logs on #212.
