namespace Ga144.Evb.Ide.Services;

/// <summary>
/// The four resident F18 node programs that make up AN003's ("SRAM Control
/// Cluster Mark 1") SRAM interface: nodes 007 (data bus/control), 008
/// (control pins), 009 (address bus), and 107 (interface). These run
/// standalone in each node's RAM -- loaded and started the same generic way
/// any other node program is (<c>KrakenLiveController.WriteRamAsync</c> +
/// <c>JumpAsync(0x000)</c>) -- and are NOT executed as puppeted Kraken
/// leaves; contrast with <see cref="KrakenSramProtocol"/>, which builds the
/// leaf sequences a memory-master node (106/108/207) uses to talk to the
/// already-running node 107 once these are installed.
///
/// Every one of these is reimplemented from AN003's prose and protocol
/// tables (sections 3 and 4), not transcribed byte-for-byte from the
/// application note's own 2010-era arrayForth screen-dump assembly listing.
/// That listing's column/screen-relative address and label numbers could not
/// be reliably recovered from the (OCR'd, and in places visibly garbled)
/// source PDF, so reproducing it exactly was not attempted; the protocol
/// tables and prose descriptions are unambiguous and are what these programs
/// were built and cross-checked against. This is a deliberate, disclosed
/// engineering tradeoff, not an oversight -- see the remarks on each program
/// below for specifics.
///
/// NOTE: nothing in the environment these were authored in could build or
/// run the actual net10.0-windows/WPF project (no .NET SDK, wrong OS). Each
/// program below WAS compiled -- with zero diagnostics, within the 64-word
/// RAM budget, entry point at address 0 -- against this project's own
/// <c>Compiler/F18Compiler.cs</c> in a throwaway standalone console harness
/// that referenced the compiler sources directly. That confirms the F18
/// syntax is valid and each image fits. It does NOT confirm the protocol
/// timing is correct on real silicon; see the remarks on
/// <see cref="Node007DataBusAndControl"/> for the one placeholder that most
/// needs bench verification before relying on it.
/// </summary>
internal static class SramClusterPrograms
{
  /// <summary>
  /// Node 009 -- address bus (AN003 section 4.3, no separate subsection
  /// heading is given in the source, but the role and B/A register
  /// assignment match the "009" column of Figure 1). B is fixed to 'right',
  /// reaching node 008. On each request it reads two words from 008 -- the
  /// 16-bit address high bits, doubled twice (i.e. shifted left 2, matching
  /// AN003's own "(a16&lt;&lt;2)" address composition) OR'd with the low 2
  /// bits recovered from the (possibly-inverted) page value -- and drives the
  /// resulting 18-bit SRAM address onto its own 'data' I/O register (address
  /// 0x141, the up-facing data port used as GPIO here, per DB001's register
  /// map), matching the CY62167EV18LL's 18-bit (A0-A17) address bus.
  /// </summary>
  public const string Node009AddressBus = """
      : start
        right b!
        cmd ;

      : cmd
        @b 2* 2*
        @b -if inv then
        3 and xor
        data a! !
        cmd ;
      """;

  /// <summary>
  /// Node 008 -- control pins (AN003 section 4.2's "coordinates the
  /// activities of nodes 008 and 009 in driving address and control signals"
  /// description; node 008 itself is summarized only by its pin-control table
  /// and dual-port role, not a separate walkthrough). B uses the "r-l-" dual
  /// port (0x1F5, right+left simultaneously) reaching both node 007 and node
  /// 009, matching AN003's statement that node 008 relays node 007's
  /// a16/page words on to node 009 while also acting on them itself. A is
  /// pinned to the I/O register once at start and never changed, since every
  /// cmd iteration only ever drives new bits through the same register.
  ///
  /// 'pins' is the 8-entry (bits[4:2] of the received page value select one
  /// of 8) WE-/CE-/A18/A19 drive-pattern table AN003 documents by hex value
  /// (r00, r01, r10, r11, w11, w10, w01, w00): x2556E, x2557E, x3556E,
  /// x3557E, x3557A, x3556A, x2557A, x2556A.
  /// </summary>
  public const string Node008ControlPins = """
      : start
        r-l- b!
        io a!
        cmd ;

      label pins
      data x2556E
      data x2557E
      data x3556E
      data x3557E
      data x3557A
      data x3556A
      data x2557A
      data x2556A

      : cmd
        @b !
        @b !b
        @b dup !b
        2/ 2/ 7 and
        pins + a! @
        io a! !
        cmd ;
      """;

