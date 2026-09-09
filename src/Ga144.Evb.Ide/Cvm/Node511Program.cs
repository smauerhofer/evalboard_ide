namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 511's resident F18 source -- CVM2's 32-register register-file node, supplied verbatim by Stefan
/// on 2026-09-07 alongside node 510 ("here are nodes 510 and 511 ... they support 32 register that can
/// be used for parameter passing to functions"). This is the first node in this project whose own
/// opcodes need BOTH a live compile (to resolve which function is being invoked) AND an embedded
/// per-instance operand (the register index) at once -- see
/// <see cref="CvmInstructionSet.LoadRegisterFileMnemonic"/>'s own remarks for the new
/// <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/> encoding this required.
///
/// <b>REVISED 2026-09-07 -- Stefan confirmed and fixed a typo in <c>r/main</c>: registers are 16-bit,
/// single-word, so doubling the register index before <c>a!</c> (this file's ORIGINAL revision) was
/// wrong.</b> Stefan, verbatim: "there was a typo in node 511. the register are 16-bit so doubling the
/// index is wrong." The fix touches ONLY <c>r/main</c>'s own body -- <c>r/leave</c> and all four ops
/// (<c>'rld</c>/<c>'rst</c>/<c>'rpop</c>/<c>'rpush</c>) are byte-for-byte unchanged from the original
/// paste -- and this resolves what this file's own ORIGINAL revision had flagged as an open question
/// (whether the doubled addressing meant a future 32-bit extension paralleling node 306): it did not: it
/// was simply wrong. This does NOT change the CVM OPCODE WORD FORMAT itself (still register in bits 4-0,
/// function-select in bits 9-5 with a 0x20 offset, exactly as this source's own trailing comment always
/// said) -- so none of <see cref="CvmInstructionSet"/>'s or
/// <see cref="Services.CvmAssemblyLanguage"/>'s own <c>Node511*</c> field-layout constants needed any
/// change; only this node's OWN internal RAM-addressing arithmetic did.
///
/// <b>Reached from node 510's own RIGHT-port relay -- confirmed directly from that node's own <c>x/main</c>
/// cascade.</b> <see cref="Node510Program"/>'s own <c>x/main</c> tests "<c>1011_11??_????_????</c>" and
/// relays it onward via <c>r--- ;</c> (its own RIGHT port). This node's own header, "register file,
/// <c>1011_11??_????_????</c>," is EXACTLY that same bit range, and its own <c># left /b</c> binding uses
/// the SAME local port name ("left") on this side -- matching the same "same name on both sides"
/// precedent already noted for 508&lt;-&gt;509 and 509&lt;-&gt;510 (see <see cref="Node510Program"/>'s
/// own remarks), not independently re-verified against <see cref="Models.KrakenConfiguration"/> this
/// time. CVM2's full chain to here: 507 -&gt; 508 -&gt; 509 -&gt; 510 -&gt; 511, its fourth relay hop off
/// of 507's own CPU.
///
/// <b>Imports node 510.</b> <c># 510 import</c> brings <c>x/r@</c>/<c>x/r!</c>/<c>x/pop</c>/<c>x/push</c>/
/// <c>x/leave</c> into scope by name.
///
/// <b><c>r/main</c> uses a fundamentally DIFFERENT dispatch shape than every other node's own main in this
/// mesh -- direct field extraction, not a bit-by-bit <c>-if</c>/<c>then</c> cascade.</b> Every other
/// relay/dispatch node so far (k/main, ar/main, x/main above, n/main, etc.) narrows down its own dispatch
/// by testing and shifting out ONE bit at a time via a chain of <c>-if</c>/<c>2*</c>/<c>then</c>.
/// <c>r/main</c> instead reads the FULL, UNMODIFIED 16-bit dispatch word once and extracts BOTH of its
/// own two 5-bit fields from it directly via masking and shifting arithmetic -- appropriate for a
/// register file, which needs a full 5-bit register index (not a single bit) to select among 32
/// registers, plus a full 5-bit function selector to pick among <c>'rld</c>/<c>'rst</c>/<c>'rpop</c>/
/// <c>'rpush</c>, not a binary decision tree.
///
/// <b>FLAGGED: no leading <c># r/leave lit &gt;r</c> continuation setup -- unlike every other cascade in
/// this mesh.</b> k/main/ar/main/x/main/n/main all open with "<c>\# &lt;node&gt;/leave lit &gt;r</c>"
/// before their own port reads, pre-loading the return stack with their own "leave" continuation. This
/// source's own <c>r/main</c> has no such opening -- it reads its dispatch word directly
/// (<c>A[ drop !p ]] lit !b @b</c>), and only calls <c>r/leave</c> EXPLICITLY, by name, at the very end
/// of its own body, right before its own closing <c>;</c>. Reproduced exactly as pasted; this is a
/// genuine structural difference from every other node's own main, not a typo (unlike the register-
/// doubling issue fixed 2026-09-07, above), and is called out rather than silently reconciled with the
/// other nodes' shape.
///
/// <b><c>r/main</c>'s own field extraction, derived bit-by-bit directly from its own body (AS FIXED
/// 2026-09-07).</b> <c>A[ drop !p ]] lit !b</c> compiles a block that discards whatever is on the data
/// stack and writes ONCE via port B (unlike the ORIGINAL revision's <c>A[ 2* !p !p ]] lit !b</c>, which
/// wrote via port B twice); the single following <c>@b</c> ("<c>// get opcode</c>") then performs exactly
/// ONE port-B read, leaving the full, untouched 16-bit opcode word W on the data stack -- ONE read, ONE
/// word, no second implicit value left over the way the ORIGINAL two-<c>@b</c> revision did:
/// <code>
/// dup 0x1f and a!
/// 2/ 2/ 2/ 2/ 2/ 0x1f and 0x20 xor ex r/leave ;
/// </code>
/// <list type="bullet">
/// <item><c>dup</c> duplicates W, leaving [W, W]; <c>0x1f and</c> on the top copy isolates bits 4-0 (the
/// REGISTER INDEX, 0-31); <c>a!</c> loads that index DIRECTLY (no <c>2*</c> doubling -- registers are
/// single 16-bit words, one per index, not 2-word slots -- see below) into the F18 pointer register
/// <c>a</c>, consuming the top copy and leaving the ORIGINAL W underneath.</item>
/// <item>Five successive <c>2/</c> (arithmetic right shift) on the remaining W shift bits 15-5 down into
/// bits 10-0, discarding bits 4-0 (the register field) entirely; <c>0x1f and</c> on THAT result then
/// isolates what were originally bits 9-5 of W -- the FUNCTION-SELECT field, 0-31 -- with every higher
/// (tag) bit already masked away by the same step, needing no separate tag-stripping.</item>
/// <item><c>0x20 xor</c>: since a 5-bit value's own bit 5 is always 0, XOR-ing in <c>0x20</c> is exactly
/// the same as adding it -- turning the raw function-select field into an actual RAM address, 0x20-0x3F.
/// This is EXACTLY what this source's own trailing comment states outright: "the address of the function
/// is encoded in the next 5 bits with an offset of 0x20." This field's own bit position and offset are
/// UNCHANGED by the 2026-09-07 fix -- only the register-field arithmetic above changed.</item>
/// <item><c>ex</c> calls (not jumps to) whichever of <c>'rld</c>/<c>'rst</c>/<c>'rpop</c>/<c>'rpush</c>
/// actually compiles to that address; once it returns, <c>r/leave</c> runs (the relay-chain
/// acknowledgment, called explicitly here rather than via the usual pre-loaded-return-stack idiom -- see
/// the FLAGGED remark above), and the outer <c>;</c> ends <c>r/main</c> itself.</item>
/// </list>
/// <c># 0x20 org</c> (below) is not a coincidence given this: it reserves precisely the 32 words (0x20-
/// 0x3F) this 5-bit function-select field can address, starting node 511's own compiled code exactly
/// where the field's own range begins.
///
/// <b>32 single-word (16-bit) register slots -- CONFIRMED 2026-09-07, the doubled-addressing shape this
/// file's ORIGINAL revision carried was a genuine typo, now fixed.</b> The ORIGINAL revision doubled the
/// register index (<c>2*</c>) before loading it into <c>a</c>, the SAME "2 words per slot" addressing
/// scheme <see cref="Node306Program"/>'s own <c>ar/reg</c> uses for its genuinely 32-bit (address+page
/// word-pair) registers -- which this file's own original remarks flagged as suspicious, since none of
/// <c>'rld</c>/<c>'rst</c>/<c>'rpop</c>/<c>'rpush</c> ever touched a second word at <c>a+1</c>, and 32
/// doubled slots (64 words) would have consumed this node's entire local RAM budget before even
/// <c># 0x20 org</c>'s own 32-word code reservation. Stefan confirmed this directly: "the register are
/// 16-bit so doubling the index is wrong." The fixed <c>r/main</c> (above) loads the register index into
/// <c>a</c> UNDOUBLED, so each of the 32 registers now occupies exactly ONE word (32 words total for the
/// register bank, comfortably alongside <c># 0x20 org</c>'s own 32-word code reservation) -- resolving
/// the earlier open question outright rather than leaving it for a future 32-bit extension.
///
/// <b>The four register-file ops, each reached by direct jump to its own compiled address (no shared
/// dispatch body):</b>
/// <list type="bullet">
/// <item><c>'rld</c> (load register): <c>A[ x/r@ ]] lit !b A[ !p ]] lit !b @b ! ;</c> -- relays a word IN
/// from the caller (<c>x/r@</c>, via node 510) and stores it (<c>!</c>) at the register slot <c>a</c>
/// already points at.</item>
/// <item><c>'rst</c> (store register): <c>A[ @p x/r! ]] lit !b @ !b ;</c> -- reads the register slot
/// (<c>@</c>) and relays it OUT to the caller (<c>x/r!</c>).</item>
/// <item><c>'rpop</c>: <c>A[ x/pop ]] lit !b A[ !p ]] lit !b @b ! ;</c> -- same shape as <c>'rld</c>, via
/// <c>x/pop</c> instead of <c>x/r@</c> (the caller's own STACK, not its register, per the trailing
/// comment's own "pop stack to reg[a]").</item>
/// <item><c>'rpush</c>: <c>A[ @p x/push ]] lit !b @ !b ;</c> -- same shape as <c>'rst</c>, via
/// <c>x/push</c> instead of <c>x/r!</c> ("push reg[a] to stack").</item>
/// </list>
/// Each of these is a plain, single-purpose leaf, self-contained and structurally simple -- unlike node
/// 306's six ops (which share bit-tested branches within one cascade), node 511's four each get their own
/// fully independent compiled address, exactly what the "jump directly to the resolved function address"
/// dispatch shape above requires.
///
/// <b>Not verified against a live compile.</b> No compiler is available in this environment to confirm
/// <c>r/main</c> or the four register ops actually compile as pasted, or that they land inside the
/// 0x20-0x3F window <see cref="CvmInstructionSet.Node511FunctionFieldBitMask"/> can represent -- the
/// exact numeric addresses (and therefore this node's own CVM opcode words) are resolved DYNAMICALLY
/// against a live compile by <see cref="Services.CvmAssemblyLanguage.BuildEncodeTable"/>/
/// <see cref="Services.CvmAssemblyLanguage.BuildDecodeTable"/>, never hand-derived or hardcoded here --
/// see those methods' own remarks. Reproduced verbatim, not adjusted to compile.
///
/// <b>An UNRELATED, still-unimplemented "register file" is separately mentioned in node 407/408's own
/// dispatch history, at a DIFFERENT address range (0xF800-0xFFFF) -- not this one.</b> See
/// <see cref="Services.CvmAssemblyLanguage.Node408BinaryComparisonTagBits"/>'s own remarks. Purely a
/// naming coincidence between two still-separate parts of this project's own history; this node's own
/// range is 0xBC00-0xBFFF, confirmed above from its own header and node 510's own relay.
/// </summary>
internal static class Node511Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 32-register register-file node.</summary>
  public const int Coordinate = 511;

  /// <summary>
  /// Node 511's full resident F18 source. See the class remarks for the four register-file ops and the
  /// register-index typo Stefan fixed on 2026-09-07.
  /// <b>RE-SYNCED 2026-09-09</b> against Stefan's own <c>workspace.yaml</c> project export as part of the
  /// opcode/assembler-vs-node reconciliation audit -- REVERSED: the PRIOR "SYNCED, 2026-09-08" copy here
  /// had drifted to a more advanced, 4-way bit-cascade dispatch (<c>[ 0x40 -25 + ] org</c>, splitting
  /// <c>1011_111?</c>/<c>1011_110?</c> into individual load/store/pop/push branches one bit at a time).
  /// The export restores the ORIGINAL, simpler direct-field-extraction dispatch documented in the class
  /// remarks above: <c># 0x20 org</c>, a single <c>r/main</c> that masks the full opcode word's own low 5
  /// bits into the register index (<c>a!</c>) and its next 5 bits (offset by 0x20) into a jump target
  /// (<c>ex</c>), landing directly on whichever of <c>'rld</c>/<c>'rst</c>/<c>'rpop</c>/<c>'rpush</c> the
  /// function-select field named -- each independently resolved
  /// (<see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>, register index in the
  /// low 5 bits of every one of these four opcodes' own CVM word) rather than reached through nested bit
  /// tests. This un-does whatever intermediate revision produced the split-cascade shape; workspace.yaml
  /// is ground truth per Stefan's own explicit ruling, so <c>'rld</c>/<c>'rst</c>/<c>'rpop</c>/<c>'rpush</c>
  /// remain live, real, currently-defined opcodes on this node -- NOT retired.
  /// </summary>
  public const string Source = """
      ( CVM2 node 511. register file, 1011_11??_????_???? )
      ( 32 register )
      # 510 import
      # 0x20  org
      entry r/main
      # 0 /a
      # left /b

      : r/leave A[ x/leave ; ]] lit !b
      : r/main  A[ drop !p ]] lit !b

        @b // get opcode
        // set register in a
        dup 0x1f and a!
        // call word
        2/ 2/ 2/ 2/ 2/ 0x1f and 0x20 xor ex r/leave ;

        // load register
      : 'rld A[ x/r@ ]] lit !b A[ !p ]] lit !b @b ! ;
        // store register
      : 'rst A[ @p x/r! ]] lit !b @ !b ;
      : 'rpop A[ x/pop ]] lit !b A[ !p ]] lit !b @b ! ;
      : 'rpush A[ @p x/push ]] lit !b @ !b ;

      (
      this node supports 32 16-bit register.
      register are encoded in the lower 5 bits of the opcode.
      the address of the function is encoded in the next 5 bits with an offset of 0x20, so address 0x20 to 0x3f can be encoded.
      opcode rld   1011_11??_???a_aaaa move r to reg[a]
      opcode rst   1011_11??_???a_aaaa move reg[a] to r
      opcode rpop  1011_11??_???a_aaaa pop stack to reg[a]
      opcode rpush 1011_11??_???a_aaaa push reg[a] to stack
      )
      """;
}