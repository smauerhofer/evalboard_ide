namespace Ga144.Evb.Ide.Services;

/// <summary>
/// The four resident F18 node programs that make up AN003's ("SRAM Control
/// Cluster Mark 1") SRAM interface: nodes 007 (data bus/control), 008
/// (control pins), 009 (address bus), and 107 (interface). These run
/// standalone in each node's RAM -- loaded and started the same generic way
/// any other node program is (<c>KrakenLiveController.WriteRamAsync</c> +
/// <c>JumpAsync</c>, to whatever address the compiler resolves as the
/// source's entry point -- see <c>SramClusterInstaller</c>) -- and are NOT
/// executed as puppeted Kraken leaves; contrast with
/// <see cref="KrakenSramProtocol"/>, which builds the leaf sequences a
/// memory-master node (106/108/207) uses to talk to the already-running node
/// 107 once these are installed.
///
/// All four (<see cref="Node009AddressBus"/>, <see cref="Node008ControlPins"/>,
/// <see cref="Node007DataBusAndControl"/>, <see cref="Node107Interface"/>)
/// are now transcribed directly from the user's own hand-translation of
/// AN003's real color-coded arrayForth listing, rather than reimplemented
/// from prose. Earlier revisions of this file could not recover that
/// listing reliably from its 2010-era screen-dump OCR (visibly garbled in
/// places) and reimplemented all four from AN003's protocol tables and prose
/// instead -- a deliberate, disclosed tradeoff at the time, now superseded
/// by an accurate transcription; node 107 in particular is now AN003's real
/// FULL 3-master polling node (section 4.1), not the smaller degenerate
/// single-master (section 6.3) reimplementation this file used to build --
/// see the remarks on <see cref="Node107Interface"/>. Its startup B value
/// (toward node 007) is set via the compiler's '/b' startup-configuration
/// directive (DB013 "node configuration" directives) rather than any code in
/// the transcription itself -- see <see cref="Node107Interface"/>'s remarks.
///
/// NOTE: nothing in the environment these were authored in could build or
/// run the actual net10.0-windows/WPF project (no .NET SDK, wrong OS). Each
/// program below WAS compiled -- with zero diagnostics, within the 64-word
/// RAM budget -- against this project's own <c>Compiler/F18Compiler.cs</c>
/// in a throwaway standalone console harness that referenced the compiler
/// sources directly. That confirms the F18 syntax is valid and each image
/// fits. It does NOT confirm the protocol timing is correct on real silicon;
/// node 007's real, now-transcribed delay-loop counts replace what was
/// previously a placeholder, but still is not bench-verified.
/// </summary>
internal static class SramClusterPrograms
{
  /// <summary>
  /// Node 009 -- address bus. Transcribed directly from the user's own
  /// hand-translation of AN003's real listing (color-coded arrayForth source,
  /// translated by the user rather than recovered from the garbled 2010-era
  /// screen-dump OCR this file originally had to reimplement from prose --
  /// see the class-level remarks). 'start' pushes a permanent '3' onto the
  /// data stack and falls through into 'cmd' without a call (no trailing
  /// ';') -- an ordinary arrayForth idiom (DB004 7.4/DB013 4.2.4) where
  /// execution simply continues across a new ':' definition boundary -- so
  /// that '3' survives, untouched, underneath every iteration's working
  /// values for 'cmd's own 'over'/'and' to reuse as a fixed mask.
  ///
  /// Verified against this project's own compiler in a standalone harness:
  /// compiles with zero diagnostics, 12 words used (well inside the 64-word
  /// RAM budget), entry point resolves to 'start' at 0x020.
  /// </summary>
  public const string Node009AddressBus = """
      # 0x20 org
      entry start
      : start
        right b!
        ..
        data a!
        ..
        3
      : cmd
        @b 2* 2*
        over @b -if
          inv and xor !
          cmd ;
        then
          and xor .. !
          cmd ;
      """;

  /// <summary>
  /// Node 008 -- control pins. Transcribed directly from the user's own
  /// hand-translation of AN003's real listing (see the remarks on
  /// <see cref="Node009AddressBus"/> and the class-level remarks). B uses the
  /// "r-l-" dual port (defined here as a local constant "'r-l-", 0x1F5,
  /// right+left simultaneously, rather than relying on this compiler's own
  /// predefined multiport name, to stay a faithful transcription) reaching
  /// both node 007 and node 009. 'start' pins A to the I/O register once and
  /// falls straight through into 'cmd' (no trailing ';'), the same
  /// fall-through idiom used throughout this cluster.
  ///
  /// The 8-entry (bits[4:2] of the received page value select one of 8)
  /// WE-/CE-/A18/A19 drive-pattern table is laid down with the raw
  /// '[ value , value , ... ]' idiom (push each value while interpreting,
  /// 'here'-and-advance it into memory with ',') rather than a named
  /// 'label' + 'data' list -- and, matching the original, is placed exactly
  /// at address 0 so 'cmd's computed 3-bit selector can be used directly as
  /// the table address with no base-address addition.
  ///
  /// '7 ..' near the top of 'cmd' pushes a permanent mask constant (7) that
  /// survives, via the same non-popping '-if'-free fall-through, to be
  /// 'and'-ed with the shifted page value later in the same iteration.
  ///
  /// Verified against this project's own compiler in a standalone harness:
  /// compiles with zero diagnostics, 20 words used, entry point resolves to
  /// 'start' at 0x020.
  /// </summary>
  public const string Node008ControlPins = """
      # 0x1F5 const 'r-l-
      entry start
      # 0 org
      [
        0x2556E , 0x2557E ,
        0x3556E , 0x3557E ,
        0x3557A , 0x3556A ,
        0x2557A , 0x2556A ,
      ]
      # 0x20 org
      : start
        'r-l- b!
        io a!
      : cmd
        @b !
        a >r
        7 ..
        @b !b
        @b dup !b
        2/ 2/
        and a!
        ..
        @
        r> a!
        !
        cmd ;
      """;

