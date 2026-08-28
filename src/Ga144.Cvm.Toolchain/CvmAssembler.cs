using System.Globalization;
using System.Text.RegularExpressions;

namespace Ga144.Cvm.Toolchain;

/// <summary>
/// Assembles CVM assembly language source text into a relocatable <see cref="CvmObjectFile"/>.
///
/// Syntax, in full:
/// <code>
/// ; a line comment (// also works)
/// .section CODE            ; switches which section subsequent lines assemble into (default: CODE)
/// .export main             ; makes a label below visible to other object files/the linker
/// .import someExternal     ; declares a name this file references but does not define
///
/// main:                     ; a label -- may share a line with an instruction, or stand alone
///   nop
///   pushlit 0x1234          ; a literal operand (0x-hex or plain decimal)
///   pushlit loop            ; or a label/import name -- assembles to that symbol's final address
///   pop
///   push
///   call loop               ; call a label/import -- resolves to that symbol's own address
///   call 0x0100             ; or a literal address, 0x0000-0x7FFF only (bit 15 is reserved)
/// loop:
///   nop
///
/// .section DATA
/// table: .word 1, 2, 3      ; raw data words -- each may also be numeric or a label/import name
/// </code>
///
/// All 5 built-in CVM instructions (<see cref="CvmInstructionSet.Instructions"/>) are always
/// available without needing <c>.import</c> -- this assembler never bakes in their numeric opcode
/// (it doesn't know any node's F18 source at all). <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c>
/// each emit their instruction word as a placeholder with a <see cref="CvmRelocationType.CvmOpcode"/>
/// relocation against an external symbol named after the mnemonic, for the linker to resolve against a
/// primitive table exported from the IDE. <c>call</c> is different: its one word directly IS the
/// callee's address (<see cref="CvmInstructionSet.CvmInstructionShape.EncodesAddressDirectly"/>), so it
/// gets a plain <see cref="CvmRelocationType.AbsoluteAddress"/> relocation against its own operand
/// instead -- the same relocation a <c>.word</c> or <c>pushlit</c> label/import operand would get.
///
/// This is a two-pass assembler: pass 1 walks every line purely to compute section layout (every
/// instruction's word length is fixed by its mnemonic alone, so a label's final offset never depends
/// on any operand value) and to validate syntax; pass 2 walks the same lines again to actually emit
/// words and relocations, by which point every label's section-relative offset is already known, so
/// forward references (an instruction near the top of a file referring to a label declared near the
/// bottom, like <c>loop</c> above) resolve correctly.
/// </summary>
public static class CvmAssembler
{
  private const string DefaultSectionName = "CODE";
  private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

