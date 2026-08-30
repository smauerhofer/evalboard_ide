namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 407's resident F18 source -- the CVM test-cluster register-w / port node. Unlike every other
/// node in this cluster, 407 <b>self-maps</b>: row' = 8-row = 8-4 = 4, its own row, so the real
/// design's node 407 and this project's test-mirror node 407 are the literal same physical node. See
/// <see cref="Node607Program"/>'s remarks for the full test-mirror mapping table.
///
/// <b>A pure servant of 507</b>, the same relationship as <see cref="Node506Program"/>'s and
/// <see cref="Node508Program"/>'s (see their remarks in full). 507 reaches 407 via a named multiport
/// call (507's own <c>-d--</c>), parking 507's OWN P at the port; from then on every <c>@p</c>/<c>!p</c>
/// 507 executes is a live handshake with whatever 407 sends through ITS reciprocal B port. 407 imports
/// 507's exported words via <c># 507 import</c>.
///
/// <b>Local port directions on 407</b> (row 4 is even, column 07 is odd, per this project's
/// <c>KrakenTopology.PortAddress</c> mirroring rules, confirmed directly against
/// <c>KrakenConfiguration.PortAddress</c> in this project's own source):
/// <code>
///   down  (-d--, 0x115) -&gt; 507  the node that puppets 407 (matches this file's own corrected
///                                 "# down /b")
///   up    (---u, 0x145) -&gt; 307  the REAL, un-mirrored r-register node -- NOT used by this file; the
///                                 source originally imported/pointed at 307, both confirmed typos,
///                                 corrected below
///   left  (--l-, 0x175) -&gt; 408  matches this file's own "# left /a" -- NOTE: unlike every other node
///                                 in this cluster, 407 uses A itself as a live PORT ADDRESS, not a
///                                 data register -- see the note on 'in/'out/'xpt below
///   right (r---, 0x1D5) -&gt; 406  not part of this cluster
/// </code>
///
/// <b>The typo fixes applied in this version</b> (both confirmed by Stefan). The import target was
/// originally <c># 307 import</c> -- corrected to <c># 507 import</c>, matching every other node in
/// this test-mirror cluster, which all talk to 507 (not the real, un-mirrored 307). Once the import
/// target was fixed, the B-port directive needed to match: it was originally <c># up /b</c> (which,
/// per <c>KrakenConfiguration.PortAddress</c>, reaches 307 from this node's position -- consistent
/// with the ORIGINAL, un-corrected import, but not with the corrected one) -- corrected to
/// <c># down /b</c>, which reaches 507.
///
/// <b>A design point specific to this node.</b> A is initialised to the CONSTANT <c>left</c> (0x175,
/// a port address) rather than a data value the way every other node in this cluster initialises A.
/// <c>'in</c>/<c>'out</c> below read/write <c>[A]</c> directly (the plain F18A <c>@</c>/<c>!</c>
/// opcodes) -- live I/O with whatever is wired to 407's own left-neighbour position -- and
/// <c>'xpt</c> below exchanges THIS ADDRESS (not a data value) with r, matching Stefan's own
/// description "exchanges port register with R": "port register" here is A itself, since A's role on
/// this node IS the port pointer.
///
/// Every op-word below reaches r (507's own register) exactly the way 506's and 508's do:
/// <c>r@</c>/<c>r!</c> ship a packed request to 507 over B and receive/send the reply, the same
/// relay idiom used throughout this cluster.
///
/// <b>'main'/'leave'</b> here are byte-for-byte the same shape as node 506's (see its remarks for the
/// full self-priming-return-stack derivation): <c>leave</c> ships <c>{return}</c> to 507 (popping
/// 507's OWN R, resuming its local cleanup) then CALLs <c>main</c> again, re-priming this node's own R
/// so that whichever op-word <c>main</c>'s <c>&gt;r</c>/<c>ex</c> dispatches into next returns
/// correctly back to <c>leave</c>'s own tail. None of the op-words below call <c>leave</c> or
/// <c>main</c> themselves, the same shape Stefan already confirmed is fine (cold start with
/// <c>main</c> directly, before <c>leave</c> has ever run once) for node 506.
///
/// <c>main</c>'s very first opcode is <c>drop</c> (also true of 506's and 508's own <c>main</c>),
/// discarding whatever is currently on this node's own local stack before shipping the dispatch
/// handshake -- worth noting is that 407 is the only one of the three that also carries an explicit
/// <c># 0 # 1 /stack</c> startup directive, priming ITS initial data stack with a single value (0) at
/// cold boot. That most likely exists so the very first <c>drop</c>, on the very first dispatch
/// (before any prior operation has left something natural to discard), has a well-defined zero to
/// drop rather than whatever the hardware's own reset state of the circular stack happens to contain
/// -- inferred, not stated outright.
///
/// <b>A second instance of this project's "CALL lands on the next compiled word regardless of
/// caller" trick</b> (the same shape as node 506's <c>csr16</c>/<c>c!</c>, itself modelled on 507's
/// <c>s/2put</c>/<c>s/put</c>): <c>sr16</c> has no <c>;</c> of its own and falls straight into
/// <c>c!</c> immediately below it (masking to 2 bits after the shift). <c>'hi@</c> below CALLs
/// <c>sr16</c> (a genuine CALL, since <c>sr16</c> was compiled much earlier); when <c>sr16</c> falls
/// into <c>c!</c> and <c>c!</c>'s own <c>;</c> fires, it pops the return address <c>'hi@</c>'s OWN
/// call pushed -- which is intrinsically the address of the word right after <c>'hi@</c>'s
/// single-word body, i.e. <c>r!</c>'s own start, exactly where <c>'hi@</c> would have landed anyway
/// had it fallen through directly. Net effect: <c>'hi@</c> = dup, shift right 16, mask to 2 bits (via
/// <c>sr16</c>/<c>c!</c>), then <c>r!</c>'s own store-into-507's-r logic, all in one call.
///
/// <b>A note on confidence.</b> No per-word descriptions were given for most of this drop; Stefan's
/// own trailing comment block covers only <c>'xpt</c>, <c>'out</c>, <c>'in</c>, <c>'hi@</c>,
/// <c>'lo@</c>, <c>'hi!</c>, and <c>'lo!</c>. Everything else is inferred from the code,
/// cross-checked against the compiled addresses and against node 506/507/508's already-confirmed
/// idioms -- treat it with the same lower confidence as node 607's <c>exec</c> or node 506's own
/// word-by-word notes.
///
/// <b>Verification.</b> This source (with the import and B-port typos corrected, both confirmed by
/// Stefan) compiles against the real <c>Compiler/F18Compiler.cs</c> with 0 errors
/// (<c>Success = true</c>), importing node 507's exported symbols via <c># 507 import</c>. 38 of 64
/// RAM words used, entry point <c>main</c> at word address 0x000, <c>InitialA</c> = 0x175 ("left"),
/// <c>InitialB</c> = 0x115 ("down"), <c>InitialStack</c> = [0]. Two informational warnings are
/// expected and benign: F18C050 for both <c>main</c> and <c>leave</c>, each redefining a name
/// imported from node 507 -- both nodes define their own independent pair, and 407 never needs to
/// call INTO 507's versions by name, so the shadowing is intentional. Adding the per-word
/// documentation comments to <see cref="Source"/> was re-verified to produce byte-for-byte identical
/// compiled output to the plain, typo-corrected version.
/// </summary>
internal static class Node407Program
{
  /// <summary>The node this program is always deployed to -- self-mapping test-mirror of real design node 407 (register w / port).</summary>
  public const int Coordinate = 407;