  /// <summary>
  /// Node 007 -- data bus and control. Transcribed directly from the user's
  /// own hand-translation of AN003's real listing (see the remarks on
  /// <see cref="Node009AddressBus"/> and the class-level remarks), including
  /// the real hand-tuned delay-loop counts (0x13 and 0x40 iterations) this
  /// file previously had to approximate with a placeholder, since the
  /// original screen-dump OCR of those constants could not be trusted -- see
  /// git history for that placeholder if the real counts ever need
  /// cross-checking.
  ///
  /// 'cmd' falls through into 'w16' (no trailing ';') for the write phase,
  /// and 'w16's body ends in a real call back to 'cmd'; the read phase 'r16'
  /// is reached only by -if's forward branch (its leading 'then' resolves
  /// that branch), the same fall-through-vs-branch idiom used throughout
  /// this cluster (see <see cref="Node009AddressBus"/>).
  ///
  /// 'start', word by word (per the user's own commentary on this
  /// transcription): 'left b!' points B at node 008 (the control-pins node),
  /// the port 'cmd'/'w16'/'r16' keep using for the rest of the routine. The
  /// two repeats of 'out io data stop' that follow are confirmed
  /// intentional, not a transcription duplicate -- they deliberately fill
  /// eight of the F18A's ten CIRCULAR data-stack slots (DB001 2.3.2 --
  /// pushing always lands on the ring's next slot and overwrites whatever
  /// was there ten pushes ago, rather than growing; popping simply exposes
  /// the previous slot's still-resident value rather than erasing anything)
  /// with two full repeats of the four literal values 'cmd'/'w16'/'r16' need
  /// most often, so later code can consume 'out'/'io'/'data'/'stop' for free
  /// wherever they naturally resurface through ordinary 'drop'/pop traffic,
  /// instead of spending a real '@p' fetch (an opcode plus a data word, at
  /// real time cost) on each one every time it's needed -- a one-time
  /// priming cost at 'start' in exchange for cheaper repeated use
  /// afterward. 'in io a! !' then points A at this node's own I/O register
  /// ('io') and stores 'in' there, switching the external SRAM data bus to
  /// INPUT mode. Finally 'down a! !b' points A at 'down' -- the local port
  /// toward node 107, which is this routine's own resting/default value for
  /// A between requests (not the F18A hardware reset default DB013's '/a'
  /// directive describes) -- and '!b' sends the value newly exposed on top
  /// of the stack, 'stop', out to node 008 over B. Those last two 'a!'
  /// targets ('io', then 'down') are one-time transient values that land in
  /// two of the ring's ten slots and get naturally overwritten again by
  /// 'cmd'/'w16'/'r16's own ordinary push traffic soon after execution
  /// begins; the primed 'out'/'io'/'data'/'stop' 4-cycle occupying the other
  /// eight slots is never touched by that traffic and so persists
  /// indefinitely. That is the steady-state pattern the user describes, S
  /// and T on the right: "[io data stop out io data stop out | io data]" --
  /// an 8-word repeating cycle read around the ring's full 10-slot view,
  /// advanced through with plain 'drop's wherever 'cmd'/'w16'/'r16' need the
  /// next literal in the cycle.
  ///
  /// Verified against this project's own compiler in a standalone harness:
  /// compiles with zero diagnostics, 35 words used, entry point resolves to
  /// 'start' at 0x020.
  /// </summary>
  public const string Node007DataBusAndControl = """
      # 0x14555 const in
      # 0x15555 const out
      # 0x3557F const stop
      entry start
      # 0x20 org
      : start
        left b!
        out io data stop
        out io data stop in io a! !
        down a! !b
      : cmd
        @ !b
        @ -if
      : w16
        !b @ a >r >r a! r> ! a! !
        0x13 for unext
        !b in ! r> a!
        cmd ;
      : r16
        then !b a >r a! drop drop
        0x40 for unext
        !b @ r> a! !
        cmd ;
      """;

