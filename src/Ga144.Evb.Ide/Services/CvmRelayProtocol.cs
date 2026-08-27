using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Self-contained fire-and-forget relay-wrapper primitives for installing the CVM test
/// cluster's compiled programs across its branching tree (708 -&gt; 707 -&gt; 607 -&gt; {507 -&gt; {407,
/// 506, 508}, 606, 608}), reusing the EXACT technique <see cref="LegacyKrakenProtocol"/> /
/// <see cref="KrakenSession.ErectOnto"/> already proved on real hardware for Kraken's own
/// (flat/linear) tentacles: host-precomputed, nested "pump" wrapping (<c>@p &gt;r</c> / count /
/// <c>@p !b unext</c>) so a single boot frame sent to node 708's ROM relays through however many
/// already-focused intermediate nodes stand between it and the target, landing a bare,
/// NO-REPLY payload once it arrives. Per the project's node-300-erection-investigation notes,
/// the fire-and-forget/no-reply-during-erection shape is the one PROVEN on real hardware --
/// this class deliberately does not introduce any new relay-construction mechanism, only a
/// tree-shaped (rather than flat-tentacle) generalization of the same one.
///
/// Deliberately a fresh, self-contained copy rather than a reuse of
/// <see cref="LegacyKrakenProtocol"/> itself: that class is Kraken's own load-bearing erection
/// code, and this project's convention for hardware routines is to keep each one self-contained
/// (copy the proven pattern, don't share plumbing across unrelated features) so a future change
/// to one can never destabilize the other.
///
/// <b>Why every payload here is reply-free, unlike <see cref="KrakenProtocol"/>'s leaf builders.</b>
/// <see cref="KrakenProtocol"/>'s own BuildWriteA/BuildWriteRam/BuildJump/etc. all end by sending
/// an echo word back over the port (<c>!p</c>) -- correct for Kraken's ONLINE, already-erected
/// request/reply transactions, where the host is standing by to read that echo immediately. This
/// class's payloads run during ONE-SHOT INSTALLATION, fire-and-forget, exactly like
/// <see cref="KrakenSession.ErectOnto"/>'s own per-node focus/writeB frames: nothing ever reads
/// any reply. An F18 port write (<c>!p</c>) is a synchronous hardware rendezvous -- it blocks
/// until a matching read happens on the other end. If a leaf here sent a reply word that nothing
/// ever reads, the target node would stall forever on that single instruction and never reach the
/// jump that follows it, silently bricking that node's boot for the rest of the session. Every
/// builder below is checked, word for word, against DB013 6.1.2.3's own literal "Post-Load
/// Initializations" forms (Set IO / Set A / Set B / Push S), all of which are themselves
/// reply-free -- and the "jump" leaf reuses the SAME bare slot-0 control-transfer opcode
/// (<c>F18InstructionSet.EncodeSlot0Control(0x02, address)</c>) that <see cref="KrakenSession.ErectOnto"/>
/// already uses for its own reply-free "focus" instruction, not <see cref="KrakenProtocol.BuildJump"/>.
/// </summary>
internal static class CvmRelayProtocol
{
  private static readonly int PumpPrefix = Pack("@p", ">r");
  private static readonly int PumpBody = Pack("@p", "!b", "unext");

  /// <summary>The GA144 IO register's memory-mapped address, per DB013 6.1.2.3's own "Set IO" example and KrakenSession's identical constant.</summary>
  public const int IoAddress = 0x15D;

  /// <summary>
  /// Bare slot-0 jump, no reply -- moves the currently focused/executing node's P directly to
  /// <paramref name="address"/>. The same shape <see cref="KrakenSession.ErectOnto"/>'s own
  /// "focus" instruction uses; reused here both to focus a relay node onto its incoming port and,
  /// at the end of a leaf's own program load, to jump it into its real compiled entry point.
  /// </summary>
  public static int BuildBareJump(int address) => F18InstructionSet.EncodeSlot0Control(0x02, address);

