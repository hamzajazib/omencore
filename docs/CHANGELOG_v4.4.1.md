# OmenCore v4.4.1

**Release Date:** TBD — in progress. Rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-09-25, one day after v4.4.0 shipped.
**Type:** Field-report follow-up to 4.4.0. Fixes two real bugs found from post-release diagnostics,
adds one board entry, and pulls in a genuine fix found reviewing a community fork.
**Base Version:** v4.4.0
**Tracking doc:** `docs/ROADMAP_v4.4.1.md` — full investigation detail, evidence trails, and what's
still open live there; this file stays short.

---

## Fixed

### Watchdog Failsafe Could Release Onto a Still-Hot Machine, and Never Re-Applied Itself

Found reviewing a community fork (`ujjawalkaushik1110/omencore`), reimplemented directly against
`main`. Two gaps: the frozen-sensor failsafe released back to BIOS Auto on the very next telemetry
sample regardless of what that sample actually read, and once active it applied the 90% fan speed
exactly once — anything that reset fan state afterward (OGH, a firmware reassert) could undo it with
nothing watching. Release now needs temperatures at or below 65°C held for 15 seconds; the failsafe
reapplies itself every 15 seconds for as long as it stays active.

### Guided Fan Verification's 100% Test Could Report a Board as Failing When Firmware Just Ignored Max

Board `88F8` ([#207](https://github.com/theantipopau/omencore/issues/207)): the 100% test's
`SetFanMax` call was accepted by firmware but never moved the fans past the previous test point —
the same "accepts Max, ignores it" bug already fixed elsewhere this cycle, reached through this
method's own one-shot apply instead. Now retries with a direct level write before giving up. Also
fixed: the existing fallback path sent a hardcoded level regardless of the board's real ceiling.

### Tray Offered GPU Power Boost on Victus Boards Whose Backend Always Refuses It

Three independent Victus exports (`8C2F` in [#155](https://github.com/theantipopau/omencore/issues/155)
and [#184](https://github.com/theantipopau/omencore/issues/184), `88F8` in
[#207](https://github.com/theantipopau/omencore/issues/207)) showed "Show GPU Power Boost: Yes"
beside the backend's own "skipped - HP Victus does not support WMI TGP/PPAB control". The display
gate treated "WMI BIOS is present" as GPU-power evidence. It now mirrors the backend's Victus rule
exactly: hidden unless the board's entry explicitly opts in. Non-Victus boards are unchanged.

### Guided Fan Diagnostic Could Be Overridden by the Keepalive From Max or Manual Mode

The fan keepalive timer already stood down during a Guided Fan Diagnostic, but only from preset
modes. Started from Max or manual control, its reasserts could fight the diagnostic's own writes.
Now covers all three. Found reviewing PR
[#210](https://github.com/theantipopau/omencore/pull/210).

---

## Added

### Board `8BBE` (Victus 16-r0xxx, Intel) Given an Exact Entry

[#211](https://github.com/theantipopau/omencore/issues/211): was Family fallback only since `#172`;
WMI fan control and V1 policy confirmed live in a real diagnostics export.

### Board `88F8` (Victus 16-d0xxx, Intel) Given an Exact Entry

[#207](https://github.com/theantipopau/omencore/issues/207): was Family fallback, which also cut the
firmware's two fans down to one. Flags from the reporter's 4.4.0 export: WMI level writes verified at
30%/60%, two fans, backlight-only keyboard. Curves, GPU boost and undervolt left off pending evidence.

### Victus 16-r0xxx Intel No Longer Misidentified as the Ryzen Board

[#115](https://github.com/theantipopau/omencore/issues/115),
[#172](https://github.com/theantipopau/omencore/issues/172): board `8BBE` resolved to the AMD `8C2F`
profile by name pattern. The new `8BBE` entry above resolves it by ProductId.

### Zone-Colour RGB: WMI Backend Now Declares Its Real Zone Count — Implemented, Pending Confirmation

[#212](https://github.com/theantipopau/omencore/issues/212) (board `8BD4`): the firmware's own
topology probe reports a single RGB zone, but `WmiBiosBackend`/`EcDirectBackend` both hardcoded
"4 zones" and never checked, so `HpWmiBios.SetColorTable`'s own payload told the firmware something
it wasn't — even on a follow-up test sending the identical colour into all four zone slots.
`WmiBiosBackend.ZoneCount` now reads the live topology probe and passes the real count into
`SetColorTable`'s byte 0 instead of a hardcoded `4`; the 4-slot colour payload itself is unchanged,
since the real single-zone byte layout still isn't known — this tests only the one piece there's hard
evidence for. `EcDirectBackend` untouched (different, older-generation mechanism, no declared-zone-
count byte). **Not yet confirmed on real hardware.** The flag that looked like a quick fix
(`HasFourZoneRgb`) still doesn't control zone count at all and was left at `true`; flipping it
removes colour control entirely instead of correcting it. See the roadmap for the full write-up.

---

## Investigated, Not Fixed

### Zone-Colour RGB On `EcDirectBackend`, and the Real Single-Zone Byte Layout If the Above Doesn't Confirm

`EcDirectBackend.ZoneCount` still hardcodes `4` — deliberately left alone this pass, see above. And
if the `WmiBiosBackend` byte-0 fix doesn't resolve `#212` on real hardware, the real single-zone
`ColorTable` layout is still unknown. See the roadmap for what's needed either way.

---
