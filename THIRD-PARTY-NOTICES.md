# Third-party notices

## NVIDIA laptop GPU power unlock (`src/OmenCore.NvidiaPower`)

The MAX/CURRENT power backend is derived from:

- **nvidia-power-control** by **timmyy123**: the original research into the driver's `romOverride` MAX policy and
  the internal NVAPI RmControl call used for CURRENT.
- **OmenCore-Prophecy** and **Prophecy Power Unlocker** by **bobshmo**: the Blackwell/Ada generalisation, the
  VBIOS and driver resolvers, the safety checks (registry backup, readback and rollback, idle-only writes) and the
  Victus fan-engine preparation.

Both authors gave permission for this code to be included in OmenCore (confirmed to the OmenCore maintainer on
the OmenCore Discord). The upstream `nvidia-power-control` repository carries no licence file, so the code is
used under that permission rather than under a published licence. If either author wants it changed or removed,
open an issue and it will be.

OmenCore adds an opt-in switch (off by default), confirmation prompts, and the Victus guard that sets HP
performance mode and pauses the fan engine around a CURRENT write; it does not write when that preparation fails.

## NvAPIWrapper.Net

Used by OmenCore's NVIDIA GPU code (`OmenCore.Core`). Licensed LGPL-3.0. It stays a separate, replaceable DLL next to OmenCore.exe.
