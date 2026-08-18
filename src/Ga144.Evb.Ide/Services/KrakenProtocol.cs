using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Builds the focus/write*/read*/tentacle(n) word sequences that erect and
/// operate a Kraken tentacle through node 708's sett/setn/w/r head protocol
/// (see <see cref="Ga144Node708HeadProtocol"/> and <see cref="KrakenSession"/>).
/// This replaces the old host-carrier-clocked x1/w1/r1/pump mechanism
/// entirely. Every sequence here is still pure F18 port-execution code --
/// a controlled node needs no resident RAM/ROM program of its own to run
/// it -- but the opcodes, wire encoding, and reply-relay shape are new.
///
/// Every "count" literal below follows the same -1 convention used
/// throughout the design this was transcribed from (see 'dec' in node
/// 708's own 'w/r', and the literal "63" in <see cref="BuildReadRam"/>/
/// <see cref="BuildReadRom"/> for a 64-word transfer): the pushed literal
/// is one less than the number of times the following `unext`-terminated
/// loop actually runs, because an F18A for/unext loop is do-while shaped
/// -- the body always runs at least once, then repeats while the
/// decremented counter has not wrapped past zero.
/// </summary>
internal static class KrakenProtocol
{
  // ---- leaf command builders ---------------------------------------------
  // Each of these is a self-contained, unwrapped ("tentacle(0)") sequence:
  // the exact code a node executes once its P is already focused on the
  // right incoming port. Wrap with WrapTentacleHop/BuildTentacle below to
  // relay one through however many already-erected nodes stand between the
  // host and the target.

  /// <summary>
  /// 'focus = A[ @p >r !p ; ], port': jumps the currently focused node's own
  /// P to the given address -- typically a compass port address, which
  /// re-anchors the node to read/write through that port instead of local
  /// RAM, but any 10-bit address works (see <see cref="KrakenSession"/>'s
  /// use of this as the new Jump primitive). The '!p' sends back whatever
  /// was on top of the data stack before this ran -- not meaningful data,
  /// but a genuine 1-word reply every relay wrap needs, at the cost of
  /// destroying the old top of stack (confirmed intentional).
  /// </summary>
  public static int[] BuildFocus(int port) => [Pack("@p", ">r", "!p", ";"), Mask(port)];

  /// <summary>'writeA = A[ @p dup a! !p ]], value': sets A, echoes it back (1 reply word).</summary>
  public static int[] BuildWriteA(int value) => [Pack("@p", "dup", "a!", "!p"), Mask(value)];

  /// <summary>'readA = A[ a !p ]]': sends A back over the port (1 reply word).</summary>
  public static int[] BuildReadA() => [Pack("a", "!p")];

  /// <summary>'writeB = A[ @p dup b! !p ]], value': sets B, echoes it back (1 reply word).</summary>
  public static int[] BuildWriteB(int value) => [Pack("@p", "dup", "b!", "!p"), Mask(value)];