  /// <summary>'writeB = A[ @p b! ]], port': points the currently focused/executing node's B at <paramref name="port"/> -- no reply. Used to (re)point a relay node at whichever child is about to load.</summary>
  public static int[] BuildWriteBNoReply(int port) => [Pack("@p", "b!"), Mask(port)];

  /// <summary>
  /// Writes a full 64-word RAM image starting at address 0 -- no reply. Unlike
  /// <see cref="KrakenProtocol.BuildWriteRam"/> (which echoes the final A back for the ONLINE
  /// request/reply model), this omits the trailing echo entirely -- see this class's own remarks
  /// on why an unread reply would deadlock the target node during a fire-and-forget install.
  /// </summary>
  public static int[] BuildWriteRamNoReply(IReadOnlyList<int> words)
  {
    ArgumentNullException.ThrowIfNull(words);
    if (words.Count != 64)
    {
      throw new ArgumentException("A GA144 node RAM image contains exactly 64 words.", nameof(words));
    }

    var stream = new List<int>(4 + words.Count)
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

    return [.. stream];
  }

  /// <summary>'Set A' per DB013 6.1.2.3 (04AB2 @p a! / value) -- no reply.</summary>
  public static int[] BuildSetA(int value) => [Pack("@p", "a!"), Mask(value)];

  /// <summary>
  /// 'Set B' per DB013 6.1.2.3 (04BB2 @p b! / value) -- no reply. Same shape as
  /// <see cref="BuildWriteBNoReply"/>; kept as a separately named entry point so a leaf's
  /// post-load register initialization reads the same way DB013 itself lists it, distinct from
  /// the tree-relay "writeB" use above.
  /// </summary>
  public static int[] BuildSetB(int value) => [Pack("@p", "b!"), Mask(value)];

  /// <summary>
  /// 'Set IO' per DB013 6.1.2.3 (04BB2 @p b! / 0015D / 05BB2 @p !b / value) -- points B at the IO
  /// register, then writes value there. Leaves B pointed at IO afterward, exactly matching
  /// DB013's own literal word sequence; a later <see cref="BuildSetB"/> call, if the descriptor
  /// also specifies an explicit InitialB, repoints B afterward.
  /// </summary>
  public static int[] BuildSetIo(int value) => [Pack("@p", "b!"), Mask(IoAddress), Pack("@p", "!b"), Mask(value)];

  /// <summary>'Push S' per DB013 6.1.2.3 (049B2 @p / value) -- pushes one value directly onto the data stack, no reply.</summary>
  public static int[] BuildPushS(int value) => [Pack("@p"), Mask(value)];

  /// <summary>
  /// Wraps <paramref name="leaf"/> with <paramref name="position"/> levels of pump relay --
  /// verbatim the technique in <see cref="LegacyKrakenProtocol.BuildX1"/>/<c>WrapForward</c>, with
  /// no return hop (nothing here ever expects a reply): position 0 sends <paramref name="leaf"/>
  /// completely unwrapped (the target is directly wired to node 708's own boot-frame-forwarding
  /// port); position N wraps it through N already-focused-and-wired intermediate nodes.
  /// </summary>
  public static IReadOnlyList<int> WrapForward(int position, IReadOnlyList<int> leaf)
  {
    if (position < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(position));
    }

    var stream = new List<int>(leaf);
    for (int hop = 0; hop < position; hop++)
    {
      int forwardCountMinusOne = stream.Count - 1;
      var wrapped = new List<int>(stream.Count + 3)
      {
        PumpPrefix,
        Mask(forwardCountMinusOne),
        PumpBody
      };
      wrapped.AddRange(stream);
      stream = wrapped;
    }

    return stream;
  }

  private static int Pack(params string[] names)
  {
    var opcodes = new List<byte>(names.Length);
    foreach (string name in names)
    {
      if (!F18InstructionSet.Opcodes.TryGetValue(name, out byte opcode))
      {
        throw new InvalidOperationException($"Unknown F18 opcode '{name}'.");
      }

      opcodes.Add(opcode);
    }

    return F18InstructionSet.EncodePackedInstruction(opcodes);
  }

  private static int Mask(int value) => value & F18InstructionSet.WordMask;
}
