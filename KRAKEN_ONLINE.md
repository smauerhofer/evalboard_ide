# Online Kraken implementation notes

This revision adds a live transport/session layer on top of the existing persistent Kraken topology.

## UI

- The GA144 chip view is enlarged and gives every node a gutter around its button.
- Directed SteelBlue/DarkOrange/MediumPurple arrows are drawn in those gutters for T1/T2/T3.
- The node editor exposes **Online Kraken…** only for nodes on a tentacle. Node 708 is intentionally excluded because it is the head.
- The online window contains RAM, ROM, Registers/I/O, and Stacks tabs.

## Session erection

A session resolves the currently selected physical board by its saved FTDI identity:

- Host -> USB A -> Host node 708
- Target -> USB C -> Target node 708

`Connect & erect Kraken` opens that live COM port at 921600 baud, asserts RESET- with RTS low using the same polarity as the proven node-708 detector, releases reset, loads a small reply helper into node 708 RAM, and then initializes every node in each tentacle in forward order.

For node `n` at zero-based tentacle position `p`, erection sends Kraken `w1(p, '@p b!', B)` where `B` is the port to the next node (or `io` for a terminal node). The controlled nodes remain executing from their incoming COM port and no Kraken code is stored in their RAM or ROM.

The first asynchronous frame after reset is accepted through `cold`, but its completion address is node 708 ROM `ser-exec` (`0x0AE`). Every subsequent setup/write frame also completes to `ser-exec`; read frames execute the RAM reply helper, whose final jump is back to `0x0AE`. This is the continuation entry defined for processing additional boot frames. It is deliberately **not** `cold`: `cold` is the reset/wakeup entry and performs the asynchronous start-bit reasonableness measurement when it sees a possible start edge. The online protocol now follows the documented concatenation mechanism instead of rerunning that classifier for every Kraken transaction.

## Kraken stream builder

`Services/KrakenProtocol.cs` implements the recurrence from the Kraken material:

- `x1(0,x) = x`
- `w1(0,x,y) = x y`
- `r1(0,x) = x`
- `wr1(0,x,y) = x y`
- each forward hop prepends `@p >r`, a word count, and `@p !b unext`
- each return hop appends `@b !p`

The operations used by the monitor include `@p a!`, `a !p`, `@p !+`, `@+ !p`, `@p b!`, `!p`, `@p`, `r> !p`, and `@p >r`.

## Carrier-clocked read return

An ordinary asynchronous boot frame provides PC -> node 708 transport, but the monitor also needs a reliable node 708 -> PC return path. The reply helper does not generate a UART baud rate with an F18 delay loop.

For each returned 18-bit word the PC transmits 18 carrier pairs:

```
00 FF   00 FF   ...  (18 pairs)
```

The EVB serial path presents the GreenArrays asynchronous polarity at node 708 (idle low, start high) and converts it back to ordinary UART polarity at the PC. For a zero bit the head mirrors the `00` carrier and consumes the following `FF` carrier silently. For a one bit it consumes the `00` carrier silently and mirrors the following `FF` carrier. The FTDI receiver therefore obtains exactly 18 bytes, each near `00` or `FF`, and the host reconstructs the 18-bit word LSB first. All start/data/stop timing comes from the FTDI UART edges; the F18 only reacts to those edges.

The connection process performs a non-destructive read of target register A after erection. The session is not reported ONLINE unless that read traverses the chosen tentacle and comes back through the reply helper.

## F18 architectural limits shown explicitly in the UI

- **A:** true read/write.
- **IO:** true read/write at `0x15D`.
- **RAM:** 64 words read/write.
- **ROM:** 64 words read-only.
- **Parameter stack:** 10-word read/write image.
- **Return stack/R:** 9-word read/write image.
- **B:** write-only in hardware. The displayed value is the expected/configured Kraken B; there is no fake readback.
- **P:** no direct read opcode. While attached, its value is known from the incoming Kraken port. **Jump** changes P by executing a Kraken command. The COM endpoint remains reserved, but the runtime is marked topology-altered and automatic reset/re-erection is forbidden.
- **I:** no direct read opcode; shown as unavailable rather than fabricated.

Writing B can break the route beyond the selected node. Writing IO may alter external pins. Jumping P makes the selected node leave Kraken port execution. After such a destructive routing change the IDE keeps the original serial handle open and marks the Kraken topology faulted/altered; it does not reset or reconnect automatically.

## Existing detector preserved

`Services/Ga144Node708Probe.cs` is not modified by this feature. The online transport reuses its internal, hardware-verified 18-bit asynchronous word encoder so the byte-0 calibration pattern and inversion remain identical to the working detector.
## 2026-08-07 crash fix

Opening **Online Kraken...** previously bound the read-only `KnownPText` property to `TextBox.Text` without an explicit binding mode. WPF defaults `TextBox.Text` to a two-way binding, so assigning the window DataContext could throw `InvalidOperationException` for the getter-only property and terminate the dispatcher event. The binding is now explicitly `Mode=OneWay`. The node-editor click handler also catches and displays any future Kraken-window construction error instead of terminating the IDE.


## 2026-08-07 tentacle focus/readback fix

Every tentacle node is now explicitly focused onto its single incoming COM port before its B register is assigned.  A reset F18 normally starts at a multiport execution address.  It can accept the initial Kraken setup instruction there, but leaving P at that multiport address makes a later `!p` a multiport write, which waits for every selected neighbor and deadlocks a Kraken `r1` reply.  Erection now sends `x1(position, jump incoming-port)` first, then `w1(position, '@p b!', B)`.  This establishes the Kraken invariant that node n executes on the port from node n-1 and makes the backward `@b !p` reply path single-port all the way to node 708.

