using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Builds the host-side Kraken leaf sequences that puppet a memory-master node
/// (106, 108, or 207) into calling one of ITS OWN resident subroutines (see
/// <see cref="SramClusterPrograms.BuildMasterSupportSource"/>) -- the four
/// real AN003 primitives (ex@/ex!/cx?/mk!) below, plus <see cref="BuildEchoTest"/>,
/// a diagnostic-only leaf that isn't part of AN003 at all. All of these are
/// installed once by <see cref="SramClusterInstaller"/> and never started
/// standalone -- the master node stays a puppet target the whole time (its P
/// register is only ever loaded with a resident RAM image via
/// <c>KrakenLiveController.WriteRamAsync</c>; <c>JumpAsync</c> is never called
/// on it, so it never leaves the tentacle the way 007/008/009/107 do once
/// their own resident programs are started).
///
/// Every leaf here follows the same three-part shape: push this operation's
/// arguments onto the master's own parameter stack via '@p' (all of them
/// packed into a SINGLE instruction word -- up to four '@p' opcodes fit in
/// one word's four slots, each one fetching the literal immediately
/// following it and auto-incrementing P to the next, so N arguments cost one
/// instruction word plus N literal words, not 2N words -- pushed in the
/// EXACT REVERSE of AN003's own wire send order; see the remarks on each
/// Build* method below, and on
/// <see cref="SramClusterPrograms.BuildMasterSupportSource"/>, for why that
/// lets the resident subroutine just fire off a straight, unreordered
/// sequence of '!b' writes), inject one raw F18 'call' word addressed at the
/// target subroutine, then read back whatever the subroutine left on the
/// stack via '!p'. A real 'call' is safe to inject directly into the live
/// online puppet stream here (unlike a value fetched with '@p', which would
/// need P to still be a RAM/ROM address to make sense as a return target): P
/// does not advance when it holds a port address rather than a RAM/ROM
/// address, so 'call' pushes the still-valid, unmodified port address as its
/// return address, and the resident subroutine's own compiler-emitted closing
/// ';' (return) pops that same address straight back into P -- puppet mode
/// resumes automatically at the next leaf word, with no manual save/restore
/// needed.
///
/// AN003's own notation: "-x" means the 18-bit one's-complement inverse of a
/// 16-bit value x. Node 107 uses the sign of each of the first two words it
/// receives through a master's port to decide which of the four operations is
/// being requested (AN003 section 4.1: "checking the signs of the first two
/// [words] as an economical way of decoding which of the four primitive
/// functions is being requested"). <see cref="Invert"/> implements that
/// sign-flip; every method below applies it, HOST-SIDE, to exactly the
/// arguments AN003's section 3 table marks with a leading "-" -- the resident
/// subroutines themselves never invert anything, they just relay whatever
/// they are handed, strictly in the order they receive it.
///
/// Every leaf ends in exactly one reply word sent back to the host via '!p',
/// matching the requirement (see KrakenProtocol's remarks on BuildWriteMemory)
/// that a tentacle relay's footer always blocks on reading back one word: ex@
/// and cx? echo the genuine SRAM/compare reply (the resident subroutine's own
/// '@b'); ex! and mk! have no protocol reply of their own (AN003's table
/// leaves their "Reply Sent" column blank), so their subroutines 'dup' the
/// value before sending it and leave the duplicate on the stack -- which is
/// then echoed back as the required acknowledgment, the same convention
/// KrakenProtocol.BuildWriteA/BuildWriteMemory already use.
/// </summary>
internal static class KrakenSramProtocol
{
  /// <summary>
  /// 'ex@ (a p - w)': fetches the 16-bit word at 20-bit address page:address,
  /// via the master's resident 'sram-read' subroutine (( addr page -- w )).
  /// AN003's wire order is [+p +a]; pushed here in reverse -- address, then
  /// page -- so 'sram-read' can send page first, address second, with two
  /// plain '!b's and no reordering of its own.
  /// </summary>
  public static int[] BuildSramReadWord(int subroutineAddress, int page, int address) =>
  [
    Pack("@p", "@p"), Mask(address), Mask(page),
    F18InstructionSet.EncodeSlot0Control(0x03, subroutineAddress),
    Pack("!p")
  ];

