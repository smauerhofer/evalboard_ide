# SRAM Simulator (node 707): CVM test infrastructure

Status: implemented, compiled with zero diagnostics against this project's real
`Compiler/F18Compiler.cs` (verified in a standalone `net10.0` console harness
built for this purpose -- see "What was verified" below). Not yet built inside
the actual WPF project or exercised against real hardware. Delivered
2026-08-24, as the first piece of the CVM (C virtual machine) test
infrastructure Stefan asked for: before the CVM itself can be built (its exact
semantics -- bytecode vs. direct compilation, instruction set, memory model --
are still to be defined), the SRAM-shaped memory it will need to exercise has
to be testable without the real external SRAM hardware wired up. This is that
piece.

## What this adds

A new "SRAM Simulator (707)" window (button in the Chip window's second
button row, next to SRAM Tentacle and the Node 708 diagnostic probes) that
reads, writes, compare-exchanges, and no-ops the master mask against a small
software-simulated SRAM resident entirely on node 707 -- Tentacle 1 position
0, one hop from the Kraken head (708).

- `Services/SramSimulatorPrograms.cs` -- node 707's resident F18 source: a
  16-word zero-initialized backing array (`sim-mem`, addresses 0x000-0x00F)
  followed by five subroutines (`sram-read`/`sram-write`/`sram-cx`/
  `sram-mask`/`echo`) with the EXACT SAME stack signatures as
  `SramClusterPrograms.BuildMasterSupportSource` builds for a real
  memory-master node (106/108/207).
- `Services/SramSimulatorInstaller.cs` -- compiles and deploys just this one
  node, via `KrakenLiveController.WriteRamAsync` only (never `JumpAsync`, so
  node 707 stays puppetable indefinitely -- the same resident-support-code
  pattern the real SRAM cluster's master node uses). No Tentacle
  reorganization is needed or performed: node 707 is already Tentacle 1
  position 0 in the default fixed `KrakenTopology`, reachable the instant any
  Kraken is erected.
- `ViewModels/SramSimulatorViewModel.cs` + `Views/SramSimulatorWindow.xaml(.cs)`
  -- Install button, echo panel, ex@/ex! panel, cx? panel, mk! panel,
  status/log area. Opened from `ChipWindow` via a new
  `OnOpenSramSimulatorClick` handler (same reusable-modeless-window pattern as
  SRAM Tentacle / Check Kraken).

## Key design decision: zero new host-side protocol code

Node 707's five subroutines were written to keep the exact stack contract
`KrakenSramProtocol`'s existing leaf builders
(`BuildSramReadWord`/`BuildSramWriteWord`/`BuildSramCompareExchange`/
`BuildSramSetMask`/`BuildEchoTest`) already produce for a real master node --
including AN003's sign-inversion convention for write/compare-exchange
addresses. That means the entire host-side transport --
`KrakenSramProtocol.cs`, and the generic, `route`-parameterized SRAM methods
already on `KrakenSession`/`KrakenLiveController`
(`ReadSramWordAsync`/`WriteSramWordAsync`/`CompareExchangeSramWordAsync`/
`SetSramMasterMaskAsync`/`EchoTestAsync`, none of them ever hardcoded to
106/108/207) -- works against node 707 completely unmodified. Only node 707's
own resident source, and a small single-node installer, needed to be written.
CVM code exercised against this simulator can later point at the real SRAM
cluster (a different `subroutineAddress` set, from `SramClusterInstaller`
against a real master) with no protocol-level changes at all.

## Key design decision: no master/007/008/009/107 layering

The real cluster needs four separate nodes because node 107 talks to
physically real SRAM control/address/data pins across three more nodes that a
memory-master node reaches by relaying over a wire. There is no physical chip
here -- node 707 just reads and writes its own local RAM directly, so it
plays both the "master" role (puppeted by Kraken exactly like a real master)
and the "memory" role in one node. No B-port handshake, no Tentacle 3
reorganization, no risk of stranding any other node.

Each subroutine had to recover values AN003's real protocol sends inverted
(for write/compare-exchange, using 'inv' locally) since there is no
downstream node 107 to do that decoding anymore -- the real master
subroutines just relay those words on as-is.

## Key design decision: small, single-page backing store

A node has 64 RAM words total, split between this simulator's own code (19
words) and its backing array (16 words, `CapacityWords`/`CapacityMask` in
`SramSimulatorPrograms.cs`) -- nowhere near the real chip's 1M x 16 words.
This is deliberate: CVM test/verification scaffolding, not a capacity claim.
`ex@`/`ex!`/`cx?`'s 20-bit page:address addressing is still accepted, for
wire compatibility, but "page" is simply discarded -- the simulator has
exactly one page, sized `CapacityWords` words. Growing this later (a bigger
`CapacityWords`, or spreading the backing array across additional free
Tentacle-1 nodes such as 706/705) is a follow-up, not a redesign.