## 2026-08-07 GA144 local-port orientation fix

The first online implementation treated the F18 port constants `right`, `left`, `up`, and `down` as fixed geographic directions. That is not true for the mirrored GA144 cell layout. GreenArrays' Ganglia Mark 2 documentation explicitly uses four `ewns` tables to compensate for the four node orientation classes (`oee`, `ooo`, `eee`, `eoo`).

The topology now converts a geographic neighbor into the **local F18 COM port** using row/column parity:

- odd row, even column: E=right, W=left, N=up, S=down
- odd row, odd column: E=left, W=right, N=up, S=down
- even row, even column: E=right, W=left, N=down, S=up
- even row, odd column: E=left, W=right, N=down, S=up

This matters immediately on T1: head node 708 reaches geographic-west node 707 through head-local `left`, but node 707's incoming port back toward geographic-east node 708 is also node-707-local `left`; its outgoing port toward node 706 is node-707-local `right`. The previous fixed-compass mapping focused node 707 on `right` instead and assigned B=`left`, so the tentacle was broken at the first controlled node. `KrakenTopology.PortAddress` and the displayed local port names now use the mirrored-cell mapping.

## Kraken path check

The GA144 chip window contains **Check Kraken**. If hardware Kraken is not yet running, the
diagnostic performs the single permitted reset/erection, then visits nodes breadth-first from the
head: position 0 of T1/T2/T3 (707, 709, 608), then position 1 of each tentacle, and so on.

For every reachable node it saves A and RAM[0], writes the decimal node coordinate into RAM[0],
reads RAM[0] back through the same Kraken route, compares the result, and restores RAM[0] and A.
Once erection has completed, **Check Kraken never resets or re-erects the chip**. If a transport
failure occurs, the first failing node is reported and all remaining nodes are skipped.

The node-708 reply helper uses the boot frame's transfer address left in A, so the same helper can
receive a reply from any of the three tentacle head ports without being recompiled or reloaded.

## Resident Kraken and idle COM parking

The live Kraken runtime is owned by the main IDE process, not by a chip, node, or check window.
A successful erection establishes a strict invariant for that physical GA144:

1. Kraken is erected only once unless the user/process explicitly abandons the runtime;
2. the physical COM endpoint remains reserved exclusively for that resident Kraken;
3. the native Win32 COM handle is opened only while an explicit Kraken operation is active;
4. when no operation is active, the native COM handle is closed/parked to stop continuous FTDI VCP activity;
5. reopening the COM endpoint never intentionally toggles reset, probes node 708, reloads the helper, or re-erects the tentacles;
6. no automatic or manual node-708 probe may touch the reserved endpoint;
7. a transport/topology fault blocks automatic reset/re-erection recovery.

For the first **Check Kraken** immediately after erection, the initial handle is intentionally kept
open across erection and the complete 143-node scan. It is parked immediately when the scan ends.
Subsequent checks reopen the endpoint once for the scan and park it afterward. Individual node
operations similarly open the endpoint for the complete high-level operation (for example a
64-word RAM read) and park it when that operation completes.

This idle-close behavior is deliberately experimental. Port A RTS is the EVB host RESET control.
Immediately after each `CreateFile`, the native transport requests DTR and RTS high before the
slower COM setup calls, and it again requests DTR/RTS high immediately before `CloseHandle`.
However, whether a particular FTDI/VCP driver preserves the physical RTS level after the last
handle closes is driver/hardware dependent. If the next Kraken operation fails after a successful
park, that is evidence that closing the VCP changed RESET and destroyed the resident Kraken.
The IDE does not hide that condition by resetting or re-erecting automatically.

## Persistent async continuation timing

The online transport returns node 708 to `ser-exec`, not `cold`, after boot frames and replies. On
the G144A12 asynchronous boot node, `cold` is the reset/wakeup entry while `ser-exec` is the ROM
concatenation entry for processing additional boot frames. For node 708 it is address `0x0AE`.

After the one permitted erection reset:

1. the first frame completes to `0x0AE`;
2. every no-reply Kraken frame completes to `0x0AE`;
3. every read frame completes to the node-708 RAM reply helper at address 0;
4. the reply helper returns directly to `0x0AE`;
5. parking/reopening changes only the PC-side COM handle; the IDE sends no GA144 reset or re-erection sequence.

## Serial-arbitration hardening

Opening the GA144 window suspends future serial scans and waits for any scan/probe already in
progress to finish before Kraken can be erected. After a Kraken becomes resident, Windows serial/
PnP discovery and node-708 probing remain completely frozen for the IDE process, even while the
Kraken COM handle itself is parked. The cached physical-board binding remains authoritative.

The live read path does not purge the receive queue before every transaction. The queue is purged
once during initial reset/erection; afterward each successful read consumes exactly the 18
carrier-clocked reply bytes.

## Native Windows COM transport

After Kraken erection the live transport intentionally does **not** use
`System.IO.Ports.SerialPort`. `KrakenSession` uses a minimal synchronous Win32 transport built on
`CreateFile`, `GetCommState` / `SetCommState`, `SetCommTimeouts`, `EscapeCommFunction`,
`ReadFile`, `WriteFile`, and `CloseHandle`.

No communications event mask or `WaitCommEvent` loop is installed. The native transport has no
background reader thread: `ReadFile` and `WriteFile` are called synchronously only while an
explicit Kraken operation is executing. Between operations the handle is closed/parked.

`Ga144Node708Probe` is deliberately unchanged and continues to use the proven
`System.IO.Ports.SerialPort` implementation only before a Kraken owns the chip.
