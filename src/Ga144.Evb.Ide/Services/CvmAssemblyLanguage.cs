using System.Globalization;
using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// The CVM's own small assembly language: Stefan's mnemonics (<c>nop</c>, <c>pushlit &lt;data&gt;</c>,
/// <c>push</c>, <c>pop</c>, <c>ret</c>) layered on top of the wire-level opcode convention
/// (opcode = 0x8000 | wordAddress) that <see cref="CvmMemoryProtocol"/> already established. (<c>call</c>,
/// <c>br</c>, <c>ifbr</c>, and <c>slit</c> are the exceptions -- see this class's own remarks on why
/// they aren't part of this tagged-opcode layer.)
///
/// This is deliberately a SEPARATE naming layer from node 607's own F18 source symbols
/// ('nop, 'plit, 'pop, 'push, still defined in <see cref="CvmMemoryProtocol"/>) -- those tick-names
/// are node 607's own interpreter labels and won't change; the mnemonics here are what a person
/// reads and writes, and the two are free to diverge (as pushlit already has from 'plit).
///
/// The mnemonic/word-length/operand-arity SHAPE of each instruction now lives in the standalone
/// Ga144.Cvm.Toolchain project's <see cref="CvmInstructionSet"/> (shared with the freestanding
/// gaasm/galib/galink command-line tools, so both sides of the toolchain agree on what the
/// instruction set even is); this file's own job is pairing each of those shapes with node 607's F18
/// symbol, which only makes sense against a live IDE compile and has no business in that shared,
/// IDE-independent project.
///
/// Shapes whose <see cref="CvmInstructionSet.CvmInstructionShape.Encoding"/> is anything other than
/// <see cref="CvmInstructionSet.CvmOperandEncoding.None"/>/<see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/>
/// are deliberately left out of that pairing: <c>call</c>
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>) and <c>br</c>/<c>ifbr</c>/
/// <c>slit</c> (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>) have no F18
/// symbol at all to resolve, since none of their opcode words are a tagged dispatch to a named
/// primitive routine -- each one's whole word is fully determined by its own operand alone. Because of
/// that, none of them need a live compile to recognize: <see cref="CvmDebugSession.DisassemblePage0"/>
/// checks for them directly via <see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/> BEFORE ever
/// consulting this file's own symbol-driven decode table, so they already show up correctly in the
/// memory inspector. What's still separate, later work is the OTHER direction -- assembling hand-typed
/// CVM asm source that uses <c>call</c>/<c>br</c>/<c>ifbr</c>/<c>slit</c> via this file's own
/// <see cref="Assemble"/>/<see cref="ParseSource"/> -- and extending this file to the other 6 primitive
/// nodes for the tagged mnemonics; until then this file's own <see cref="Instructions"/> simply omits
/// all four, since they would have nothing to pair them with.
///
/// Both directions -- <see cref="BuildDecodeTable"/> for disassembly and <see cref="BuildEncodeTable"/>/
/// <see cref="Assemble"/> for assembly -- are built from the single <see cref="Instructions"/> table,
/// so they can never drift apart: adding a new TAGGED opcode to <see cref="CvmInstructionSet"/> plus
/// one line here (the F18 symbol it resolves to) is the only change either direction needs.
/// </summary>
internal static class CvmAssemblyLanguage
{
  public const string NopMnemonic = CvmInstructionSet.NopMnemonic;
  public const string PushLitMnemonic = CvmInstructionSet.PushLitMnemonic;
  public const string PushMnemonic = CvmInstructionSet.PushMnemonic;
  public const string PopMnemonic = CvmInstructionSet.PopMnemonic;
  public const string RetMnemonic = CvmInstructionSet.RetMnemonic;