  /// <summary>
  /// Node 007 -- data bus and control (AN003 section 4.2, "Node 007 - Data
  /// Bus and Control"). B is fixed to 'left', reaching node 008. A alternates
  /// between 'down' (talking to node 107) and 'data' (the 16-bit data bus
  /// I/O register) as each phase requires, matching AN003's own description
  /// of A's role. The node007 protocol table (a16 then +-p, then w only for
  /// a write) is decoded directly: 'a' is always read first and relayed to
  /// node008/009 unmodified (sign included, so their own decode of page's
  /// sign stays intact), then the sign of the second word ('page') selects
  /// the read or write phase.
  ///
  /// PLACEHOLDER TIMING -- MUST BE VERIFIED BEFORE HARDWARE USE: AN003's own
  /// listing comments each delay loop as e.g. "40 13 for unext" / "50 40 for
  /// unext" (roughly 45ns/55ns pulses at the F18A's ~1.4ns/instruction). The
  /// exact numeric delay constants could not be reliably recovered from the
  /// garbled OCR of that listing. The "63 for . unext" loops below (~64
  /// iterations of a 1-word body) are a conservative placeholder chosen to
  /// generously clear the CY62167EV18LL-55's 55ns access/cycle-time spec at
  /// this project's clock rate, NOT a transcription of AN003's real,
  /// hand-tuned values. Verify against the SRAM datasheet and a scope/logic
  /// analyzer -- and tighten if read/write throughput needs to improve --
  /// before depending on this in a real design.
  /// </summary>
  public const string Node007DataBusAndControl = """
      : start
        left b!
        x3557F io a! !
        x14555 io a! !
        cmd ;

      : cmd
        down a!
        @ !b
        @ dup !b
        -if
          @
          data a! !
          x15555 io a! !
          63 for . unext
          x3557F !b
          x14555 io a! !
          cmd ;
        then
          63 for . unext
          x3557F !b
          data a! @
          down a! !
          cmd ;
      """;

