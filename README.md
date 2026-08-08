# GA144 Evalboard IDE

A Windows WPF IDE for GreenArrays EVB001 and EVB002 boards, targeting **.NET 10** and Visual Studio 2026.

## Current capabilities

- Enumerates Windows serial interfaces and reads FTDI Plug-and-Play identity through WMI.
- Decodes GreenArrays FTDI serial numbers such as `GAEVB001S0121AA`.
- Creates and updates a persistent list of physical EVB001/EVB002 boards.
- Keeps physical boards independent from software projects.
- Remembers the selected board and selected project independently.
- Maps USB A, B, and C to stable FTDI identities rather than volatile COM numbers.
- Actively verifies Host or Target node 708 with the proven reset, asynchronous boot, and challenge-response detector.
- Displays EVB001 and EVB002 board artwork with clickable jumper, FTDI, Host-chip, and Target-chip overlays.
- Stores all persistent state in YAML.
- Supports multiple software projects containing Host and Target node source, compiled images, startup registers, stacks, and user macros.
- Provides system-wide GA144 ROM source and system macros.
- Can install an independent three-tentacle Kraken topology on the Host chip, Target chip, or both.
- Draws directed tentacle arrows between GA144 nodes and opens a live Online Kraken monitor from a tentacle-controlled node.

## Requirements

- Windows 10 or Windows 11
- Visual Studio 2026 with the .NET desktop development workload
- .NET 10 SDK

## Open and run

1. Open `Ga144EvalboardIde.slnx` in Visual Studio 2026.
2. Restore NuGet packages.
3. Build and run `Ga144.Evb.Ide`.
4. Attach one or more EVB001/EVB002 USB interfaces.
5. The board list is updated automatically when a recognized GreenArrays FTDI serial number is found.

A configuration path can be supplied explicitly:

```text
Ga144.Evb.Ide.exe --config D:\Projects\MyGa144Workspace\workspace.yaml
```

The default files are:

```text
%LOCALAPPDATA%\Ga144EvalboardIde\workspace.yaml
%LOCALAPPDATA%\Ga144EvalboardIde\ga144-rom.yaml
```

Workspace saves are atomic. The previous workspace is retained as `workspace.yaml.bak`.

## Boards and projects

The two left-hand lists are intentionally independent:

- **Boards** represent physical hardware: model, board serial number, USB A/B/C FTDI bindings, last-seen information, and jumper state.
- **Projects** represent software: Host and Target GA144 node source, RAM images, startup configuration, and project macros.

Selecting a board changes the board image and serial assignments. Selecting a project changes the node source and startup data opened when Host or Target is clicked. Either selection can be changed without changing the other.

Both selections are persisted in `workspace.yaml` as `activeBoardId` and `activeProjectId`.

## FTDI board detection

The IDE recognizes the observed GreenArrays convention:

```text
GA<model>S<board-serial><physical-port><FTDI-channel>
```

Examples:

```text
GAEVB001S0121AA  -> EVB001, board 0121, USB A
GAEVB001S0121BA  -> EVB001, board 0121, USB B
GAEVB001S0121CA  -> EVB001, board 0121, USB C
```

Interfaces with the same model and board serial are grouped into one board entry. A newly connected recognized board is added automatically. Unrecognized hardware can be added and assigned manually.

The serial-number convention is used as a strong identity hint. Active node-708 probing remains a separate electrical verification for USB A and USB C.

## Node source, ROM, and macros

Open a Host or Target chip and click a node to open the node editor. The editor keeps the existing RAM/startup workflow and exposes these additional tabs:

- **RAM source** — project-specific source for the selected node.
- **ROM source** — system-wide ROM source for the same node. ROM is compiled before RAM, and its exported names are automatically visible to RAM.
- **Expanded macro source** — the exact ROM and RAM text after recursive macro expansion.
- **Compiler output** — diagnostics, separate ROM/RAM listings, and exported symbols.
- **RAM and startup** — compiled RAM, entry point, registers, and stacks.
- **System ROM image** — compiled ROM words stored in `ga144-rom.yaml`.

The macro editor is available from the main toolbar through **Edit F18 macros** and remains available in the selected-project panel through **F18 macros**. System macros are stored in `ga144-rom.yaml` and may be used by ROM or RAM. Project user macros are stored in `workspace.yaml` and may be used only by RAM. Nested textual imports use:

```forth
macro-name import
```


## Kraken topology

Each project chip has an independent Kraken switch in the GA144 chip window. Installing it does not remove or overwrite RAM source, ROM source, macros, startup registers, or other project state.

The built-in layout uses node `708` as the Kraken head. This is a natural three-way head for the EVB workflow because its west, east, and south COM links feed three independent tentacles while node 708 remains the asynchronous serial endpoint. The IDE installs three disjoint adjacent-node paths that cover all 143 non-head nodes exactly once:

- **T1 / West:** 50 nodes, starting at 707.
- **T2 / East:** 46 nodes, starting at 709.
- **T3 / South:** 47 nodes, starting at 608.

The longest tentacle is only four nodes longer than the shortest. Every node is labeled in the chip view with the tentacle number and zero-based position. Tooltips also show the incoming port, the next node, the outgoing COM port, and the corresponding B-register port address. The head is marked `K HEAD`. Colored arrows in the enlarged chip grid show the actual direction of each stored path, including the three edges leaving node 708.