  public static (CvmObjectFile? Object, IReadOnlyList<string> Errors) Assemble(string source)
  {
    var errors = new List<string>();
    List<ParsedLine> lines = Parse(source);

    // Pass 1: layout -- section membership, label offsets, and the export/import name sets. No
    // operand identifier is resolved here; only syntax (operand counts, identifier shape) is checked.
    var labelOffsets = new Dictionary<string, (string Section, int Offset)>(StringComparer.Ordinal);
    var exported = new HashSet<string>(StringComparer.Ordinal);
    var imported = new HashSet<string>(StringComparer.Ordinal);
    var sectionCursors = new Dictionary<string, int> { [DefaultSectionName] = 0 };
    string section = DefaultSectionName;

    foreach (ParsedLine line in lines)
    {
      if (line.Label is not null)
      {
        if (!labelOffsets.TryAdd(line.Label, (section, sectionCursors[section])))
        {
          errors.Add($"line {line.LineNumber}: label \"{line.Label}\" is already defined.");
        }
      }

      switch (line.Directive)
      {
        case null:
          break;

        case ".section":
          if (line.Args.Count != 1 || !IdentifierPattern.IsMatch(line.Args[0]))
          {
            errors.Add($"line {line.LineNumber}: \".section\" needs exactly one name, e.g. \".section DATA\".");
            break;
          }

          section = line.Args[0].ToUpperInvariant();
          sectionCursors.TryAdd(section, 0);
          break;

        case ".export":
        case ".import":
          if (line.Args.Count == 0 || line.Args.Any(name => !IdentifierPattern.IsMatch(name)))
          {
            errors.Add($"line {line.LineNumber}: \"{line.Directive}\" needs at least one name.");
            break;
          }

          (line.Directive == ".export" ? exported : imported).UnionWith(line.Args);
          break;

        case ".word":
          if (line.Args.Count == 0)
          {
            errors.Add($"line {line.LineNumber}: \".word\" needs at least one value.");
            break;
          }

          sectionCursors[section] += line.Args.Count;
          break;

        default:
          CvmInstructionSet.CvmInstructionShape? shape = CvmInstructionSet.TryGetShape(line.Directive);
          if (shape is null)
          {
            errors.Add($"line {line.LineNumber}: \"{line.Directive}\" is not a known instruction or directive.");
            break;
          }

          if (shape.HasOperand != (line.Args.Count == 1) || line.Args.Count > 1)
          {
            errors.Add(shape.HasOperand
                ? $"line {line.LineNumber}: \"{shape.Mnemonic}\" requires exactly one operand, e.g. \"{shape.Mnemonic} 0x1234\"."
                : $"line {line.LineNumber}: \"{shape.Mnemonic}\" does not take an operand.");
            break;
          }

          sectionCursors[section] += shape.WordLength;
          break;
      }
    }

    foreach (string name in exported)
    {
      if (!labelOffsets.ContainsKey(name))
      {
        errors.Add($"\".export {name}\" refers to a label that is never defined in this file.");
      }
    }

    foreach (string name in imported)
    {
      if (labelOffsets.ContainsKey(name))
      {
        errors.Add($"\"{name}\" is both a local label and \".import\"ed -- it can only be one.");
      }
    }

    if (errors.Count > 0)
    {
      return (null, errors);
    }

    // Pass 2: emit. Walking the exact same lines in the exact same order reproduces the exact same
    // cursor positions pass 1 computed, so this needs no extra bookkeeping beyond re-running the walk.
    var objectFile = new CvmObjectFile();
    var externalSymbols = new HashSet<string>(StringComparer.Ordinal);
    section = DefaultSectionName;
    objectFile.GetOrAddSection(DefaultSectionName);

    foreach (ParsedLine line in lines)
    {
      switch (line.Directive)
      {
        case null:
        case ".export":
        case ".import":
          break;

        case ".section":
          section = line.Args[0].ToUpperInvariant();
          objectFile.GetOrAddSection(section);
          break;

        case ".word":
          foreach (string arg in line.Args)
          {
            EmitOperandWord(objectFile, section, arg, line.LineNumber, labelOffsets, imported, externalSymbols, errors);
          }

          break;

        default:
          CvmInstructionSet.CvmInstructionShape shape = CvmInstructionSet.TryGetShape(line.Directive)!;
          CvmSection codeSection = objectFile.GetOrAddSection(section);

          if (shape.EncodesAddressDirectly)
          {
            // "call"-style: there is no tag word at all here -- the instruction's one and only word
            // IS the (eventually resolved) target address, exactly the same relocation a ".word"/
            // "pushlit" label or import operand would get (AbsoluteAddress: "write the resolved
            // address into this word as-is"), just narrower -- 15 bits, not 16, since bit 15 must
            // stay clear so a linked program's interpreter can tell a call word apart from a tagged
            // instruction word by that bit alone.
            EmitOperandWord(
                objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors,
                maxValue: CvmInstructionSet.CallAddressMask,
                rangeDescription: "a 15-bit call target (0x0000-0x7FFF -- bit 15 is reserved to tell a call word apart from a tagged instruction word)");
            break;
          }

          int opcodeOffset = codeSection.Words.Count;
          // Self-describing placeholder -- not a bare 0. 0x8000 | shape.Id is stable and unique per
          // mnemonic regardless of which node(s)/opcode-range actually implement it, so a tool that
          // dumps an unlinked object file's raw words (or a linker that hasn't resolved this
          // relocation yet) can still tell which instruction a word was meant to become. The linker
          // still resolves the real opcode via the CvmOpcode relocation below, keyed by SymbolName --
          // this placeholder value itself is never load-bearing for linking, only for readability.
          codeSection.Words.Add(0x8000 | shape.Id);
          externalSymbols.Add(shape.Mnemonic);
          objectFile.Relocations.Add(new CvmRelocation
          {
            SectionName = section,
            WordOffset = opcodeOffset,
            SymbolName = shape.Mnemonic,
            Type = CvmRelocationType.CvmOpcode,
          });

          if (shape.HasOperand)
          {
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
          }

          break;
      }
    }

    if (errors.Count > 0)
    {
      return (null, errors);
    }

    foreach ((string name, (string sectionName, int offset)) in labelOffsets)
    {
      objectFile.Symbols.Add(new CvmSymbol
      {
        Name = name,
        Binding = exported.Contains(name) ? CvmSymbolBinding.Global : CvmSymbolBinding.Local,
        SectionName = sectionName,
        Value = offset,
      });
    }

    foreach (string name in imported.Concat(externalSymbols).Distinct())
    {
      objectFile.Symbols.Add(new CvmSymbol { Name = name, Binding = CvmSymbolBinding.External, SectionName = null, Value = 0 });
    }

    return (objectFile, errors);
  }