  /// <summary>
  /// 'writeRAM = A[ dup xor a! ]], A[ @p >r ]], 63, A[ @p !+ unext ]], A[ a !p ]]':
  /// writes a full 64-word RAM image starting at address 0, then echoes the
  /// final A (1 reply word).
  /// </summary>
  public static int[] BuildWriteRam(IReadOnlyList<int> words)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count != 64)
    {
      throw new ArgumentException("A GA144 node RAM image contains exactly 64 words.", nameof(words));
    }

    var stream = new List<int>(4 + words.Count + 1)
    {
      Pack("dup", "xor", "a!"),
      Pack("@p", ">r"),
      Mask(63),
      Pack("@p", "!+", "unext")
    };
    foreach (int word in words)
    {
      stream.Add(Mask(word));
    }

    stream.Add(Pack("a", "!p"));
    return [.. stream];
  }

  /// <summary>
  /// 'readRAM = A[ dup xor a! ]], A[ @p >r ]], 63, A[ @+ !p unext ]]':
  /// reads all 64 RAM words back starting at address 0 (64 reply words).
  /// </summary>
  public static int[] BuildReadRam() =>
      [Pack("dup", "xor", "a!"), Pack("@p", ">r"), Mask(63), Pack("@+", "!p", "unext")];

  /// <summary>
  /// 'readROM = A[ @p a! ]], 0x80, A[ @p >r ]], 63, A[ @+ !p unext ]]':
  /// reads all 64 ROM words back starting at address 0x080 (64 reply words).
  /// </summary>
  public static int[] BuildReadRom() =>
      [Pack("@p", "a!"), Mask(0x080), Pack("@p", ">r"), Mask(63), Pack("@+", "!p", "unext")];

  /// <summary>
  /// 'writePStack = A[ @p >r ]], 8, A[ @p unext ]], A[ dup !p ]]': pushes 9
  /// words directly onto the parameter stack (each loop iteration is a
  /// plain '@p', a native push, not a store -- there is no address to set),
  /// then echoes the new top of stack (1 reply word).
  ///
  /// This pushes 9 words while <see cref="BuildReadPStack"/> pops and sends
  /// back 10 -- confirmed intentional, not a mismatch: every leaf must
  /// return at least 1 reply word for the relay wrap to work, so writing 9
  /// real words plus the required echo of the new top of stack is exactly
  /// the given "8"/"9" pair, not an error.
  /// </summary>
  public static int[] BuildWritePStack(IReadOnlyList<int> words)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count != 9)
    {
      throw new ArgumentException(
          "'writePStack' pushes exactly 9 words, per the given 'A[ @p >r ]], 8' loop count.", nameof(words));
    }

    var stream = new List<int>(2 + words.Count + 1)
    {
      Pack("@p", ">r"),
      Mask(8),
      Pack("@p", "unext")
    };
    foreach (int word in words)
    {
      stream.Add(Mask(word));
    }

    stream.Add(Pack("dup", "!p"));
    return [.. stream];
  }

  /// <summary>
  /// 'readPStack = A[ @p >r ]], 9, A[ !p unext ]]': pops and sends back 10
  /// words from the parameter stack (loop count 9 -&gt; 10 iterations, same
  /// -1 convention as everywhere else here).
  /// </summary>
  public static int[] BuildReadPStack() =>
      [Pack("@p", ">r"), Mask(9), Pack("!p", "unext")];

  /// <summary>
  /// 'writeRStack': nine "A[ @p >r ]], rs(k)" pairs (rs(8) down to rs(0),
  /// each consuming one payload word) push the whole return stack, then
  /// 'A[ dup !p ]]' echoes the new top (1 reply word) -- 9*2+1 = 19 words,
  /// matching "size(writeRStack) = 19" from the original formula exactly.
  /// The return stack is 9 words (R plus 8 circular cells), not 10; an
  /// earlier draft of this file read the given "rs(9)..rs(0)" labels as 10
  /// literal values, which was wrong -- confirmed corrected to 9.
  /// <paramref name="rs8ToRs0"/> must be given in that order, rs(8) first.
  /// </summary>
  public static int[] BuildWriteRStack(IReadOnlyList<int> rs8ToRs0)
  {
    ArgumentNullException.ThrowIfNull(rs8ToRs0);
    if (rs8ToRs0.Count != 9)
    {
      throw new ArgumentException(
          "'writeRStack' restores exactly 9 return-stack words (rs(8)..rs(0)).", nameof(rs8ToRs0));
    }

    var stream = new List<int>(9 * 2 + 1);
    foreach (int word in rs8ToRs0)
    {
      stream.Add(Pack("@p", ">r"));
      stream.Add(Mask(word));
    }

    stream.Add(Pack("dup", "!p"));
    return [.. stream];
  }

  /// <summary>
  /// 'readRStack = A[ r> r> r> ]] x3, [ @p >r ]], 8, [ !p unext ]]': moves
  /// the 9 return-stack words onto the data stack (on top of whatever T
  /// already held), then pops and sends back exactly those 9 -- not the
  /// full 10-word 'readPStack' (which would also resend the pre-existing
  /// T). The pop/send tail therefore uses count 8 (9 iterations), not
  /// readPStack's own 9 (10 iterations); it is a sibling of readPStack, not
  /// a reuse of it.
  /// </summary>
  public static int[] BuildReadRStack() =>
      [
        Pack("r>", "r>", "r>"),
        Pack("r>", "r>", "r>"),
        Pack("r>", "r>", "r>"),
        Pack("@p", ">r"),
        Mask(8),
        Pack("!p", "unext")
      ];

  /// <summary>
  /// Not part of the given formula set -- added to preserve arbitrary
  /// single-address memory access (e.g. the IoAddress register), which none
  /// of focus/writeA/writeRAM/etc. cover on their own. Mirrors the old
  /// KrakenProtocol's 'ReadMemoryInstruction' opcode pair exactly (@ + !p),
  /// just packed through the Pack helper below. Reads the word at the
  /// address currently in A, non-incrementing (1 reply word).
  /// </summary>
  public static int[] BuildReadMemory() => [Pack("@", "!p")];

  /// <summary>
  /// See <see cref="BuildReadMemory"/>. Same idea as the old
  /// 'WriteMemoryInstruction' (@p + !): stores at the address currently in
  /// A, non-incrementing -- but, per the same fix applied to 'focus' (see
  /// its remarks), a bare '@p !' produces no reply of its own, and every
  /// leaf must: a parent hop's relay footer always tries to pull exactly
  /// one word back over B, and with nothing ever sent that read blocks
  /// forever. So this dups the value before storing and echoes the copy,
  /// exactly like 'writeA'/'writeB' already do (1 reply word).
  /// </summary>
  public static int[] BuildWriteMemory(int value) => [Pack("@p", "dup", "!", "!p"), Mask(value)];

  // ---- tentacle relay wrapping --------------------------------------------

  /// <summary>
  /// One hop of 'tentacle(n)': wraps <paramref name="inner"/> so the
  /// currently focused node relays it one step further out over its B
  /// port, then relays <paramref name="replyWordCountMinusOne"/> + 1 reply
  /// words back over P. size(n) = size(n-1) + 6, computed here from the
  /// actual array length rather than a hardcoded constant, so any future
  /// mismatch between a hand-counted "size(x)" comment and the real
  /// sequence (as happened with the return stack -- see
  /// <see cref="BuildWriteRStack"/>) cannot propagate into this code.
  /// </summary>
  public static int[] WrapTentacleHop(IReadOnlyList<int> inner, int replyWordCountMinusOne)
  {
    ArgumentNullException.ThrowIfNull(inner);
    if (inner.Count == 0)
    {
      throw new ArgumentException("An inner tentacle sequence must be at least one word.", nameof(inner));
    }

    var stream = new List<int>(inner.Count + 6)
    {
      Pack("@p", ">r"),
      Mask(inner.Count - 1),
      Pack("@p", "!b", "unext")
    };
    stream.AddRange(inner);
    stream.Add(Pack("@p", ">r"));
    stream.Add(Mask(replyWordCountMinusOne));
    stream.Add(Pack("@b", "!p", "unext"));
    return [.. stream];
  }

  /// <summary>
  /// Wraps <paramref name="leaf"/> with <paramref name="hops"/> levels of
  /// <see cref="WrapTentacleHop"/> -- i.e. 'tentacle(hops)' built outward
  /// from 'tentacle(0) = leaf', per the recursive definition given. Used to
  /// reach a node several hops down a tentacle while that node (and every
  /// hop beyond it) has not yet been erected, so no relay code of its own
  /// exists to lean on and the host must supply every level of wrapping
  /// itself. All wrapped hops share the same reply-word count, matching
  /// the given formula's single 'y' used unchanged at every level.
  /// </summary>
  public static int[] BuildTentacle(int hops, IReadOnlyList<int> leaf, int replyWordCountMinusOne)
  {
    ArgumentNullException.ThrowIfNull(leaf);
    if (hops < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(hops));
    }

    int[] stream = [.. leaf];
    for (int hop = 0; hop < hops; hop++)
    {
      stream = WrapTentacleHop(stream, replyWordCountMinusOne);
    }

    return stream;
  }

  private static int Pack(params string[] names)
  {
    var opcodes = new List<byte>(names.Length);
    foreach (string name in names)
    {
      // The compiler's own CompileQuotedInstruction special-cases the token
      // text ";" to the terminator opcode directly rather than a dictionary
      // lookup (the dictionary key is "return"); replicate that mapping
      // here so the ";" in the given 'focus' formula packs correctly.
      string lookup = name == ";" ? "return" : name;
      if (!F18InstructionSet.Opcodes.TryGetValue(lookup, out byte opcode))
      {
        throw new InvalidOperationException($"Unknown F18 opcode '{name}'.");
      }

      opcodes.Add(opcode);
    }

    return F18InstructionSet.EncodePackedInstruction(opcodes);
  }

  private static int Mask(int value) => value & F18InstructionSet.WordMask;
}