  /// <summary>
  /// Node 407's full resident F18 source, fully commented per-word (Stefan's own descriptions are
  /// quoted where given -- 'xpt, 'out, 'in, 'hi@, 'lo@, 'hi!, 'lo! -- everything else is inferred)
  /// with a traced control-flow walkthrough of the sr16/c!/'hi@/r! address-collision reuse. See the
  /// class remarks for the compile verification this source was checked against, including its
  /// cross-node import of node 507's symbol table via <c># 507 import</c>, and the two confirmed
  /// typo fixes (<c># 307 import</c>-&gt;<c># 507 import</c>, <c># up /b</c>-&gt;<c># down /b</c>).
  /// </summary>
  public const string Source = """
      ( cvm in/out support, 1111_????_????_????)
      // ============================================================================
      // Node 407 -- CVM test-cluster register-w / port node (self-mapping: real
      // design node 407 and its test mirror are the same physical node, register w)
      // ============================================================================
      //
      // Real hardware role (per cvm_2.txt): node 407 holds w, the CVM's I/O-port
      // register, and implements the CVM's port read/write opcodes plus the
      // hi/lo-half transfer helpers that move an 18-bit port value to and from the
      // shared r register (held on 307/507) in 16-bit-plus-2-bit pieces. Node 407
      // is the one node in this cluster that "self-maps" (row' = 8-4 = 4, its own
      // row): there is only one physical 407, shared by both the real, un-mirrored
      // design and this project's test-mirror topology -- see Node607Program.cs's
      // remarks for the full mirror-mapping table.
      //
      // A pure servant of 507, the same relationship as 506's and 508's (see
      // Node506Program.cs's remarks in full): 507 reaches 407 via a named
      // multiport call (507's own "-d--"), parking 507's OWN P at the port; from
      // then on every @p/!p 507 executes is a live handshake with whatever 407
      // sends through ITS reciprocal B port. 407 imports 507's exported words via
      // '# 507 import'.
      //
      // Local port directions on 407 (row 4 is even, column 07 is odd, per this
      // project's KrakenTopology.PortAddress mirroring rules, confirmed directly
      // against KrakenConfiguration.PortAddress in this project's own source):
      //     down  (-d--, 0x115) -> 507  (the node that puppets 407; matches this
      //                                   file's own corrected "# down /b")
      //     up    (---u, 0x145) -> 307  (the REAL, un-mirrored r-register node --
      //                                   NOT used by this file; the source
      //                                   originally imported/pointed at 307, both
      //                                   confirmed typos, corrected below)
      //     left  (--l-, 0x175) -> 408  (matches this file's own "# left /a" --
      //                                   NOTE: unlike every other node in this
      //                                   cluster, 407 uses A itself as a live PORT
      //                                   ADDRESS, not a data register -- see the
      //                                   note on 'in/'out/'xpt below)
      //     right (r---, 0x1D5) -> 406  (not part of this cluster)
      //
      // The typo fixes applied in this version (both confirmed by Stefan): the
      // import target was originally '# 307 import' -- corrected to '# 507
      // import', matching every other node in this test-mirror cluster, which all
      // talk to 507 (not the real, un-mirrored 307). Once the import target was
      // fixed, the B-port directive needed to match: it was originally '# up /b'
      // (which, per KrakenConfiguration.PortAddress, reaches 307 from this node's
      // position -- consistent with the ORIGINAL, un-corrected import, but not
      // with the corrected one) -- corrected to '# down /b', which reaches 507.
      //
      // A design point specific to this node: A is initialised to the CONSTANT
      // 'left' (0x175, a port address) rather than a data value the way every
      // other node in this cluster initialises A. 'in'/'out' below read/write
      // [A] directly (the plain F18A '@'/'!' opcodes) -- live I/O with whatever is
      // wired to 407's own left-neighbour position -- and 'xpt' below exchanges
      // THIS ADDRESS (not a data value) with r, matching Stefan's own description
      // "exchanges port register with R": "port register" here is A itself, since
      // A's role on this node IS the port pointer.
      //
      // Every op-word below reaches r (507's own register) exactly the way 506's
      // and 508's do: r@/r! ship a packed request to 507 over B and receive/send
      // the reply, the same relay idiom used throughout this cluster.
      //
      // 'main'/'leave' here are byte-for-byte the same shape as node 506's (see
      // its remarks for the full self-priming-return-stack derivation): 'leave'
      // ships {return} to 507 (popping 507's OWN R, resuming its local cleanup)
      // then CALLs 'main' again, re-priming this node's own R so that whichever
      // op-word 'main's '>r'/'ex' dispatches into next returns correctly back to
      // 'leave's own tail. None of the op-words below call 'leave' or 'main'
      // themselves, the same shape Stefan already confirmed is fine (cold start
      // with 'main' directly, before 'leave' has ever run once) for node 506.
      //
      // 'main's very first opcode is 'drop' (also true of 506's and 508's own
      // 'main), discarding whatever is currently on this node's own local stack
      // before shipping the dispatch handshake -- worth noting is that 407 is the
      // only one of the three that also carries an explicit '# 0 # 1 /stack'
      // startup directive, priming ITS initial data stack with a single value (0)
      // at cold boot. That most likely exists so the very first 'drop', on the
      // very first dispatch (before any prior operation has left something
      // natural to discard), has a well-defined zero to drop rather than
      // whatever the hardware's own reset state of the circular stack happens to
      // contain -- inferred, not stated outright.
      //
      // A second instance of this project's "CALL lands on the next compiled word
      // regardless of caller" trick (the same shape as node 506's csr16/c!, itself
      // modelled on 507's s/2put/s/put): 'sr16' has no ';' of its own and falls
      // straight into 'c!' immediately below it (masking to 2 bits after the
      // shift). 'ldhi' below CALLs 'sr16' (a genuine CALL, since sr16 was compiled
      // much earlier); when sr16 falls into c! and c!'s own ';' fires, it pops
      // the return address 'ldhi's OWN call pushed -- which is intrinsically the
      // address of the word right after 'ldhi's single-word body, i.e. 'r!'s own
      // start, exactly where 'ldhi would have landed anyway had it fallen through
      // directly. Net effect: 'ldhi = dup, shift right 16, mask to 2 bits (via
      // sr16/c!), then r!'s own store-into-507's-r logic, all in one call.
      //
      // No per-word descriptions were given for most of this drop; Stefan's own
      // trailing comment block covers only 'xpt, 'out, 'in, 'ldhi, 'ldlo, 'sthi, and
      // 'stlo. Everything else is inferred from the code, cross-checked against
      // the compiled addresses and against node 506/507/508's already-confirmed
      // idioms -- treat it with the same lower confidence as node 607's 'exec or
      // node 506's own word-by-word notes.
      //
      // ----------------------------------------------------------------------
      // Revision note (this drop, per Stefan: "every word that begins with a '
      // is an opcode. the mnemonic is the same name without the leading '")
      // ----------------------------------------------------------------------
      // Four of this node's own words are RENAMED from the earlier drop below --
      // their compiled bodies (and therefore their compiled addresses) are byte-
      // for-byte unchanged, only the names differ, to match this node's own
      // official cvm mnemonic table (this opcode class is tagged
      // "1111_????_????_????", per this file's own opening comment line -- the
      // same convention node 506/508 already carry as their own first line):
      //     'hi@ -> 'ldhi    (moves hi 2 bits from value to R)
      //     'lo@ -> 'ldlo    (moves lo 16 bit from value to R)
      //     'hi! -> 'sthi    (moves 2 bits from R to hi value)
      //     'lo! -> 'stlo    (moves 16 bits from R to lo value)
      // 'xpt, 'out, and 'in are unchanged. Stefan's own descriptions for all
      // seven were already confirmed in the prior drop (see each word's own
      // comment below) and carry over unchanged to their new names -- only the
      // names moved, nothing about what any of the seven actually does.
      //
      // Verified: this source (with the import and B-port typos corrected, both
      // confirmed by Stefan) compiles against the real F18Compiler with 0 errors
      // (Success=true), importing node 507's exported symbols via '# 507 import'.
      // 38 of 64 RAM words used, entry point 'main' at word address 0x000,
      // InitialA=0x175 ("left"), InitialB=0x115 ("down"), InitialStack=[0] --
      // byte-for-byte identical to the earlier (pre-rename) drop's own compiled
      // words, confirming this revision's rename touched only symbol names, never
      // any compiled code. Two informational warnings are expected and benign:
      // F18C050 for both 'main' and 'leave', each redefining a name imported from
      // node 507 -- both nodes define their own independent pair, and 407 never
      // needs to call INTO 507's versions by name, so the shadowing is
      // intentional.
      //
      // Now that this node's own opcode tag is confirmed (node 507's own 'main
      // dispatch forwards the whole "1111_????_????_????" block here unmasked,
      // via its own "-d--" branch -- see Node507.f18's own 'main' comments, and
      // this project's own cvm-toolchain-design.md), all seven of this node's own
      // tick-prefixed op-words -- 'xpt, 'out, 'in, 'ldhi, 'ldlo, 'sthi, 'stlo --
      // are registered as tagged CVM instructions in
      // Ga144.Cvm.Toolchain.CvmInstructionSet (Ids 65-71) and
      // Ga144.Evb.Ide.Services.CvmAssemblyLanguage.NodeSymbolByMnemonic
      // (Node407TagBits = 0xF000), the same way node 506's and 508's ops are.
      // ============================================================================

      # 507 import

      # 0 org
      entry main

      //  The initial data stack holds a single 0 -- see the header note on why
      //  'main's own leading 'drop (below) most likely needs this.
      # 0 # 1 /stack

      //  A is initialised to the port address 'left' (0x175, toward this node's
      //  own left neighbour) -- NOT a data value, unlike every other node in
      //  this cluster. 'in'/'out' below read/write [A] directly as live port
      //  I/O; 'xpt' exchanges this address itself with r.
      # left /a

      //  B is initialised to point "down", toward 507 -- the master node that
      //  puppets this one. Every !b/@b in this file talks to 507 through B.
      # down /b

      // ----------------------------------------------------------------------
      // main  --  wait for the next dispatch and jump to it (inferred, same
      // shape as node 506's own 'main)
      // ----------------------------------------------------------------------
      // Ships {drop, !p} to 507 as a packed literal: when 507 (its P parked at
      // the port from its own "CALL -d--") executes this, its own 'drop'
      // discards a stack item and '!p' sends 507's new top of stack -- the
      // op-word address 507's own dispatch already selected -- out over the
      // port. '@b' on 407's side receives that address, '>r' parks it, and
      // 'ex' pops it straight back off and jumps there. Because '>r' only added
      // ONE entry on top of whatever was already on R, and 'ex' consumes only
      // that same entry, R is left exactly as it was before 'main' ran -- see
      // the header note on how this keeps the loop self-priming from the
      // second dispatch onward.
      : main A[ drop !p ]] lit !b @b >r ex

      // ----------------------------------------------------------------------
      // leave  --  signal 507 that this operation is done, then wait for the
      // next one (inferred, same shape as node 506's own 'leave)
      // ----------------------------------------------------------------------
      // Ships a single packed {return} word to 507 -- executed by 507 (still
      // fetching over the port from its own parked P), this pops 507's OWN R
      // and resumes 507's own local cleanup code. Then CALLs 'main' again:
      // this is what re-primes 407's own R with a fresh return address (this
      // word's own trailing ';') before 'main's '>r'/'ex' pair consumes just
      // the dispatch entry on top of it, so whichever word runs next returns
      // correctly back here.
      : leave A[ ; ]] lit !b main ;

      // ----------------------------------------------------------------------
      // sr16 ( w-w)  --  shift right 16, then fall into c! below to mask to 2
      // bits (inferred)
      // ----------------------------------------------------------------------
      // 'for'/'unext' loops 8 times (7 for), each pass doing '2/ 2/' -- 16
      // right shifts total. Has no ';' of its own: falls straight through into
      // 'c!' immediately below, so calling sr16 ALSO runs c!'s "3 and" mask
      // before finally returning -- see the header note on 'ldhi's own use of
      // this.
      : sr16 ( w-w) 7 for 2/ 2/ unext

      // ----------------------------------------------------------------------
      // c!  --  mask to the low 2 bits (inferred)
      // ----------------------------------------------------------------------
      // '3 and' keeps only bits 0-1. Reached both as the fall-through tail of
      // sr16 above and (implicitly, via 'ldhi's own CALL to sr16) as part of
      // 'ldhi's own dispatch below.
      : c! 3 and ;

      // ----------------------------------------------------------------------
      // sl16 ( w-w)  --  shift left 16 (inferred, mirrors sr16)
      // ----------------------------------------------------------------------
      // Same 'for'/'unext' loop as sr16, but with '2* 2*' (two left shifts per
      // pass) instead of '2/ 2/'.
      : sl16 ( w-w) 7 for 2* 2* unext ;

      // ----------------------------------------------------------------------
      // r@ ( -w)  --  read 507's own register r (inferred, same relay idiom as
      // node 506's/508's own r@)
      // ----------------------------------------------------------------------
      // Ships {a, !p} to 507: 507's own 'a' pushes 507's A (r), and '!p' sends
      // it back over the port. '@b' on 407's side receives it.
      : r@ ( -w) A[ a !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // 'ldhi  --  moves hi 2 bits from value to R (Stefan's own description;
      // renamed from 'hi@ -- see the revision note above)
      // ----------------------------------------------------------------------
      // 'dup' keeps a copy of the 18-bit value for the caller (or a later
      // 'ldlo call); the CALL to 'sr16 shifts the duplicate right 16 bits and,
      // via sr16's own fall-through into c!, masks it to 2 bits -- isolating
      // exactly the value's high 2 bits. Has no ';' of its own: because sr16's
      // call chain (through c!'s own ';') returns to precisely the next word
      // after this one-word body -- which is 'r!'s own start, immediately
      // below -- calling 'ldhi continues directly into r!'s "store into 507's
      // r" logic (see the header note on this address-collision reuse). Net
      // effect: extracts the high 2 bits and stores them into r, in one call.
      : 'ldhi dup sr16

      // ----------------------------------------------------------------------
      // r! ( w)  --  write 507's own register r (inferred, same relay idiom as
      // node 506's/508's own r!)
      // ----------------------------------------------------------------------
      // Ships {@p, a!} to 507: 507's own '@p' fetches the literal w this
      // word's own trailing '!b' carried across, and 'a!' stores it into 507's
      // A (r). The write-side counterpart of r@ above; also reached as the
      // implicit tail of 'ldhi above.
      : r! ( w) A[ @p a! ]] lit !b !b ;

      // ----------------------------------------------------------------------
      // 'xpt  --  exchanges port register with R (Stefan's own description --
      // "port register" is THIS node's own A, which holds the live port
      // address 'in'/'out' use, not a data value)
      // ----------------------------------------------------------------------
      // 'a' pushes A's current value (the port address); 'r@' fetches 507's r
      // onto the stack; 'a!' stores r's value into A (A now holds whatever r
      // held); 'r!' stores the ORIGINAL A value (still sitting where 'a' left
      // it) into r. A genuine two-way swap of A (this node's port pointer) and
      // r.
      : 'xpt a r@ a! r! ;

      // ----------------------------------------------------------------------
      // 'out  --  writes 18 bit value to port (Stefan's own description)
      // ----------------------------------------------------------------------
      // 'dup' keeps a copy on the stack; the plain F18A '!' opcode writes T to
      // [A] -- live I/O at whatever port address A currently holds.
      : 'out dup ! ;

      // ----------------------------------------------------------------------
      // 'in  --  reads 18 bit value from port (Stefan's own description)
      // ----------------------------------------------------------------------
      // The plain F18A '@' opcode: reads [A] onto the stack -- live I/O at
      // whatever port address A currently holds.
      : 'in @ ;

      // ----------------------------------------------------------------------
      // 'ldlo  --  moves lo 16 bit from value to R (Stefan's own description;
      // renamed from 'lo@ -- see the revision note above)
      // ----------------------------------------------------------------------
      // 'dup' keeps a copy of the value for the caller; 'r!' (above) stores the
      // duplicate into 507's r. Unlike 'ldhi, no shifting/masking is applied
      // here -- the value's low 16 bits are simply what's moved, with any
      // excess high bits left for a separate 'ldhi transfer to handle.
      : 'ldlo dup r! ;

      // ----------------------------------------------------------------------
      // 'sthi  --  moves 2 bits from R to hi value (Stefan's own description;
      // renamed from 'hi! -- see the revision note above)
      // ----------------------------------------------------------------------
      // 'xffff and' masks whatever is on the stack (an existing low-16-bit
      // value) to 16 bits, clearing any stray high bits; 'r@' fetches 507's r
      // (the 2 bits to insert); 'sl16' shifts those 2 bits up into the high
      // (bits 16-17) position; 'xor' merges them into the masked value's now-
      // clear high bits. Net effect: builds an 18-bit value from an existing
      // low half plus 2 high bits supplied via r.
      : 'sthi xffff and r@ sl16 xor ;

      // ----------------------------------------------------------------------
      // 'stlo  --  moves 16 bits from R to lo value (Stefan's own description;
      // renamed from 'lo! -- see the revision note above)
      // ----------------------------------------------------------------------
      // 'x30000 and' masks whatever is on the stack to JUST its high 2 bits
      // (bits 16-17), clearing the low 16; 'r@' fetches 507's r (the new low-16
      // value); 'xor' merges it into the masked value's now-clear low bits (the
      // same shift-in-via-XOR idiom used throughout this cluster). Net effect:
      // builds an 18-bit value from an existing high half plus a new low 16
      // bits supplied via r -- the complement of 'sthi above.
      : 'stlo x30000 and r@ xor ;

      // ----------------------------------------------------------------------
      // spop ( -w)  --  pop a value relayed from 607's own extended memory, via
      // 507 (inferred, same relay idiom as node 506's/508's own spop)
      // ----------------------------------------------------------------------
      // 's/pop' resolves (via '# 507 import') to 507's own exported word of
      // that name, so this ships {CALL s/pop} to 507 -- 507 in turn relays a
      // further {CALL /pop} up to 607, which pops and returns a word from its
      // own extended-memory area. A second packed word ships {!p} -- what 507
      // itself executes to send that value back down over the port -- and '@b'
      // on 407's side is what actually receives it.
      : spop ( -w) A[ s/pop ]] lit !b A[ !p ]] lit !b @b ;

      // ----------------------------------------------------------------------
      // spush ( w)  --  push a value up the chain to 607's own extended memory,
      // via 507 (inferred, same relay idiom as node 506's/508's own spush)
      // ----------------------------------------------------------------------
      // Ships {@p, CALL s/push} to 507 in one packed word: 507's own '@p'
      // fetches the literal w this word's own trailing '!b' just carried
      // across, then 507 falls into its own exported 's/push', which itself
      // relays {@p, CALL /push} further up to 607 to complete the push.
      : spush ( w) A[ @p s/push ]] lit !b !b ;
      """;
}