  private static void EmitOperandWord(
      CvmObjectFile objectFile,
      string section,
      string operand,
      int lineNumber,
      IReadOnlyDictionary<string, (string Section, int Offset)> labelOffsets,
      ISet<string> imported,
      ISet<string> externalSymbols,
      List<string> errors,
      int maxValue = CvmWordCodec.WordMask,
      string rangeDescription = "a 16-bit CVM word (0..0xFFFF)")
  {
    CvmSection targetSection = objectFile.GetOrAddSection(section);
    int offset = targetSection.Words.Count;

    if (TryParseNumericLiteral(operand, out int literal))
    {
      if ((uint)literal > (uint)maxValue)
      {
        errors.Add($"line {lineNumber}: {operand} does not fit in {rangeDescription}.");
        targetSection.Words.Add(0);
        return;
      }

      targetSection.Words.Add(literal);
      return;
    }

    if (!labelOffsets.ContainsKey(operand) && !imported.Contains(operand))
    {
      errors.Add($"line {lineNumber}: \"{operand}\" is undefined -- declare it with \".import\" or define it as a label first.");
      targetSection.Words.Add(0);
      return;
    }

    if (imported.Contains(operand))
    {
      externalSymbols.Add(operand);
    }

    targetSection.Words.Add(0); // placeholder -- filled in by the linker via the relocation below.
    objectFile.Relocations.Add(new CvmRelocation
    {
      SectionName = section,
      WordOffset = offset,
      SymbolName = operand,
      Type = CvmRelocationType.AbsoluteAddress,
    });
  }

  private static bool TryParseNumericLiteral(string text, out int value)
  {
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    if (text.Length == 0 || text[0] is < '0' or > '9')
    {
      value = 0;
      return false;
    }

    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
  }

  private sealed record ParsedLine(int LineNumber, string? Label, string? Directive, IReadOnlyList<string> Args);

  private static List<ParsedLine> Parse(string source)
  {
    var lines = new List<ParsedLine>();
    string[] rawLines = source.Replace("\r\n", "\n").Split('\n');
    for (int index = 0; index < rawLines.Length; index++)
    {
      int lineNumber = index + 1;
      string content = StripComment(rawLines[index]).Trim();
      if (content.Length == 0)
      {
        continue;
      }

      string? label = null;
      int colon = content.IndexOf(':');
      if (colon >= 0 && IdentifierPattern.IsMatch(content[..colon].Trim()))
      {
        label = content[..colon].Trim();
        content = content[(colon + 1)..].Trim();
      }

      if (content.Length == 0)
      {
        lines.Add(new ParsedLine(lineNumber, label, null, []));
        continue;
      }

      int firstSpace = content.IndexOfAny([' ', '\t']);
      string keyword = firstSpace < 0 ? content : content[..firstSpace];
      string argsText = firstSpace < 0 ? string.Empty : content[(firstSpace + 1)..].Trim();
      List<string> args = argsText.Length == 0
          ? []
          : [.. argsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

      lines.Add(new ParsedLine(lineNumber, label, keyword, args));
    }

    return lines;
  }

  private static string StripComment(string line)
  {
    int semicolon = line.IndexOf(';');
    int slashSlash = line.IndexOf("//", StringComparison.Ordinal);
    int cut = semicolon < 0 ? slashSlash : (slashSlash < 0 ? semicolon : Math.Min(semicolon, slashSlash));
    return cut < 0 ? line : line[..cut];
  }
}