  // Node 607's F18 symbol for each shared-toolchain mnemonic. Every mnemonic in
  // CvmInstructionSet.Instructions must have an entry here, or BuildDecodeTable/BuildEncodeTable
  // simply won't find it in a live compile -- this is the one place that link, kept as a small,
  // easy-to-audit map rather than folded back into the shared table (which has no notion of "F18
  // symbol" at all, on purpose: gaasm never needs one).
  private static readonly IReadOnlyDictionary<string, string> F18SymbolByMnemonic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
  {
    [NopMnemonic] = CvmMemoryProtocol.NopSymbolName,
    [PushLitMnemonic] = CvmMemoryProtocol.PlitSymbolName,
    [PushMnemonic] = CvmMemoryProtocol.PushSymbolName,
    [PopMnemonic] = CvmMemoryProtocol.PopSymbolName,
    [RetMnemonic] = CvmMemoryProtocol.RetSymbolName,
  };

  /// <summary>
  /// Every known CVM asm mnemonic THAT RESOLVES TO A NODE 607 F18 SYMBOL, that symbol, and how many
  /// words (its own opcode word included) it occupies once assembled. <c>pushlit</c> is the only such
  /// instruction with a trailing operand word today -- extend <see cref="CvmInstructionSet"/> plus
  /// <see cref="F18SymbolByMnemonic"/> as more tagged-dispatch opcodes are defined; nothing else in
  /// this file needs to change. A shape whose
  /// <see cref="CvmInstructionSet.CvmInstructionShape.Encoding"/> is
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/> (<c>call</c>) or
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/> (<c>br</c>, <c>ifbr</c>,
  /// <c>slit</c>) has no F18 symbol by design and is filtered out here rather than added to
  /// <see cref="F18SymbolByMnemonic"/> -- see this class's own remarks for why.
  /// </summary>
  public static readonly IReadOnlyList<(string Mnemonic, string SymbolName, int WordLength, bool HasOperand)> Instructions =
      [.. CvmInstructionSet.Instructions
          .Where(shape => shape.Encoding is CvmInstructionSet.CvmOperandEncoding.None or CvmInstructionSet.CvmOperandEncoding.TrailingWord)
          .Select(shape => (shape.Mnemonic, F18SymbolByMnemonic[shape.Mnemonic], shape.WordLength, shape.HasOperand))];

  /// <summary>One parsed line of CVM assembly: a mnemonic plus its operand, when required (pushlit only, today).</summary>
  public sealed record CvmAsmInstruction(string Mnemonic, int? Operand);

  /// <summary>
  /// Resolves <see cref="Instructions"/> against THIS run's own node 607 compile (never a frozen
  /// reference copy -- every address can move as the source evolves) and returns the decode
  /// direction: a map from a word's actual wire/memory VALUE to its mnemonic and word length, for
  /// <see cref="CvmDebugSession.DisassemblePage0"/> to consume. An instruction whose F18 symbol
  /// isn't defined in the current source is simply omitted.
  /// </summary>
  public static IReadOnlyDictionary<int, (string Mnemonic, int WordLength)> BuildDecodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<int, (string, int)>();
    if (!compiledRam.TryGetValue(CvmMemoryProtocol.NopSourceNodeCoordinate, out F18CompileResult? compile))
    {
      return table;
    }

    foreach ((string mnemonic, string symbolName, int wordLength, _) in Instructions)
    {
      if (compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        // A CVM opcode is a 16-bit CVM word (CvmWordCodec.WordMask), not the wider 18-bit F18 wire
        // word the symbol's own address happens to be stored as.
        int opcode = 0x8000 | (symbol.Value & CvmWordCodec.WordMask);
        table[opcode] = (mnemonic, wordLength);
      }
    }

    return table;
  }

  /// <summary>
  /// The encode direction, for <see cref="Assemble"/>: resolves <see cref="Instructions"/> against
  /// THIS run's own node 607 compile and returns a map from mnemonic (case-insensitive) to its
  /// opcode word, word length, and whether it takes an operand.
  /// </summary>
  public static IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> BuildEncodeTable(
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    var table = new Dictionary<string, (int, int, bool)>(StringComparer.OrdinalIgnoreCase);
    if (!compiledRam.TryGetValue(CvmMemoryProtocol.NopSourceNodeCoordinate, out F18CompileResult? compile))
    {
      return table;
    }

    foreach ((string mnemonic, string symbolName, int wordLength, bool hasOperand) in Instructions)
    {
      if (compile.Symbols.TryGetValue(symbolName, out F18ExportedSymbol? symbol))
      {
        // A CVM opcode is a 16-bit CVM word (CvmWordCodec.WordMask), not the wider 18-bit F18 wire
        // word the symbol's own address happens to be stored as.
        int opcode = 0x8000 | (symbol.Value & CvmWordCodec.WordMask);
        table[mnemonic] = (opcode, wordLength, hasOperand);
      }
    }

    return table;
  }

