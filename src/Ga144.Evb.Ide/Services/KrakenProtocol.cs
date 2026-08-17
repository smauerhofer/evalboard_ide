using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Builds the x1/w1/r1/wr1 streams defined by the Kraken method. The stream
/// itself is pure F18 port-execution code; controlled nodes do not need any RAM
/// or ROM resident Kraken program.
/// </summary>
internal static class KrakenProtocol
{
  private static readonly int PumpPrefix = Pack("@p", ">r");
  private static readonly int PumpBody = Pack("@p", "!b", "unext");
  private static readonly int ReturnHop = Pack("@b", "!p");

  public static int WriteAInstruction { get; } = Pack("@p", "a!");
  public static int ReadAInstruction { get; } = Pack("a", "!p");
  public static int WriteBInstruction { get; } = Pack("@p", "b!");
  public static int ReadMemoryIncrementInstruction { get; } = Pack("@+", "!p");
  public static int WriteMemoryIncrementInstruction { get; } = Pack("@p", "!+");
  public static int ReadMemoryInstruction { get; } = Pack("@", "!p");
  public static int WriteMemoryInstruction { get; } = Pack("@p", "!");
  public static int PopDataInstruction { get; } = Pack("!p");
  public static int PushDataInstruction { get; } = Pack("@p");
  public static int PopReturnInstruction { get; } = Pack("r>", "!p");
  public static int PushReturnInstruction { get; } = Pack("@p", ">r");

  public static IReadOnlyList<int> BuildX1(int position, int instruction) =>
      WrapForward(position, [Mask(instruction)], appendReturnHop: false);

  public static IReadOnlyList<int> BuildW1(int position, int instruction, int value) =>
      WrapForward(position, [Mask(instruction), Mask(value)], appendReturnHop: false);

  /// <summary>
  /// Concatenates <paramref name="values"/> independent w1 requests (same position,
  /// same instruction) into one stream. Kraken writes are fire-and-forget -- no
  /// return hop, no reply capture -- so each w1 block is fully self-contained and
  /// nodes along the tentacle simply keep fetching/relaying straight through from
  /// one block into the next. This lets a whole RAM/ROM image (or stack) be
  /// written in a single transaction instead of one per word, without changing the
  /// per-word wire protocol at all.
  /// </summary>
  public static IReadOnlyList<int> BuildBlockW1(int position, int instruction, IReadOnlyList<int> values)
  {
    var stream = new List<int>(values.Count * 2);
    foreach (int value in values)
    {
      stream.AddRange(BuildW1(position, instruction, value));
    }

    return stream;
  }

  public static IReadOnlyList<int> BuildR1(int position, int instruction) =>
      WrapForward(position, [Mask(instruction)], appendReturnHop: true);

  public static IReadOnlyList<int> BuildWr1(int position, int instruction, int value) =>
      WrapForward(position, [Mask(instruction), Mask(value)], appendReturnHop: true);

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