  /// <summary>
  /// 'ex! (w a p - )': stores a 16-bit word at 20-bit address page:address,
  /// via the master's resident 'sram-write' subroutine (( value addr page --
  /// value )). AN003's wire order is [-p -a w] (page and address inverted,
  /// identifying this as a write); pushed here in reverse -- value, then the
  /// already-inverted address, then the already-inverted page -- so
  /// 'sram-write' can send page, then address, then a duplicated value, with
  /// no reordering or inversion of its own.
  /// </summary>
  public static int[] BuildSramWriteWord(int subroutineAddress, int page, int address, int value) =>
  [
    Pack("@p", "@p", "@p"), Mask(value), Mask(Invert(address)), Mask(Invert(page)),
    F18InstructionSet.EncodeSlot0Control(0x03, subroutineAddress),
    Pack("!p")
  ];

  /// <summary>
  /// 'cx? (w a p n - f)': compares the word at page:address to
  /// <paramref name="compareValue"/>; if equal, stores <paramref name="newValue"/>
  /// there and node 107 returns true (0xFFFF), otherwise memory is untouched
  /// and it returns false (0). Via the master's resident 'sram-cx' subroutine
  /// (( w a p n -- f )). AN003's wire order is [-n +p a w] (only the compare
  /// value inverted, identifying this as cx? rather than ex!/ex@); pushed
  /// here in reverse -- new value, address, page, then the already-inverted
  /// compare value -- so 'sram-cx' can send n, p, a, w in that order with
  /// four plain '!b's, then read back f with one '@b'.
  /// </summary>
  public static int[] BuildSramCompareExchange(
      int subroutineAddress, int page, int address, int compareValue, int newValue) =>
  [
    Pack("@p", "@p", "@p", "@p"), Mask(newValue), Mask(address), Mask(page), Mask(Invert(compareValue)),
    F18InstructionSet.EncodeSlot0Control(0x03, subroutineAddress),
    Pack("!p")
  ];

  /// <summary>
  /// 'mk! (w f -0)': sets or posts node 107's master enable/stimulus mask,
  /// via the master's resident 'sram-mask' subroutine (( m f x -- m )).
  /// AN003's wire order is [+x -f w] (an arbitrary positive marker word, the
  /// inverted f flag, then the mask word itself); pushed here in reverse --
  /// mask, the already-inverted flag, then the marker -- so 'sram-mask' can
  /// send x, f, then a duplicated mask, with no reordering of its own.
  ///
  /// IMPORTANT: this project installs AN003 section 6.3's degenerate,
  /// single-fixed-master version of node 107 (see the remarks on
  /// <see cref="SramClusterPrograms.BuildNode107Source"/>), which recognises
  /// and consumes an mk! request on the wire for protocol compatibility but
  /// does not act on it -- there is exactly one master per installed cluster,
  /// wired in at install time, so there is nothing left to enable/disable or
  /// post a stimulus for. This leaf is provided so the SRAM Tentacle window's
  /// mask panel round-trips cleanly against real hardware rather than
  /// desyncing node 107's command parser; it is not a functioning mask.
  /// </summary>
  public static int[] BuildSramSetMask(int subroutineAddress, int mask, bool postStimuli) =>
  [
    Pack("@p", "@p", "@p"), Mask(mask), Mask(Invert(postStimuli ? 1 : 0)), Mask(0),
    F18InstructionSet.EncodeSlot0Control(0x03, subroutineAddress),
    Pack("!p")
  ];

  /// <summary>
  /// DIAGNOSTIC ONLY -- not part of AN003, and not a memory operation of any
  /// kind. Calls the master's own resident 'echo' subroutine (( n -- n+1 ),
  /// see <see cref="SramClusterPrograms.BuildMasterSupportSource"/>), which
  /// never touches B or node 107 at all. This exercises exactly the same
  /// '@p'-push / 'call' / '!p'-read-back mechanism every SRAM leaf above
  /// uses, in isolation from the AN003 handshake with 107 -- useful to tell
  /// apart "Kraken's push/call/return to the master node itself is broken"
  /// from "the master's own call/return works fine, but the handshake with
  /// 107 is what's failing". A correct round trip returns
  /// <paramref name="value"/> + 1 (masked to 18 bits); anything else, or a
  /// timeout, points at the call/return plumbing rather than AN003.
  /// </summary>
  public static int[] BuildEchoTest(int subroutineAddress, int value) =>
  [
    Pack("@p"), Mask(value),
    F18InstructionSet.EncodeSlot0Control(0x03, subroutineAddress),
    Pack("!p")
  ];

  /// <summary>The 18-bit one's-complement inverse AN003 calls "-x" throughout section 3 (matches the F18 'inv' opcode's own effect).</summary>
  private static int Invert(int value) => (~value) & F18InstructionSet.WordMask;

  private static int Pack(params string[] names)
  {
    var opcodes = new List<byte>(names.Length);
    foreach (string name in names)
    {
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