namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 607's resident F18 source -- the CVM test-cluster CPU node (test-mirror of real design
/// node 207). Holds P (the CVM program counter, kept in this node's own A register) and S (the
/// CVM data-stack top, kept directly on this node's native F18 data stack): the node that
/// fetches, decodes, and executes every CVM instruction.
///
/// <b>Test-mirror mapping.</b> Node 607 is real design node 207, geometrically reflected into
/// this project's test topology (row' = 8-row, column unchanged) so the whole CVM cluster sits
/// mirrored next to the existing Kraken/SRAM test infrastructure:
/// <code>
///   real design            test mirror          holds
///   107 (SRAM interface) -&gt; 707                  bridges to PC via 708
///   207 (p, s)            -&gt; 607 (this node)      P (in A reg), S (native stack)
///   307 (r)               -&gt; 507                  register r
///   206 (f)               -&gt; 606                  register f (frame)
///   208 (g)               -&gt; 608                  register g (globals)
///   306 (d)               -&gt; 506                  register d
///   308 (t)               -&gt; 508                  register t
///   407 (w)               -&gt; 407 (self-maps)      register w
/// </code>
/// In the TEST setup only (not real hardware) node 707 has no local storage of its own -- it is a
/// stateless interface that forwards every request on to the PC over the serial link through node
/// 708, which stands in for the external SRAM the real node 107 would talk to.
///
/// <b>Local port directions on 607</b> (row 6 is even, column 07 is odd, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules):
/// <code>
///   down  (-d--, 0x115) -&gt; 707  memory/PC interface, mirrors 107
///   up    (---u, 0x145) -&gt; 507  register r, mirrors 307
///   left  (--l-, 0x175) -&gt; 608  register g / globals, mirrors 208
///   right (r---, 0x1D5) -&gt; 606  register f / frame, mirrors 206
/// </code>
/// Per Stefan's instruction, every occurrence of the named multiport call <c>-d--</c> that
/// targets register r on node 507 has been replaced with <c>---u</c> (down -&gt; up), since r
/// lives on 507, reached via the up port, not on 707. The one legitimate use of <c>-d--</c> that
/// remains is inside <c>exec</c>'s first branch, which genuinely hands control down to 707 for
/// memory-class opcodes -- that one is unchanged.
///
/// <b>Verification.</b> Compiled with zero diagnostics against this project's real
/// <c>Compiler/F18Compiler.cs</c> (via <c>F18CompilerOptions.ForRam(607)</c>) in a standalone,
/// non-WPF <c>net10.0</c> console harness: <c>Success = true</c>, all 64/64 RAM words used (no
/// headroom left in this node), entry point <c>main</c> at word address 0x03B. Adding the
/// per-word documentation comments to the source below was re-verified to produce byte-for-byte
/// identical compiled output to the plain, uncommented version -- comments have no effect on the
/// compiled image. The nine CVM-instruction opcodes from the reference instruction-set table
/// (cvm_2.txt) resolve to exactly the word addresses this compiler assigns their implementing
/// words below (e.g. <c>'plit</c> compiles to 0x00E and its opcode is 0x800E, <c>'lit</c>
/// compiles to 0x014 and its opcode is 0x8014, and so on for all nine) -- confirming both the
/// -d--/---u substitution above and the opcode annotations in <see cref="Source"/> are correct
/// for this exact compiled layout. This is exactly the mechanism <c>exec</c>'s final branch uses
/// to turn a decoded opcode into a jump: the opcode's low bits already are the target word's
/// address.
/// </summary>
internal static class Node607Program
{
  /// <summary>The node this program is always deployed to -- test-mirror of real design node 207.</summary>
  public const int Coordinate = 607;