  /// <summary>
  /// Node 107 -- interface. Transcribed directly from the user's own
  /// hand-translation of AN003's real listing: the FULL, 3-master polling
  /// node (section 4.1's "cx"/"cmd"/"re"/"cmds"/"poll"), not the earlier
  /// degenerate single-master (section 6.3) reimplementation this method
  /// used to build. Because this version polls all three neighbor ports
  /// itself ('poll's own "right"/"left"/"up ... cmds" dispatch), it is no
  /// longer generated per master at install time -- it is the same fixed
  /// image regardless of which of 106/108/207 is acting as master, so
  /// <c>SramClusterInstaller</c> deploys it unparameterized. This also means
  /// 'mk!' (see AN003's own protocol table) is live, real logic here instead
  /// of the degenerate version's deliberate no-op.
  ///
  /// Two things the earlier prose-based reimplementation got wrong, both
  /// confirmed against this real transcription and against the F18A opcode
  /// reference (DB001 2.3.5) and the arrayForth manuals (DB004/DB013
  /// 5.3.2.1/4.2.4.1):
  ///
  /// 1. 'if'/'-if' do NOT pop T. Per DB001's own wording -- "if. If T is
  /// nonzero, continues... If T is zero, jumps" / "-if. If T is negative,
  /// continues... If T is positive, jumps" -- neither description says the
  /// value is consumed, in explicit contrast to every arithmetic/memory
  /// opcode in the same reference that DOES ("or... Pops data stack", "!b...
  /// pops the data stack"). AN003's own 'cmd' confirms this directly: it
  /// dispatches with a bare '@ -if' and never dups a spare copy first,
  /// because -if leaves the fetched word sitting on the stack for the branch
  /// body to use as-is. The degenerate reimplementation had instead assumed
  /// ordinary (ANS-Forth-style) popping 'if'/'-if' and 'dup'-ed a spare copy
  /// ahead of every dispatch test, which under the real hardware behavior
  /// left a permanent extra word on node 107's data stack after every single
  /// request -- compounding every request against the F18A's 10-deep
  /// CIRCULAR data stack (DB001 2.3.2) until real, still-needed values got
  /// silently overwritten.
  ///
  /// 2. The entry point is 're', not the start of the source ('cx'). The
  /// first attempt at wiring this transcription in used the compiler's
  /// default entry (address 0, i.e. 'cx') because no 'entry' directive was
  /// present in the transcribed text; the user confirmed 're' is the correct
  /// entry word. Declared here via 'entry re' before the first 'org', which
  /// this compiler resolves after the whole source compiles (forward
  /// reference to a word not yet defined is fine).
  ///
  /// RESOLVED (previously flagged "STILL OPEN"): this transcription never
  /// sets B toward node 007 (AN003's 'down' direction) in-line -- 'cx's own
  /// '!b'/'@b' calls rely on B already being there, and unlike the
  /// degenerate version's explicit 'down b!' in its own 'start' word, this
  /// real transcription has no 'start' word at all. The user confirmed this
  /// is intentional: AN003's real node configuration is supplied as
  /// deployment-time metadata via DB013's node-configuration directives, not
  /// as compiled code. '# down /b' below tells the compiler this node's
  /// startup B value is 'down' (the local port toward node 007); the
  /// compiler surfaces that as <c>F18CompileResult.InitialB</c>, and
  /// <see cref="SramClusterInstaller"/> applies it via
  /// <c>KrakenLiveController.WriteBAsync</c> before jumping into 're'.
  ///
  /// Verified against this project's own compiler in a standalone harness:
  /// compiles with zero diagnostics, 60 words used (comfortably inside the
  /// 64-word RAM budget), entry point resolves to 're' (0x018), InitialB
  /// resolves to 'down' (0x115).
  /// </summary>
  public const string Node107Interface = """
      entry re
      # down /b
      # 0 org
      : cx ( wp-) over >r @ dup
        !b over !b @b r> inv xor if
        @ dup xor 0xff ! ;
        then drop !b inv !b @ !b 0xFFFF ! ;
      : cmd @ -if @ [ ' cx ] -until inv !b !b @ !b ;
        then @ -if
        inv >r drop drop r> if
        drop drop @ 2* over inv ahead [ swap ]
        then drop and @ over over 2*
        then and xor
      : re 0x15555 dup ahead [ swap ]
        then !b !b @b ! ;
      : cmds a! cmd
      : poll then io a!
        begin drop over over @ xor and until
        over over and if and and
        dup 0x10000 and if right ahead [ swap ] then
        drop 0x1000 over and if left ahead [ swap ] then
        drop dup up then then
        a! and xor dup ! [ ' re ] end
        then drop 2* 2* -if right cmds ;
        then 2* 2* 2* 2* -if left cmds ;
        then up cmds ;
      """;

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
  /// node's OWN local port toward node 107 (see
  /// <c>KrakenTopology.PortName(masterCoordinate, 107)</c>; node 107's own
  /// <see cref="Node107Interface"/> is no longer parameterized per master --
  /// it polls all three neighbor ports itself -- but this master-side port
  /// is still fixed per master, since Kraken always puppets one specific
  /// master node) -- itself, every call, rather than relying on some earlier
  /// puppet operation having left B pointed there already.
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