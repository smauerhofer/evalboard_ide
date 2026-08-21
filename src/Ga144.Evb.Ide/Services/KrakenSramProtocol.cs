using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Builds the host-side Kraken leaf sequences that puppet a memory-master node
/// (106, 108, or 207) through node 107's AN003 "SRAM Control Cluster Mark 1"
/// protocol (AN003 section 3, "Interface and Usage"). These leaves need no
/// resident program of their own on the master -- exactly like
/// <see cref="KrakenProtocol"/>'s ReadA/WriteA/etc., they are raw, unwrapped
/// ("tentacle(0)") F18 opcode sequences that Kraken relays out to and executes
/// directly on whichever master node route is targeted, the same
/// Transact/WriteRead708 plumbing already used for every other per-node
/// operation. Node 107 (plus the resident node 007/008/009 firmware; see
/// <see cref="SramClusterPrograms"/>) must already be installed and running
/// before any of these are used -- these leaves only speak the wire protocol,
/// they do not install anything.
///
/// AN003's own notation: "-x" means the 18-bit one's-complement inverse of a
/// 16-bit value x. Node 107 uses the sign of each of the first two words it
/// receives through a master's port to decide which of the four operations is
/// being requested (AN003 section 4.1: "checking the signs of the first two
/// [words] as an economical way of decoding which of the four primitive
/// functions is being requested"). <see cref="Invert"/> implements that
/// sign-flip; every method below applies it to exactly the words AN003's
/// section 3 table marks with a leading "-".
///
/// Every leaf ends in exactly one reply word sent back to the host via '!p',
/// matching the requirement (see KrakenProtocol's remarks on BuildWriteMemory)
/// that a tentacle relay's footer always blocks on reading back one word: ex@
/// and cx? echo the genuine SRAM/compare reply; ex! and mk! have no protocol
/// reply of their own (AN003's table leaves their "Reply Sent" column blank),
/// so they echo back the value written, the same acknowledgment convention
/// KrakenProtocol.BuildWriteA/BuildWriteMemory already use.
/// </summary>
internal static class KrakenSramProtocol
{
  /// <summary>
  /// 'ex@ (a p - w)': fetches the 16-bit word at 20-bit address page:address.
  /// Sets B to the master's port toward node 107, writes the protocol's
  /// [+p +a] request, then reads back the [w] reply (AN003 section 3, "ex@").
  /// </summary>
  public static int[] BuildSramReadWord(int masterPortToNode107, int page, int address) =>
  [
    Pack("@p", "b!"), Mask(masterPortToNode107),
    Pack("@p", "!b"), Mask(page),
    Pack("@p", "!b"), Mask(address),
    Pack("@b", "!p")
  ];

  /// <summary>
  /// 'ex! (w a p - )': stores a 16-bit word at 20-bit address page:address.
  /// Writes the protocol's [-p -a w] request (page and address inverted to
  /// identify the write, per AN003 section 3, "ex!"); no reply is defined, so
  /// the written value is echoed back as the required acknowledgment word.
  /// </summary>
  public static int[] BuildSramWriteWord(int masterPortToNode107, int page, int address, int value) =>
  [
    Pack("@p", "b!"), Mask(masterPortToNode107),
    Pack("@p", "!b"), Mask(Invert(page)),
    Pack("@p", "!b"), Mask(Invert(address)),
    Pack("@p", "dup", "!b", "!p"), Mask(value)
  ];

  /// <summary>
  /// 'cx? (w a p n - f)': compares the word at page:address to
  /// <paramref name="compareValue"/>; if equal, stores <paramref name="newValue"/>
  /// there and node 107 returns true (0xFFFF), otherwise memory is untouched
  /// and it returns false (0). Writes the protocol's [-n +p a w] request
  /// (only the compare value is inverted, identifying this as cx? rather than
  /// ex!/ex@; AN003 section 3, "cx?") and reads back the [f] reply.
  /// </summary>
  public static int[] BuildSramCompareExchange(
      int masterPortToNode107, int page, int address, int compareValue, int newValue) =>
  [
    Pack("@p", "b!"), Mask(masterPortToNode107),
    Pack("@p", "!b"), Mask(Invert(compareValue)),
    Pack("@p", "!b"), Mask(page),
    Pack("@p", "!b"), Mask(address),
    Pack("@p", "!b"), Mask(newValue),
    Pack("@b", "!p")
  ];

  /// <summary>
  /// 'mk! (w f -0)': sets or posts node 107's master enable/stimulus mask.
  /// Writes the protocol's [+x -f w] request (AN003 section 3, "mk!") -- an
  /// arbitrary positive marker word, the inverted f flag (0 = replace the
  /// enable mask, 1 = post stimuli), then the mask word itself, echoed back
  /// as the required acknowledgment since mk! defines no reply.
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
  public static int[] BuildSramSetMask(int masterPortToNode107, int mask, bool postStimuli) =>
  [
    Pack("@p", "b!"), Mask(masterPortToNode107),
    Pack("@p", "!b"), Mask(0),
    Pack("@p", "!b"), Mask(Invert(postStimuli ? 1 : 0)),
    Pack("@p", "dup", "!b", "!p"), Mask(mask)
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
