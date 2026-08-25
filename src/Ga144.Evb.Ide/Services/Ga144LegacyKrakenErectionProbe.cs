using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Verbatim port of the old KrakenProtocol.cs's x1/w1/r1 stream builders --
/// the host-precomputed relay-wrapper opcodes used by old-method erection and
/// old-method reads. Distinct from the CURRENT KrakenProtocol
/// (BuildFocus/BuildWriteB). Used by <c>KrakenSession.ErectOnto</c>'s real,
/// production erection path (the fire-and-forget, host-precomputed focus/writeB
/// technique that fixed the node-300 erection bug -- see the project's
/// node-300-erection-investigation notes) -- this is load-bearing code, not a
/// diagnostic probe, even though it originated alongside one. The one-shot
/// "Node 708 legacy erection test" probe that used to live in this file
/// (Ga144LegacyKrakenErectionProbe, plus its Node708LegacyKrakenReport) has
/// been removed now that the node-300 root cause is confirmed and fixed; this
/// class stayed behind because KrakenSession still depends on it.
/// </summary>
internal static class LegacyKrakenProtocol
{
  private static readonly int PumpPrefix = Pack("@p", ">r");
  private static readonly int PumpBody = Pack("@p", "!b", "unext");
  private static readonly int ReturnHop = Pack("@b", "!p");

  public static int WriteBInstruction { get; } = Pack("@p", "b!");
  public static int ReadAInstruction { get; } = Pack("a", "!p");

  public static IReadOnlyList<int> BuildX1(int position, int instruction) =>
      WrapForward(position, [Mask(instruction)], appendReturnHop: false);

  public static IReadOnlyList<int> BuildW1(int position, int instruction, int value) =>
      WrapForward(position, [Mask(instruction), Mask(value)], appendReturnHop: false);

  public static IReadOnlyList<int> BuildR1(int position, int instruction) =>
      WrapForward(position, [Mask(instruction)], appendReturnHop: true);

  private static IReadOnlyList<int> WrapForward(int position, IReadOnlyList<int> leaf, bool appendReturnHop)
  {
    if (position < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(position));
    }

    var stream = new List<int>(leaf);
    for (int hop = 0; hop < position; hop++)
    {
      int forwardCountMinusOne = stream.Count - 1;
      var wrapped = new List<int>(stream.Count + (appendReturnHop ? 4 : 3))
            {
                PumpPrefix,
                Mask(forwardCountMinusOne),
                PumpBody
            };
      wrapped.AddRange(stream);
      if (appendReturnHop)
      {
        wrapped.Add(ReturnHop);
      }

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