  /// <summary>
  /// Node 107 -- interface (AN003 section 6.3's "degenerate sram" single-
  /// master, no-polling, no-stimuli variant of section 4.1's node -- see that
  /// section's own words: "single master, no polling, no stimuli. maximum
  /// speed, minimum power"). Deliberately NOT AN003 section 4.1's full
  /// 3-master polling version: Kraken only ever transiently puppets one
  /// master node per SRAM transaction (see <see cref="KrakenSramProtocol"/>)
  /// -- no resident master program is ever actually left running and idle
  /// waiting on a stimulus in this system's usage pattern -- so the full
  /// version's multi-master arbitration and stimulus-wake machinery could
  /// never be functionally exercised here. AN003 section 6.3 offers exactly
  /// this simplification for exactly this situation ("single master"), and
  /// it is far smaller, which is what let this fit the 64-word RAM budget at
  /// all (the full version, first implemented as directly and explicitly as
  /// possible for verifiability, compiled to roughly 117 words).
  ///
  /// A GA144 port read/write blocks in hardware until the other side is
  /// ready, so -- exactly as AN003's own degenerate listing does -- 'cmd'
  /// needs no explicit poll/wait loop of its own: its first '@b' simply
  /// blocks until the selected master writes a request.
  ///
  /// The master port is fixed at INSTALL time, not runtime: <paramref
  /// name="masterPortName"/> (one of "right"/"left"/"up", i.e. node 107's
  /// local port toward 106/108/207 respectively -- see
  /// <c>KrakenTopology.PortName(107, masterCoordinate)</c>) is baked directly
  /// into this source before it is compiled and deployed, once, by
  /// <c>SramClusterInstaller</c> for whichever master the SRAM Tentacle
  /// window's Install action was run with. Re-running Install with a
  /// different master recompiles and redeploys this node with the new port.
  ///
  /// mk! is still recognised on the wire (so a master's mk! request does not
  /// desync 'cmd's parser mid-stream) but is a deliberate protocol no-op in
  /// this single-fixed-master configuration -- see the remarks on
  /// <see cref="KrakenSramProtocol.BuildSramSetMask"/>.
  ///
  /// Dispatch mirrors AN003 section 4.1's own description of the (shared,
  /// unchanged-between-versions) decoding rule: "checking the signs of the
  /// first two [words] as an economical way of decoding which of the four
  /// primitive functions is being requested" -- ex@/mk! both start with a
  /// non-negative first word, ex!/cx? both start with a negative first word;
  /// the second word's sign then distinguishes the pair. Each leaf of that
  /// 2x2 dispatch is split into its own single-word-call subroutine
  /// (do-write/do-cx/do-discard/do-read) purely so every 'if'/'-if' forward
  /// branch in 'cmd'/'neg-cmd'/'pos-cmd' only ever has to jump over one call
  /// -- this compiler's packed-instruction forward transfers have a very
  /// short reach once they land outside slot 0 (an 'align' is used ahead of
  /// each dispatch test for the same reason, forcing it to start a fresh
  /// word in slot 0), and splitting the branch bodies out into subroutines
  /// keeps every jump trivially in range regardless of how large any one
  /// leaf's own logic is.
  ///
  /// CORRECTED (against AN003's own real listing, hand-transcribed and
  /// supplied by the user, plus the F18A opcode reference DB001 section 2.3.5
  /// and the arrayForth manuals DB004/DB013 section 5.3.2.1/4.2.4.1): 'if' and
  /// '-if' do NOT pop T. Per DB001's own wording -- "if. If T is nonzero,
  /// continues... If T is zero, jumps" / "-if. If T is negative, continues...
  /// If T is positive, jumps" -- neither description says the value is
  /// consumed, in explicit contrast to every arithmetic/memory opcode in the
  /// same reference that DOES ("or... Pops data stack", "!b... pops the data
  /// stack"). AN003's own 'cmd' confirms this directly: it dispatches with a
  /// bare '@ -if' and never dups a spare copy first, because -if leaves the
  /// fetched word sitting on the stack for the branch body to use as-is.
  ///
  /// The first version of this file assumed ordinary (ANS-Forth-style)
  /// popping 'if'/'-if' and so 'dup'-ed a spare copy ahead of every dispatch
  /// test here, on the assumption the tested copy would be consumed. Under
  /// the real, non-popping hardware behavior that dup is never consumed by
  /// anything -- it survives BOTH branches and is left as a permanent extra
  /// item on node 107's data stack after every single request. 'cx?' had the
  /// matching bug: its 'xor' comparison result is tested by a non-popping
  /// 'if' but was never explicitly dropped, so it also survived under the
  /// restored 'r>'ed arguments in both branches. Neither leak crashes
  /// anything outright -- the F18A data stack is a 10-deep CIRCULAR buffer
  /// (DB001 2.3.2: pushing past the top silently overwrites the bottom), so
  /// there is no hard overflow fault -- but every extra unconsumed word
  /// pushes real, still-needed values one slot closer to being silently
  /// clobbered by the next leak, and this compounds every request. Fixed by
  /// removing the now-unnecessary 'dup' in 'cmd'/'neg-cmd'/'pos-cmd' (the
  /// fetched word is already available to the branch body without it) and by
  /// adding an explicit 'drop' of the comparison flag at the top of both of
  /// 'cx?'s branches.
  /// </summary>
  public static string BuildNode107Source(string masterPortName)
  {
    if (masterPortName is not ("right" or "left" or "up"))
    {
      throw new ArgumentException(
          "Node 107 only has three neighbors that may act as an SRAM memory master: " +
          "'right' (106), 'left' (108), or 'up' (207).",
          nameof(masterPortName));
    }

    return $$"""
        : start
          {{masterPortName}} b!
          cmd ;

        : sram@ ( p a -- w )
          down b!
          !b
          !b
          @b ;

        : sram! ( p a w -- )
          down b!
          >r
          !b
          inv !b
          r> !b ;

        : cx? ( n p a w -- f )
          >r
          dup >r
          over >r
          sram@
          xor
          if
            drop
            r> drop r> drop r> drop
            0
          else
            drop
            r> r> r>
            sram!
            xFFFF
          then ;

        : do-write ( p a -- )
          @b
          sram!
          {{masterPortName}} b! ;

        : do-cx ( n p -- )
          @b
          @b
          cx?
          {{masterPortName}} b!
          !b ;

        : neg-cmd ( p-or-n -- )
          @b align -if
            inv
            do-write
          then
            do-cx
          ;

        : do-discard ( p -- )
          @b drop
          drop ;

        : do-read ( p a -- )
          sram@
          {{masterPortName}} b!
          !b ;

        : pos-cmd ( p -- )
          @b align -if
            drop
            do-discard
          then
            do-read
          ;

        : cmd
          @b align -if
            inv
            neg-cmd
          then
            pos-cmd
          cmd ;
        """;
  }

