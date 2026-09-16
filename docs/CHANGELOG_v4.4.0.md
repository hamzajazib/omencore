# OmenCore v4.4.0

**Release Date:** TBD — just started, nothing shipped yet. Rolling changelog, updated as work lands.
**Release Status:** In progress. Started 2026-09-16, one day after v4.3.1 shipped.
**Type:** TBD — currently two field reports, one a straightforward model-database addition and one
a real, still-open fan/thermal-telemetry investigation. Scope will grow from here the same way past
cycles have; see the roadmap for full detail as it develops.
**Base Version:** v4.3.1
**Tracking doc:** `docs/ROADMAP_v4.4.0.md` — full investigation detail, rejected options, and evidence trails live there; this file stays short.

---

## Added

*(nothing shipped yet this cycle)*

---

## Fixed

*(nothing shipped yet this cycle)*

---

## Investigated, Not Yet Actioned

- **[#198](https://github.com/theantipopau/omencore/issues/198)** — Victus 16-r0xxx (board `8BBE`, Intel i7-13700H + RTX 4060): wildly fluctuating CPU temperature readings (~40°C to ~95°C), fans getting stuck at max for minutes after temps normalize and not responding to profile/mode switches, suspected bad RPM readback. Dug into the attached diagnostics rather than guessing: the session log shows the CPU thermal-authority source switching from LibreHardwareMonitor (timed out after 500ms) to ACPI Thermal Zone, which then reported a genuinely frozen 27.9°C for 699 consecutive readings while CPU load swung 36% — an existing "frozen sensor" detector already logs a warning for this, but nothing currently acts on it. Separately, an "External fan reset suspected — Max mode re-applied after sustained drop" recovery heuristic fired in the same window, and thermal-protection's own max→auto restore sequence completed correctly 7 seconds later in this specific instance — but the user's report describes cases where this doesn't resolve within seconds. Strong lead, not a confirmed root cause yet: the CPU-temperature-source unreliability on this board is the most likely explanation for both symptoms (a stuck/wrong reading feeding thermal protection decisions, and the fan-recovery heuristic reacting to state it can't fully trust), but confirmed via one session's logs, not yet independently reproduced or fixed. Board also has no exact database entry yet (resolves via generic Family fallback, `RequiredCpuVendor` guard correctly prevents it from inheriting the AMD-only `8C2F` profile that a sibling board with the same WMI name pattern uses — verified working as intended, not a suspect here).
- **[#197](https://github.com/theantipopau/omencore/issues/197)** — HP Victus 15-fb3xxx (board `8DD2`, 2025 AMD), reported without a diagnostics export. Currently resolves via WMI-model-name-pattern match to the existing `15-fb3` conservative profile (low confidence, no exact ProductId entry). Reporter states no RGB keyboard on this unit — useful data point for a future keyboard-database entry. Needs a diagnostics export and (ideally) a Guided Fan Verification run before promoting to an exact, evidence-backed entry.

---
