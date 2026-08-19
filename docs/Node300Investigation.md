# Node 300 (tentacle 1, position 31) erection failure — investigation notes

Status as of 2026-08-19: **paused**, unresolved. Real erection (`KrakenSession.ErectOnto`) still fails at node 300 every time. No code changes have been made to the real erection path — all changes below are diagnostic-only (new probe buttons on the Chip window), and the app's normal behavior is unchanged.

## Symptom

`KrakenSession.ErectOnto`, building the real 3-tentacle Kraken, always times out at tentacle 1, position 31 (node 300), sending `focus` -> port 0x1D5: 0 of 3 bytes received, every time, regardless of every variable tried below.

## Ruled out, with evidence

- **Generic depth/capacity limit in the relay-wrapper mechanism.** Tentacles 2 (46 nodes) and 3 (47 nodes) — both deeper than position 31, both with no boot nodes — sweep to full depth with 100% success using the identical `focus`+`writeB` mechanism. (`Ga144Node708TentacleDepthProbe`, "Node 708 tentacle-depth test" button.)
- **Boot-timing / reasonableness-check window.** Delays up to 1500ms and response timeouts up to 8000ms produce byte-for-byte identical failures.
- **Erection order / accumulated warm-up state.** Reversing tentacle order (1 last, after 2 and 3 succeed) made no difference.
- **Jumper/GPIO glitch causing node 300 to stick in `ser-exec` (its real ROM's synchronous-boot receive path).** This was the leading hypothesis for a while, but is now conclusively dead: the old-method read (below) proves node 300 executes and answers through its normal mesh ports, which a node stuck in `ser-exec` could not do. Also, J34/J35 GPIO pins (300.1/300.17) are physically separate from the inter-node mesh ports Kraken relay actually uses, so they can't affect mesh relay electrically either.
- **Node 301 as the actual bottleneck (can't relay one hop further, regardless of target).** `Ga144Node708AlternateRelayProbe` redirected node 301's `writeB` to node 401 instead of node 300, at the identical 31-layer depth — also timed out identically. Ruled out node 301.
- **Direction-specific fault (something about approaching node 300 from the east specifically).** `Ga144Node708BypassProbe` reached node 300 via node 400 (north neighbor) instead of node 301 (east neighbor), at a different depth (21 layers) — also timed out identically, 0 of 3 bytes. Node 300 is unreachable from either real physical direction under the current (new) protocol.
- **Timing/pacing anomaly building up on this specific path.** The tentacle-depth probe now sweeps all 3 tentacles and reports per-position `Elapsed`. Tentacle 1's pacing is completely flat and identical to tentacles 2/3's successful pacing (~112-117ms per position, no growth with depth) all the way through position 30 (node 301); position 31 then fails cleanly after ~1079ms (~79ms request + full 1000ms timeout waiting for a reply that never arrives). No anomaly anywhere.

## Confirmed working: the OLD (pre-redesign) erection method reaches node 300

`Ga144LegacyKrakenErectionProbe` ports the old, pre-redesign erection mechanism (from an uploaded old-code zip) verbatim and independently of the current implementation: fire-and-forget `SendBootFrame` boot frames (no reply ever checked during erection) to erect the real, full topology, then a genuine old-style 18-bit carrier-clocked read (the old `reply` RAM program) against node 301 (control) and node 300 (target).

**Result: both reads succeeded.** Node 301 read back `0x2A8A2`. Node 300 read back `0x031A5` — which is an exact match for the literal `0x31A5` that node 300's own real ROM (`cold`) pushes and stores into A as its very first two instructions (`x31A5. a!`, from the ROM source the user pasted verbatim). This is not a coincidental value; it's proof the old method reached the real, physical node 300 mid-execution of its actual boot ROM.

This is the key finding: **node 300 itself is healthy and reachable. The fault is specific to the new protocol's own construction/relay mechanism**, even though:
- the wrap opcodes (`PumpPrefix`/`PumpBody`/`ReturnHop` vs. new `w/r`'s on-chip equivalents) are byte-identical between old and new (confirmed via direct diff of the old zip),
- `KrakenTopology.PortAddress`/`KrakenConfiguration.cs` are byte-identical between old and new,
- the physical route (707 -> ... -> 301 -> 300, 31 hops) is the same in both methods,
- pacing is identical and flat in both.

## What's structurally different, and still unexplained

Old erection sends each hop's entire wrapped instruction stream as one precomputed, host-buffered burst (`SendBootFrame`), with **zero acknowledgment of anything** during the whole erection — success was historically only inferred later via a completely different, hardware-level carrier-clocked read (`ReadWord`/`WriteRequestAndRead`, 18-bit carrier clocking), never verified at erection time. New erection (`w/r`) constructs the same wrap **dynamically, live, on node 708 itself**, interleaving a real host round-trip (`readw`) for every setup word at every hop, with a software echo/ack scheme.

This is a genuine architectural difference, but per the timing data above it does *not* manifest as a speed/latency problem — the per-hop pacing is identical whether a hop succeeds (tentacles 2/3, node 301) or fails (node 300). Why the *dynamic* construction specifically fails to reach node 300 while the *static* one succeeds, given identical opcodes/addresses/pacing, is unresolved.

One black-box avenue that is **not testable** on this hardware: reading back node 301's B register after its `writeB` to confirm it actually latched the value pointing at node 300 — the F18 instruction set on this hardware has no "read B" opcode (`b` is write-only), confirmed via `F18InstructionSet.Opcodes`.

## Decision point (paused here, 2026-08-19)

Options discussed with the user, not yet chosen:
1. Adopt old-style (static, unacknowledged, single-burst) erection as the real `ErectOnto` mechanism, since it's now hardware-proven reliable on this exact board — possibly keeping the new `w/r` mechanism for reads/writes after erection.
2. Further hardware instrumentation (e.g. scope-level signal comparison) to find the remaining difference — beyond what's testable via this IDE's own black-box probes.
3. Leave as-is for now (chosen).

## Diagnostic tools added this investigation (all still present, all self-contained, none touch the real erection path)

- `Ga144Node708TentacleDepthProbe.cs` — sweeps `focus`+`writeB` to full depth on any subset of tentacles; now defaults the UI button to all 3, reports per-position `Elapsed`.
- `Ga144Node708AlternateRelayProbe.cs` — redirects node 301's `writeB` to node 401 instead of node 300, same depth.
- `Ga144Node708BypassProbe.cs` — reaches node 300 via node 400 instead of node 301, different depth/direction.
- `Ga144LegacyKrakenErectionProbe.cs` — old-method erection + old-method carrier-clocked read, fully independent of current `KrakenSession`/`KrakenProtocol`.
- `KrakenProtocol.BuildFocus` now includes `dup` so `focus`'s reply echoes back the same port value that was sent, letting `ErectOnto` distinguish "timed out, 0 bytes" from "replied, but with a mismatched value" (added to the real erection path; harmless when things work, informative when they don't).

All are reachable from the Chip window's second button row (Node 708 setn/tentacle-depth/alternate-relay/bypass/legacy-erection tests). Each does its own chip reset and is blocked while a Kraken is erected, same as the existing probe buttons.