  /// <summary>
  /// The memory-master node's (106/108/207) own resident AN003 support
  /// subroutines -- one per primitive (<c>sram-read</c>/<c>sram-write</c>/
  /// <c>sram-cx</c>/<c>sram-mask</c>). Unlike the four programs above, this is
  /// deployed with <c>KrakenLiveController.WriteRamAsync</c> ONLY -- never
  /// followed by <c>JumpAsync</c> -- so the master's P register stays parked
  /// on its incoming port and the node remains puppetable indefinitely, the
  /// same way it was before this was installed. Nothing here ever runs on
  /// its own; each subroutine only executes when a host-built leaf (see
  /// <see cref="KrakenSramProtocol"/>) pushes that op's arguments onto this
  /// node's stack via '@p' and then injects a real 'call' word addressed at
  /// the subroutine. A GA144 port address does not advance P the way a
  /// RAM/ROM address does, so that 'call' safely pushes the still-valid port
  /// address as its return address; each subroutine's own compiler-emitted
  /// closing ';' pops that same address back into P, handing control back to
  /// puppet mode with no special handling needed at either end.
  ///
  /// Every subroutine sets B to <paramref name="masterPortName"/> -- this
  /// node's OWN local port toward node 107 (the reverse direction of node
  /// 107's own <c>masterPortName</c> in <see cref="BuildNode107Source"/>; see
  /// <c>KrakenTopology.PortName(masterCoordinate, 107)</c>) -- itself, every
  /// call, rather than relying on some earlier puppet operation having left B
  /// pointed there already.
  ///
  /// Argument order: <see cref="KrakenSramProtocol"/> pushes each op's
  /// arguments in the EXACT REVERSE of AN003's own wire send order, so every
  /// subroutine here can just fire off its '!b' writes strictly in stack-pop
  /// order with no swap/rot of its own -- the first word popped is always the
  /// first word AN003 expects on the wire. ex!/mk!, which AN003 defines no
  /// reply for, 'dup' the value being sent before the last '!b' and leave the
  /// duplicate on the stack as the required echoed acknowledgment (matching
  /// KrakenProtocol.BuildWriteA/BuildWriteMemory's own convention); ex@/cx?,
  /// which do have a real protocol reply, end in a genuine '@b' instead.
  ///
  /// Also includes 'echo' -- DIAGNOSTIC ONLY, not part of AN003 at all. It
  /// never touches B or node 107; it exists purely to exercise the
  /// '@p'-push / 'call' / '!p'-read-back mechanism itself (see
  /// <see cref="KrakenSramProtocol.BuildEchoTest"/>) against real master-node
  /// hardware, in isolation from the AN003 handshake with 107 -- so a failure
  /// there can be told apart from a failure in this call/return plumbing.
  /// Deliberately increments the pushed value ('1 +') rather than returning
  /// it unchanged: a bare ';' would "pass" even if the call never actually
  /// ran and the old value was simply still sitting on the stack from
  /// something else, whereas a changed, predictable result can only come
  /// from the subroutine's own instruction genuinely executing.
  /// </summary>
  public static string BuildMasterSupportSource(string masterPortName)
  {
    if (masterPortName is not ("right" or "left" or "up" or "down"))
    {
      throw new ArgumentException(
          "A memory-master node's local port toward node 107 must be one of the four compass directions.",
          nameof(masterPortName));
    }

    return $$"""
        : sram-read ( addr page -- w )
          {{masterPortName}} b!
          !b
          !b
          @b ;

        : sram-write ( value addr page -- value )
          {{masterPortName}} b!
          !b
          !b
          dup !b ;

        : sram-cx ( w a p n -- f )
          {{masterPortName}} b!
          !b
          !b
          !b
          !b
          @b ;

        : sram-mask ( m f x -- m )
          {{masterPortName}} b!
          !b
          !b
          dup !b ;

        : echo ( n -- n+1 )
          1 + ;
        """;
  }
}