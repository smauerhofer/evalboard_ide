# SRAM Tentacle: reading/writing the EVB001 external SRAM through Kraken

Status: implemented, not yet built or hardware-tested (see "What still needs
verification" below). Delivered 2026-08-21.

## What this adds

A new "SRAM Tentacle" window (button in the Chip window's second button row,
next to the Node 708 diagnostic probes) that reads, writes, compare-exchanges,
and sets the master mask on the eval board's external SRAM (Cypress
CY62167EV18LL, 1M x 16) via AN003's "SRAM Control Cluster Mark 1" protocol.

- `Services/SramClusterPrograms.cs` — the four resident F18 node programs
  (007 data bus/control, 008 control pins, 009 address bus, 107 interface).
  Node 107's source is generated per-master (`BuildNode107Source(masterPortName)`)
  rather than static.
- `Services/KrakenSramProtocol.cs` — host-side Kraken leaf builders
  (`BuildSramReadWord`/`BuildSramWriteWord`/`BuildSramCompareExchange`/
  `BuildSramSetMask`) that puppet a memory-master node (106/108/207) to talk
  to the already-running node 107, mirroring `KrakenProtocol.cs`'s style.
- `Services/SramClusterInstaller.cs` — compiles and deploys all four cluster
  nodes for a chosen master (writes each node's `SourceCode`, compiles via
  `F18NodeCompilationService`, then `WriteRamAsync` + `JumpAsync(0x000)` via
  the existing `KrakenLiveController`).
- New SRAM methods on `KrakenSession`/`KrakenLiveController`
  (`ReadSramWordAsync`/`WriteSramWordAsync`/`CompareExchangeSramWordAsync`/
  `SetSramMasterMaskAsync`), mirroring the existing `ReadAAsync`/`WriteAAsync`
  shape exactly.
- `ViewModels/SramTentacleViewModel.cs` + `Views/SramTentacleWindow.xaml(.cs)`
  — master picker (106/108/207), Install button, ex@/ex! panel, cx? panel,
  mk! panel, status/log area. Opened from `ChipWindow` via a new
  `OnOpenSramTentacleClick` handler (same reusable-modeless-window pattern as
  Check Kraken), no changes to `ChipViewModel` needed.

## Key design decision: the degenerate single-master node 107

AN003 section 4.1 gives a full node 107 that polls three masters (106/108/207)
plus a stimulus-passing mechanism. Implemented literally and explicitly first
for verifiability, that version compiled to ~117 words — nearly double node
107's 64-word RAM budget.

Kraken only ever transiently puppets **one** master node per SRAM transaction;
no resident master program is ever left running and idle waiting on a
stimulus in this system's usage pattern. AN003 section 6.3 ("Simplifying the
Interface") offers exactly this simplification — a single-fixed-master,
no-polling, no-stimuli node 107 — for exactly this situation. That's what's
installed: `SramClusterInstaller` bakes the chosen master's local port
(right/left/up, from `KrakenTopology.PortName(107, masterCoordinate)`)
directly into node 107's source at install time, so switching masters means
re-running Install, not a runtime reconfiguration.

`mk!` is still recognised on the wire (so a stray mk! request from the UI
doesn't desync node 107's command parser) but is a **protocol no-op**: with
one fixed master, there is nothing to enable/disable or post a stimulus for.
This is disclosed in code comments on `KrakenSramProtocol.BuildSramSetMask`
and `SramClusterPrograms.BuildNode107Source`, and in the mask panel's own
on-screen reference text.

All four programs were compiled — 0 diagnostics, within the 64-word RAM
budget, entry point at address 0 — against this project's real
`Compiler/F18Compiler.cs`, in a throwaway standalone `net10.0` console
harness (no WPF/Windows dependency) built for this purpose. Final word counts:
009 = 11, 008 = 34, 007 = 37, 107 = 47 (all templated-source, i.e. the exact
string `BuildNode107Source` produces, not a hand copy).

## Known placeholder: node 007's read/write timing

AN003's own listing for node 007 comments each delay loop with the target
pulse width (e.g. "40 13 for unext" ~45ns, "50 40 for unext" ~55ns), but the
OCR of that 2010-era arrayForth screen-dump listing was too garbled to
recover the real numeric constants reliably. `Node007DataBusAndControl`
currently uses a placeholder ("63 for . unext", ~64 iterations) chosen to
generously clear the CY62167EV18LL-55's 55ns spec — **not** a transcription
of AN003's real hand-tuned values. This is flagged in the code's own XML doc
comment. Verify against the SRAM datasheet and a scope/logic analyzer (and
tighten for throughput if needed) before relying on this in hardware.

## What still needs verification

This sandbox has no .NET SDK usable for `net10.0-windows`/WPF (Linux
container) — nothing here could build or run the actual IDE project. Verified
so far: the four F18 sources compile cleanly against the real compiler
(above), and the new C# files were checked by eye against the existing
KrakenSession/KrakenLiveController/KrakenProtocol/NodeEditorViewModel/
ChipViewModel patterns for signature and namespace consistency (braces
balanced, XAML well-formed, all cross-references resolved by hand).

Not yet done, and needing Visual Studio + real hardware:
- A full `dotnet build` of the WPF project with these files added.
- Bench bring-up, incrementally: start with node 107 + one master doing a
  loop-back `mk!`/`ex@`/`ex!` sanity check before letting 007/008/009 actually
  toggle the real SRAM's WE-/CE- lines.
- Tuning node 007's placeholder delay constants against the real datasheet.