`mk!` is a protocol no-op here, for the same reason AN003 section 6.3's
degenerate single-fixed-master node 107 made it one: with exactly one
simulated interface, there is nothing to enable, disable, or post a stimulus
for. It just echoes the mask back.

## What was verified

A .NET 10 SDK was available in the environment this session ran in (an
improvement over the entirely-offline sandbox earlier work on this project
had -- see `SRAM_Kraken.md`/the node-300 investigation docs). A standalone,
non-WPF `net10.0` console harness referencing this project's real
`Compiler/F18Compiler.cs`/`F18InstructionSet.cs`/etc. directly (no NuGet
packages needed for the compiler itself) confirmed:

- `SramSimulatorPrograms.BuildSource()` compiles with **zero diagnostics**
  against `F18CompilerOptions.ForRam(707)`: 35 of 64 RAM words used (16 for
  the backing array, 19 for all five subroutines), comfortable headroom to
  grow.
- Symbols resolve as expected: `sram-read` 0x010, `sram-write` 0x013,
  `sram-cx` 0x017, `sram-mask` 0x020, `echo` 0x021 -- the same names
  `SramSimulatorInstaller` looks up from the compiled symbol table, the same
  way `SramClusterInstaller` does for the real cluster's master support code.
- Each subroutine's stack effect was hand-traced word-by-word against
  `KrakenSramProtocol`'s real host-side push order (see the code comments in
  `SramSimulatorPrograms.cs` for the full trace of each op).

**What this does NOT confirm**: a full `dotnet build` of the actual WPF
project was attempted and failed only because NuGet (`api.nuget.org`) is not
reachable from this sandbox's network allowlist (`System.IO.Ports`/
`System.Management`/`YamlDotNet` could not be restored) -- not because of any
error in the new code itself. The new C# files
(`SramSimulatorInstaller.cs`/`SramSimulatorViewModel.cs`/
`SramSimulatorWindow.xaml(.cs)`/the `ChipWindow` changes) were checked by eye,
member-by-member, against the real signatures of everything they call
(`Ga144ChipConfiguration.GetNode`, `Ga144NodeConfiguration.SourceCode`/
`Enabled`, `KrakenLiveController.IsOperational`/`IdlePolicy`/`WriteRamAsync`/
the five SRAM methods, `ChipViewModel.Chip`/`RomLibrary`/`Project`/
`KrakenController`/`KrakenRoutes`, `ProjectViewModel.Model.UserMacros`) --
grepped directly out of the real source, not assumed -- but nothing here
could actually compile or run the WPF assembly itself. There is also still no
F18 instruction-level simulator in this project, so the actual runtime
behavior of ex@/ex!/cx?/mk!/echo against a real or emulated running node has
not been exercised, only the compiled output and the hand-traced stack
effects.

## What's still needed

- A real `dotnet build` inside Visual Studio (this sandbox cannot reach
  NuGet).
- Bench bring-up against real hardware: Install, then `echo` first (isolates
  Kraken's push/call/return plumbing to node 707 itself, independent of the
  read/write/compare-exchange logic -- same reasoning as the real SRAM
  Tentacle's own echo diagnostic), then ex@/ex!/cx?/mk! sanity checks.
- The CVM itself: its exact semantics (bytecode interpreter vs. direct C-to-F18
  compilation, instruction set, memory model, how/whether it uses this
  simulator's SRAM-shaped memory for program data) are still to be defined --
  this simulator and the existing generic `KrakenNodeControlWindow` (already
  usable against any node reachable via Kraken, including a future CVM
  interpreter resident on a Tentacle-1 node near 707/708) are the
  infrastructure the CVM work will build on next.