  /// <summary>
  /// Assembles a sequence of CVM asm instructions into opcode/operand words, resolving each
  /// mnemonic against THIS run's own node 607 compile via <see cref="BuildEncodeTable"/>. Returns a
  /// null word list with a 1-based-line error message (never throws) when a mnemonic isn't
  /// recognized (or node 607's current source doesn't define its symbol), an operand is missing
  /// where one is required, or one is supplied where none is allowed -- ready to eventually replace
  /// <see cref="CvmMemoryProtocol.TryBuildTestProgram"/>'s hardcoded instruction sequence with a
  /// program someone actually wrote.
  /// </summary>
  public static (List<int>? Words, string? Error) Assemble(
      IReadOnlyList<CvmAsmInstruction> instructions,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    IReadOnlyDictionary<string, (int Opcode, int WordLength, bool HasOperand)> encodeTable = BuildEncodeTable(compiledRam);
    var words = new List<int>();
    for (int line = 0; line < instructions.Count; line++)
    {
      CvmAsmInstruction instruction = instructions[line];
      if (!encodeTable.TryGetValue(instruction.Mnemonic, out (int Opcode, int WordLength, bool HasOperand) entry))
      {
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" is not a known CVM asm mnemonic, or node {CvmMemoryProtocol.NopSourceNodeCoordinate:000}'s current compile doesn't define its symbol.");
      }

      if (entry.HasOperand && instruction.Operand is null)
      {
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" requires an operand, e.g. \"{instruction.Mnemonic} 0x1234\".");
      }

      if (!entry.HasOperand && instruction.Operand is not null)
      {
        return (null, $"line {line + 1}: \"{instruction.Mnemonic}\" does not take an operand.");
      }

      words.Add(entry.Opcode);
      if (entry.HasOperand)
      {
        words.Add(instruction.Operand!.Value & CvmWordCodec.WordMask);
      }
    }

    return (words, null);
  }

  /// <summary>
  /// Parses CVM assembly source text into <see cref="CvmAsmInstruction"/>s ready for
  /// <see cref="Assemble"/>: one mnemonic per line, optionally followed by a "0x"-prefixed hex or
  /// plain decimal operand; blank lines and ";" or "//" line comments are ignored. This is purely
  /// textual -- it does not know or care whether a mnemonic actually resolves against node 607's
  /// current compile, that check happens in <see cref="Assemble"/>.
  /// </summary>
  public static (List<CvmAsmInstruction>? Instructions, string? Error) ParseSource(string source)
  {
    var instructions = new List<CvmAsmInstruction>();
    string[] lines = source.Replace("\r\n", "\n").Split('\n');
    for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
    {
      string original = lines[lineNumber];
      string line = StripComment(original).Trim();
      if (line.Length == 0)
      {
        continue;
      }

      string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 1)
      {
        instructions.Add(new CvmAsmInstruction(parts[0], null));
        continue;
      }

      if (parts.Length == 2 && TryParseOperand(parts[1], out int operand))
      {
        instructions.Add(new CvmAsmInstruction(parts[0], operand));
        continue;
      }

      return (null, $"line {lineNumber + 1}: could not parse \"{original.Trim()}\".");
    }

    return (instructions, null);
  }

  private static string StripComment(string line)
  {
    int semicolon = line.IndexOf(';');
    int slashSlash = line.IndexOf("//", StringComparison.Ordinal);
    int cut = semicolon < 0 ? slashSlash : (slashSlash < 0 ? semicolon : Math.Min(semicolon, slashSlash));
    return cut < 0 ? line : line[..cut];
  }

  private static bool TryParseOperand(string text, out int value)
  {
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
  }
}