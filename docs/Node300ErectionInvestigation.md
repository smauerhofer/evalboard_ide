# Node 300 (tentacle 1, position 31) erection failure — investigation notes

Status as of 2026-08-20: **RESOLVED (implemented, not yet hardware-confirmed).** Real erection (`KrakenSession.ErectOnto`) now uses the old-style, fire-and-forget boot-frame erection method instead of the new `w/r`-based dynamic relay construction, since the old method is hardware-proven to reach node 300 reliably while the new one is hardware-proven not to. Awaiting the user's hardware test of the updated `ErectOnto` to confirm node 300 now erects successfully in real use (not just via the diagnostic probes).

## Symptom (original)

`KrakenSession.ErectOnto`, building the real 3-tentacle Kraken, always timed out at tentacle 1, position 31 (node 300), sending `focus` -> port 0x1D5: 0 of 3 bytes received, every time, regardless of every variable tried below.

## Ruled out, with evidence

- **Generic depth/capacity limit in the relay-wrapper mechanism.** Tentacles 2 (46 nodes) and 3 (47 nodes) — both deeper than position 31, both with no boot nodes — sweep to full depth with 100% success using the identical `focus`+`writeB` mechanism. (`Ga144Node708TentacleDepthProbe`, "Node 708 tentacle-depth test" button.)
- **Boot-timing / reasonableness-check window.** Delays up to 1500ms and response timeouts up to 8000ms produce byte-for-byte identical failures.
- **Erection order / accumulated warm-up state.** Reversing tentacle order (1 last, after 2 and 3 succeed) made no difference.
- **Jumper/GPIO glitch causing node 300 to stick in `ser-exec` (its real ROM's synchronous-boot receive path).** Conclusively dead: the old-method read (below) proves node 300 executes and answers through its normal mesh ports, which a node stuck in `ser-exec` could not do. Also, J34/J35 GPIO pins (300.1/300.17) are physically separate from the inter-node mesh ports Kraken relay actually uses, so they can't affect mesh relay electrically either.
- **Node 301 as the actual bottleneck (can't relay one hop further, regardless of target).** `Ga144Node708AlternateRelayProbe` redirected node 301's `writeB` to node 401 instead of node 300, at the identical 31-layer depth — also timed out identically. Ruled out node 301.
- **Direction-specific fault (something about approaching node 300 from the east specifically).** `Ga144Node708BypassProbe` reached node 300 via node 400 (north neighbor) instead of node 301 (east neighbor), at a different depth (21 layers) — also timed out identically, 0 of 3 bytes. Node 300 was unreachable from either real physical direction under the OLD (dynamic) protocol.
- **Timing/pacing anomaly building up on this specific path.** The tentacle-depth probe sweeps all 3 tentacles and reports per-position `Elapsed`. Tentacle 1's pacing was completely flat and identical to tentacles 2/3's successful pacing (~112-117ms per position, no growth with depth) all the way through position 30 (node 301); position 31 then failed cleanly after ~1079ms (~79ms request + full 1000ms timeout waiting for a reply that never arrived). No anomaly anywhere.

## Confirmed working: the OLD (pre-redesign) erection method reaches node 300

`Ga144LegacyKrakenErectionProbe` ports the old, pre-redesign erection mechanism (from an uploaded old-code zip) verbatim and independently of the current implementation: fire-and-forget `SendBootFrame` boot frames (no reply ever checked during erection) to erect the real, full topology, then a genuine old-style 18-bit carrier-clocked read (the old `reply` RAM program) against node 301 (control) and node 300 (target).

**Result: both reads succeeded.** Node 301 read back `0x2A8A2`. Node 300 read back `0x031A5` — an exact match for the literal `0x31A5` that node 300's own real ROM (`cold`) pushes and stores into A as its very first two instructions (`x31A5. a!`, from the ROM source the user pasted verbatim). This is not a coincidental value; it's proof the old method reached the real, physical node 300 mid-execution of its actual boot ROM.

Key finding: **node 300 itself is healthy and reachable. The fault was specific to the new protocol's own dynamic construction/relay mechanism**, even though the wrap opcodes, port-address formula, physical route, and per-hop pacing were all identical/indistinguishable between old and new. Old erection sends each hop's entire wrapped instruction stream as one precomputed, host-buffered burst, with zero acknowledgment of anything during erection. New erection (`w/r`) constructed the same wrap dynamically, live, on node 708 itself, interleaving a real host round-trip for every setup word at every hop. Why the dynamic construction specifically failed to reach node 300 while the static one succeeded was never fully explained at the protocol level (timing was ruled out) — it remains an open question, but is now moot for real use since erection no longer uses the dynamic method.

## Fix implemented (2026-08-20)

`KrakenSession.ErectOnto` was rewritten to:
1. Reset the chip as before.
2. Load the current `main`/`obit`/`readw`/`oword`/`obyt`/`sett`/`w/r` head program into node 708's RAM via a boot frame, but with **completion pointed at ROM's ser-exec** (`AsyncSerialContinuationAddress = 0x0AE`) instead of directly at `main`'s entry — so node 708 stays passively parked in ROM, ready to accept more framed boot frames, without yet running `main`.
3. Erect every node in all three tentacles using **old-style, fire-and-forget, precomputed boot frames** (`LegacyKrakenProtocol.BuildX1`/`BuildW1`, reusing the verbatim-ported old `KrakenProtocol` logic already built for `Ga144LegacyKrakenErectionProbe`) — the exact mechanism proven to reach node 300 reliably. No reply is read or checked during this phase, matching old code's own behavior.
4. Send one final, empty (zero-payload) boot frame with completion pointed at RAM address 0 (`main`'s entry), which is already resident in RAM from step 2 — this is what actually starts `main` running, now that every node is parked and wired.

Everything downstream of erection (`SelectTentacle708`, `WriteRead708`, `ReadWord708`, all `w/r`-based reads/writes, `Check Kraken`, ROM verification, etc.) is **completely unchanged** — old-style `focus` (a direct jump instruction) and old-style `writeB` leave each node in the same final state (P == incoming port, B == next hop's port) as the new-style words did, so the current protocol's post-erection operations don't know or care how erection happened. `ConnectAndErect`'s existing post-erection verification read (`ReadA` against `_targetRoute`) still runs exactly as before and is now the first real functional check after an old-style erection, exactly as it was for the old code historically.

The temporary `ErectionDiagnosticResponseTimeoutMilliseconds` (8s diagnostic widening) and the erection-time `focus`-reply-mismatch check were removed along with the old dynamic erection loop, since old-style erection has no reply to check during erection at all. `KrakenProtocol.BuildFocus`'s `dup` (added earlier so `focus` echoes its own payload) is still in effect for `JumpAsync`/`WriteBAsync` and other post-erection `w/r`-based operations — only erection itself changed.

**Not yet done:** hardware confirmation that a real "Install Kraken" now succeeds through node 300. The diagnostic probes (tentacle-depth, alternate-relay, bypass, legacy-erection tests) all remain in place and unaffected, still usable for comparison if anything looks off.

## Diagnostic tools added during this investigation (all still present, self-contained, reachable from the Chip window's second button row)

- `Ga144Node708TentacleDepthProbe.cs` — sweeps `focus`+`writeB` to full depth on any subset of tentacles (now defaults to all 3), reports per-position `Elapsed`.
- `Ga144Node708AlternateRelayProbe.cs` — redirects node 301's `writeB` to node 401 instead of node 300, same depth.
- `Ga144Node708BypassProbe.cs` — reaches node 300 via node 400 instead of node 301, different depth/direction.
- `Ga144LegacyKrakenErectionProbe.cs` — old-method erection + old-method carrier-clocked read, fully independent of `KrakenSession`/`KrakenProtocol`. Its `LegacyKrakenProtocol` helper class is now also reused directly by the real `KrakenSession.ErectOnto`.

Each probe does its own chip reset and is blocked while a Kraken is erected, same as before.