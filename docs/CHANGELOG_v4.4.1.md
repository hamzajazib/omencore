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

---

## Added

### Board `8BBE` (Victus 16-r0xxx, Intel) Given an Exact Entry

[#211](https://github.com/theantipopau/omencore/issues/211): was Family fallback only since `#172`;
WMI fan control and V1 policy confirmed live in a real diagnostics export.

---

## Investigated, Not Fixed

### Zone-Colour RGB Is Very Likely Broken on Every Single-Zone-Topology Board

[#212](https://github.com/theantipopau/omencore/issues/212) (board `8BD4`): the firmware's own
topology probe reports a single RGB zone, but `WmiBiosBackend`/`EcDirectBackend` both hardcode
"4 zones" and never check. Not fixed — the real single-zone byte layout isn't known, and the one
flag that looked like a quick fix (`HasFourZoneRgb`) doesn't control zone count at all; flipping it
removes colour control entirely instead of correcting it. See the roadmap for the full write-up and
what's needed to actually fix it.

---