The topology is persistent project metadata. It does not rewrite a node's existing source or startup B register. Host and Target Kraken settings are saved independently.

### Online Kraken node control

Open a tentacle-controlled node and press **Online Kraken…**. The separate monitor window resolves the currently selected physical board to USB A for Host or USB C for Target, then offers **Connect & erect Kraken**. Connecting is deliberately destructive to the running chip: the selected GA144 is reset, a small serial-reply helper is loaded into the RAM of head node 708, and the three stored tentacles are erected by port execution. No RAM or ROM words in the controlled node are consumed by Kraken.

The monitor provides:

- live RAM read/write for all 64 words;
- live ROM read-only access for all 64 words;
- A register read/write and IO (`0x15D`) read/write;
- parameter-stack read/write for 10 words and return-stack read/write for 9 words;
- the known Kraken P focus port plus an explicit **Jump / detach** operation;
- B write with an explicit warning that F18A B is write-only and changing it can break the remainder of the tentacle.

The hardware does not provide a direct read instruction for B, P, or I. The monitor therefore does not invent readback values: B is shown as configured/expected, P is shown as the known incoming Kraken port while attached, and I is marked unavailable. T/S and R are represented by the two stack views.

Reads use the Kraken `r1` return path. Node 708 converts the returned 18-bit word into serial bytes with a carrier-clocked reply scheme: the PC supplies `0x00`/`0xFF` UART carrier pairs and the head mirrors one carrier per returned bit. This keeps the serial bit timing supplied by the FTDI UART instead of by an F18 timing loop. The existing node-708 detector and its verified 921600-baud boot encoder are not changed.

As with active detection, J23 and the applicable J20/J22 reset path must be installed. J26 `NO BOOT` is recommended for deterministic Host online sessions because **Connect & erect Kraken** resets the Host and immediately takes control through its asynchronous boot node.

After that one permitted erection, the runtime is persistent: the original `SerialPort` object remains open and is reused for all checks and node windows. Node 708 stays on its asynchronous ROM `ser-exec` continuation path (`0x0AE`) between boot frames; no later Kraken transaction returns to `cold`, toggles RTS, closes/reopens the serial port, or performs an active node-708 probe. While any live Kraken exists, background serial scans are metadata-only. **Check Kraken** additionally performs a 100 ms post-check idle read to verify that the same live session remains usable after the complete 143-node test.

## Important node-708 probe behavior

Active detection is destructive to the running GA144 state. It:

- opens the port at 921600 baud, 8-N-1, with DTR high;
- drives RTS low to assert RESET- and high to release it;
- rejects a pre-existing loopback while reset is asserted;
- sends the verified asynchronous boot stream;
- loads a nine-word mirror program into node 708 RAM;
- verifies a 16-byte challenge response.

Reset or boot the board normally afterward to restore the intended application. The relevant J23 data shunts and J20/J22 reset paths must be installed. J26 `NO BOOT` avoids a Host flash-boot race during active probing.

## YAML workspace schema

Schema version 7 stores boards and projects separately and persists Kraken configuration per project chip:

```yaml
schemaVersion: 7
activeBoardId: 11111111-1111-1111-1111-111111111111
activeProjectId: 22222222-2222-2222-2222-222222222222
boards:
  - id: 11111111-1111-1111-1111-111111111111
    name: EVB001 S0121
    model: EVB001
    serialNumber: "0121"
    portA: ...
    portB: ...
    portC: ...
    jumpers: ...
projects:
  - id: 22222222-2222-2222-2222-222222222222
    name: GA144 Project 1
    chips:
      - role: Host
        name: Host GA144
        kraken:
          enabled: true
          headCoordinate: 708
          tentacles: ...
      - role: Target
        name: Target GA144
        kraken:
          enabled: false
          headCoordinate: 708
          tentacles: []
    userMacros: ...
```

Workspaces from earlier schema versions are normalized automatically. Board state from schemas through 5 is migrated into the top-level `boards` collection, and projects without Kraken fields simply start with Kraken disabled.

## Project layout

- `Models/Ga144Board.cs` — persistent physical-board model
- `Models/IdeWorkspace.cs` — schema versioning and migration
- `Models/KrakenConfiguration.cs` — Kraken head, three balanced tentacles, validation, and per-node routing metadata
- `Services/SerialPortDiscoveryService.cs` — WMI and COM discovery
- `Services/Ga144Node708Probe.cs` — verified node-708 detector
- `Services/YamlConfigurationStore.cs` — external atomic YAML persistence
- `ViewModels/BoardViewModel.cs` — board identity, connection state, jumpers, and FTDI summaries
- `ViewModels/MainWindowViewModel.cs` — independent board/project selection and hot-plug handling
- `Controls/BoardViewControl.*` — clickable board visualization
- `Compiler/` — F18 source compiler, imports, and macros


### USB pacing while Kraken is live

Normal Kraken transactions use a 5 ms settle interval; Check Kraken uses 10 ms. Once Kraken is erected, all serial/COM discovery is frozen. The resident Kraken keeps its COM endpoint reserved, but the native Win32 COM handle is closed/parked whenever no explicit Kraken operation is active and reopened without an intentional reset for the next operation.
#   e v a l b o a r d _ i d e  
 