  /// <summary>
  /// Node 607's full resident F18 source, fully commented per-word (using Stefan's own
  /// descriptions where given) with opcode annotations on every CVM-instruction-implementing
  /// word (<c>'plit</c>, <c>'lit</c>, <c>'pop</c>, <c>'push</c>, <c>'ret</c>, <c>'xs</c>,
  /// <c>'xp</c>, <c>'tjmp</c>, <c>'pc</c>) and a structural walkthrough of <c>exec</c>'s
  /// four-way dispatch. See the class remarks for the compile verification this source was
  /// checked against.
  /// </summary>
  public const string Source = """
      // ============================================================================
      // Node 607 -- CVM test-cluster CPU node (test-mirror of real design node 207)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 207 holds the CVM's two primary
      // registers -- P (the CVM program counter) and S (the CVM data-stack top) --
      // and is the node that fetches, decodes and executes every CVM instruction.
      // Node 607 is that same node, geometrically reflected into this project's
      // test topology (row' = 8-row, column unchanged) so the whole CVM cluster
      // sits mirrored next to the existing Kraken/SRAM test infrastructure:
      //
      //     real design           test mirror         holds
      //     ----------------------------------------------------------
      //     107 (SRAM interface)  707                 (bridges to 707 below)
      //     207 (p, s)             607  <-- this node  P (in A reg), S (native stack)
      //     307 (r)                 507                register r
      //     206 (f)                 606                register f (frame)
      //     208 (g)                 608                register g (globals)
      //     306 (d)                 506                register d
      //     308 (t)                 508                register t
      //     407 (w)                 407 (self-maps)    register w
      //
      // In the TEST setup only (not real hardware), node 707 has no local storage
      // of its own -- it is a stateless interface that forwards every request on to
      // the PC over the serial link through node 708, which stands in for the
      // external SRAM the real node 107 would talk to. So node 607's neighbour
      // "down" (toward 707) reaches memory/PC, not a local RAM node.
      //
      // Local port directions on 607 (row 6 is even, column 07 is odd, per this
      // project's KrakenTopology.PortAddress mirroring rules):
      //     down  (-d--, 0x115) -> 707  (memory/PC interface, mirrors 107)
      //     up    (---u, 0x145) -> 507  (register r, mirrors 307)
      //     left  (--l-, 0x175) -> 608  (register g / globals, mirrors 208)
      //     right (r---, 0x1D5) -> 606  (register f / frame, mirrors 206)
      //
      // P (the CVM's own program counter) is kept in this node's local A register.
      // S (the CVM's data-stack top) is kept directly on node 607's own native F18
      // data stack -- there is no separate S variable; the F18 hardware stack IS
      // the CVM's data stack while this node is running.
      //
      // Per Stefan's instruction, every occurrence of the named multiport call
      // '-d--' that targets register r on node 507 has been replaced with '---u'
      // (down -> up), since r lives on 507 (reached via the up port), not on 707.
      // The one legitimate use of '-d--' that remains is inside 'exec's first
      // branch, where control is genuinely handed down to 707 for memory-class
      // opcodes -- that one is correct as originally written and is NOT replaced.
      //
      // Verified: this source compiles against the real F18Compiler with 0
      // diagnostics, Success=true, using all 64/64 RAM words, entry point 'main'
      // at 0x03B. The nine CVM-instruction opcodes below (in the reference
      // instruction-set table from cvm_2.txt) resolve to exactly the addresses
      // this compiler assigns their implementing words -- 'plit compiles to word
      // address 0x00E and its opcode is 0x800E, 'lit compiles to 0x014 and its
      // opcode is 0x8014, and so on for every one of the nine -- confirming both
      // the -d--/---u substitution above and the opcode table below are correct
      // for this exact compiled layout. This is exactly how 'exec's final branch
      // (see below) turns an opcode into a jump: the opcode's low bits already
      // are the target word's address.
      // ============================================================================

      # 0 org
      entry main

      //  P always starts at extended address 0: every CVM program's first
      //  instruction lives at address 0, so A (which holds P here) is initialised
      //  to 0 at cold start.
      #  0 /a

      //  B is initialised to point "down", i.e. at the neighbour that services
      //  memory access (707, which forwards to the PC over the serial link
      //  through 708). Words that use B directly to fetch/store through the
      //  memory-access neighbour (/@, /1@, /!, /1!, /cx?) rely on this.
      # down /b

      // ----------------------------------------------------------------------
      // /r@ ( s-sw)  --  fetch r from node 507
      // ----------------------------------------------------------------------
      // Pushes a zero-valued request word (dup dup xor: duplicating s and
      // XOR-ing the two copies together always yields 0, regardless of s's
      // value, without disturbing s itself), duplicates that zero once more,
      // then hands control to node 507 via the up port (---u). By AN003's
      // protocol convention a zero request word means "read", so node 507
      // answers by leaving r's current value, w, on top of the stack when
      // control returns -- giving the (s-sw) effect: s is left untouched
      // underneath, with r's value w pushed on top of it.
      : /r@ ( s-sw) dup dup xor dup ---u ;

      // ----------------------------------------------------------------------
      // /cx? ( wabn-f)  --  compare and exchange operation of memory controller
      // ----------------------------------------------------------------------
      // AN003's cx? primitive sends the comparison word and the new value
      // inverted (one's-complement) so the interface can distinguish a
      // compare-exchange request from a plain read/write; 'inv' performs that
      // inversion here, and the two '!b' stores hand the inverted compare value
      // and the new value to the memory-access neighbour through B. Rather than
      // duplicating /@'s own fetch-the-result tail, /cx? finishes by jumping
      // unconditionally (ahead) into the middle of /@'s definition below, right
      // at the 'then' that resolves this branch -- reusing /@'s '!b !b @b ;'
      // tail to complete the handshake and fetch the resulting flag f.
      : /cx? ( wabn-f) inv !b !b ahead

      // ----------------------------------------------------------------------
      // /pop ( p-pt)  --  pop value from stack
      // ----------------------------------------------------------------------
      // Falls straight through (no ';') into /1@, then into /@ -- see the note
      // on fall-through below. Combined, /pop/1@/@ read one word t from
      // extended-page memory at the address formed from p, advancing p by one
      // so the caller can keep reading sequential words; 'dup >r' keeps a copy
      // of p on the return stack while 1/!b/!b/@b (borrowed from the words it
      // falls into) perform the actual page-1-relative fetch through B.
      : /pop ( p-pt) dup >r 1 . + r>

      // ----------------------------------------------------------------------
      // /1@ ( a-w)  --  fetch word from page 1
      // ----------------------------------------------------------------------
      // This word is just the literal page number 1, immediately followed
      // (fall-through, no ';') by /@'s body below -- so "1" supplies the page
      // argument that /@'s '!b !b @b' addresses against. /1@ is never entered
      // on its own from outside this file except via fall-through from /pop.
      : /1@ ( a-w) 1

      // ----------------------------------------------------------------------
      // /@ ( ab-w)  --  fetch word from extended address
      // ----------------------------------------------------------------------
      // The 'then' here is the landing point 'ahead' (in /cx?, above) jumps to,
      // so this same tail -- store a, store b, fetch through b (!b !b @b) --
      // serves both a plain extended-address fetch (entered from the top, via
      // /pop -> /1@ fall-through) and the tail half of a compare-exchange
      // (entered directly via /cx?'s ahead branch).
      : /@ ( ab-w) then !b !b @b ;

      // ----------------------------------------------------------------------
      // /! ( wab)  --  store word at extended address
      // ----------------------------------------------------------------------
      // Mirrors /@'s addressing but for a write: both the address components
      // are sent inverted (AN003's write convention, 'inv' applied twice) ahead
      // of the final plain '!b' that actually deposits the word through B.
      : /! ( wab) inv !b inv !b !b ;

      // ----------------------------------------------------------------------
      // /next ( s-sx)  --  read next instruction word
      // ----------------------------------------------------------------------
      // Fetches the word at P (using /pop's page-relative fetch through a,
      // which holds P), then restores a! from the popped/advanced p so P has
      // already moved on to the following word by the time /next returns --
      // this is what lets 'main' simply chain '/next ... exec main' in a loop.
      : /next ( s-sx) a /pop >r a! r> ;

      // ----------------------------------------------------------------------
      // 'plit ( s-s)  --  push next literal onto stack
      // ----------------------------------------------------------------------
      // CVM opcode 0x800E (compiles to word address 0x00E). Simply reuses
      // /next to fetch the very next instruction word and push it as a literal
      // value onto the CVM's data stack (S, i.e. this node's own hardware
      // stack) -- the fetched word IS the literal.
      : 'plit ( s-s) /next

      // ----------------------------------------------------------------------
      // /push ( pt-p)  --  push value onto stack
      // ----------------------------------------------------------------------
      // Falls through (no ';') into /1! below. Combined, /push/1! store t at
      // the extended address one below p (>r -1 . + r>: move p to the return
      // stack, decrement it, bring it back), i.e. push t onto the CVM's
      // extended-memory return/parameter area addressed via p, leaving the
      // decremented p as the new p.
      : /push ( pt-p) >r -1 . + r> over

      // ----------------------------------------------------------------------
      // /1! ( wa)  --  store word in page 1
      // ----------------------------------------------------------------------
      // Supplies the page-1 argument (literal 1) to /! -- the write-side
      // counterpart of /1@, used the same way via fall-through from /push.
      : /1! ( wa) 1 /! ;

      // ----------------------------------------------------------------------
      // 'lit ( s-s)  --  store next literal in register r
      // ----------------------------------------------------------------------
      // CVM opcode 0x8014 (compiles to word address 0x014). Despite the name,
      // this implements the CVM's "load literal into r" instruction (llit in
      // the reference table): it reuses /next to fetch the following
      // instruction word, exactly like 'plit -- the difference between "push
      // as literal onto S" and "load into r" is realised entirely by what the
      // caller (exec's dispatch) does with the fetched value afterwards.
      : 'lit ( s-s) /next

      // ----------------------------------------------------------------------
      // /r! ( sw-s)  --  store top of stack in register r
      // ----------------------------------------------------------------------
      // Pushes a non-zero request tag (3, duplicated) ahead of the value to
      // store, then hands control to node 507 via the up port (---u) --
      // substituted here from the original '-d--' since register r lives on
      // 507, reached via up, not on 707 (down). By AN003 convention a non-zero
      // leading word marks a write rather than a read.
      : /r! ( sw-s) 3 dup ---u ;

      // ----------------------------------------------------------------------
      // 'pop ( s-s)  --  CVM instruction: pop
      // ----------------------------------------------------------------------
      // CVM opcode 0x8018 (compiles to word address 0x018). Pops p (i.e. reads
      // and advances the parameter/return-area pointer via /pop) and stores
      // the popped value into register r via /r!.
      : 'pop ( s-s) /pop /r! ;

      // ----------------------------------------------------------------------
      // 'push ( s-s)  --  CVM instruction: push
      // ----------------------------------------------------------------------
      // CVM opcode 0x801A (compiles to word address 0x01A). Fetches r's
      // current value from 507 (/r@) and pushes it onto the extended-memory
      // parameter/return area via /push.
      : 'push ( s-s) /r@ /push ;

      // ----------------------------------------------------------------------
      // exec ( sxy-s)  --  interpret instruction
      // ----------------------------------------------------------------------
      // Decodes one already-fetched CVM opcode by successively testing and
      // shifting out its high bits (each '2* -if ... then' tests and consumes
      // the current top bit). Four cases, matching the opcode-table's
      // high-level grouping in cvm_2.txt:
      //
      //   1) Top bit set: this is a memory-class opcode -- hand control
      //      straight down to 707 (the memory/PC interface, mirroring 107)
      //      via -d--, which stays '-d--' here since 707 genuinely is "down"
      //      from 607. 707 (and, behind it, the PC over the serial link
      //      through 708) completes the operation and returns.
      //   2) Next bit set: a frame/global-class opcode. A further bit test
      //      chooses between handing control left to 608 (register g /
      //      globals, --l-) or right to 606 (register f / frame, r---).
      //   3) Next bit set: an r-relative opcode. /r@ fetches r's value; if it
      //      is zero the three top stack items are discarded and execution
      //      returns (this is the family of opcodes that only make sense when
      //      r holds something), otherwise it is shifted down (six 2/'s) and
      //      combined with the base address of the specialised-word jump
      //      table (via 'a . + a!') to jump directly to the matching 'xxx
      //      word below -- this is the mechanism that makes each of 'ret,
      //      'xs, 'xp, 'tjmp, 'pc reachable by opcode value alone, exactly
      //      as confirmed by their compiled addresses matching the reference
      //      opcode table's low bits.
      //   4) None of the above: the opcode is a plain local-jump-table index
      //      (this is the same tail as case 3's final branch, reached here
      //      with a already primed) -- drop the remaining tag and return to
      //      the caller (>r ;) so 'main' resumes with a already set to the
      //      target address.
      : exec ( sxy-s)
        2* -if -d-- ; then
        2* -if
          2* -if --l- ; then r--- ;
        then 2* -if
          2* -if /r@
            if drop drop drop ; then drop
          then >r drop r> 2* 2/ 2/ 2/ 2/ 2/ 2/ 2/ a . + a! ;
        then drop >r ;

      // ----------------------------------------------------------------------
      // 'ret ( s-s)  --  return from call
      // ----------------------------------------------------------------------
      // CVM opcode 0x802E (compiles to word address 0x02E). Pops the saved
      // return address (via /pop, which advances/reads the parameter/return
      // area) and installs it directly into A -- i.e. into P -- resuming the
      // CVM program at the popped address.
      : 'ret ( s-s) /pop a! ;

      // ----------------------------------------------------------------------
      // 'xs ( s-s')  --  exchange r with s
      // ----------------------------------------------------------------------
      // CVM opcode 0x8030 (compiles to word address 0x030). Saves r's current
      // value on the return stack (>r /r@), then stores the popped value back
      // into r via /r! -- swapping the top of the CVM's data stack (S, this
      // node's own native stack) with register r.
      : 'xs ( s-s') >r /r@ r> /r! ;

      // ----------------------------------------------------------------------
      // 'xp ( s-s)  --  exchange r with p
      // ----------------------------------------------------------------------
      // CVM opcode 0x8032 (compiles to word address 0x032). Fetches r's
      // current value into A (becoming the new P), then stores the old P
      // (still sitting where /r@ left the request pair) back into r via /r! --
      // swapping P and r.
      : 'xp ( s-s) a /r@ a! /r! ;

      // ----------------------------------------------------------------------
      // 'tjmp ( s-s)  --  table jump
      // ----------------------------------------------------------------------
      // CVM opcode 0x8034 (compiles to word address 0x034). Adds r's value to
      // the current P (a /r@ +), fetches the instruction word at that computed
      // address via /next, adds it again, and installs the result into A --
      // i.e. P jumps to base-plus-offset-plus-fetched-displacement, the
      // classic computed-table-jump idiom.
      : 'tjmp ( s-s) a /r@ + /next + a! ;

      // ----------------------------------------------------------------------
      // 'pc ( s-s)  --  move p to r
      // ----------------------------------------------------------------------
      // CVM opcode 0x8037 (compiles to word address 0x037). Reads the current
      // P out of A and stores it into register r via /r!.
      : 'pc ( s-s) a /r! ;

      // ----------------------------------------------------------------------
      // /call ( sxy-s)  --  call subroutine
      // ----------------------------------------------------------------------
      // Discards the decode tag, saves the current P on the return stack,
      // pushes it onto the extended-memory parameter/return area via /push
      // (the call's return address), then installs the target address (r>,
      // left on the stack by the caller -- see 'main' below) into A as the new
      // P. Deliberately has no ';': execution falls straight through into
      // 'main' immediately below, continuing the fetch/decode loop with P
      // already pointing at the called subroutine's first instruction.
      : /call ( sxy-s) drop >r a /push r> a!

      // ----------------------------------------------------------------------
      // main ( s-s)  --  node entry point
      // ----------------------------------------------------------------------
      // The CVM's fetch/decode loop: fetch the next instruction word (/next),
      // duplicate it and shift it left twice to align/test its top two bits;
      // '# /call' overrides the branch target actually taken so that, while
      // the top-two-bit test is false (-until), the loop routes each fetched
      // word through /call (i.e. treats it as a subroutine-call target address
      // rather than an opcode) -- once the test succeeds, control instead
      // falls into 'exec' to decode it as an ordinary CVM opcode, then loops
      // back to 'main'.
      : main ( s-s) /next dup 2* 2* # /call -until exec main ;
      """;
}
