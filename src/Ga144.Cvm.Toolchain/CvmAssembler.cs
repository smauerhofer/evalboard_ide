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
///   ret                     ; return -- pops the address a call pushed and jumps back to it
///   br -3                   ; branch by a literal signed offset, -0x400..0x3FF (11 bits)
///   br loop                 ; or by a label defined in this same file's same section
///   cbr 5                   ; conditional branch, offset -0x200..0x1FF (10 bits) -- branches if r == 0
///   slit -100                ; load a literal signed value into R, -0x800..0x7FF (12 bits)
///   enter 3                 ; node 606: enter stack frame, reserve 3 locals -- unsigned, 0x00..0xFF
///   ldp 1                   ; node 606: load parameter at frame-relative offset 1
///   stl 0                   ; node 606: store to local at frame-relative offset 0
///   adjust 2                ; node 606: adjust stack frame by an unsigned 0x00..0xFF amount
///
/// .section DATA
/// table: .word 1, 2, 3      ; raw data words -- each may also be numeric or a label/import name
/// </code>
///
/// All built-in CVM instructions (<see cref="CvmInstructionSet.Instructions"/>) are always
/// available without needing <c>.import</c> -- this assembler never bakes in a real numeric opcode for
/// any of them. Three different operand encodings are in play (<see cref="CvmInstructionSet.CvmOperandEncoding"/>):
/// <c>nop</c>/<c>pushlit</c>/<c>push</c>/<c>pop</c>/<c>ret</c> emit their instruction word as a
/// placeholder with a <see cref="CvmRelocationType.CvmOpcode"/> relocation against an external symbol
/// named after the mnemonic, for the linker to resolve against a primitive table exported from the
/// IDE (it doesn't know any node's F18 source at all). <c>call</c> is different: its one word directly
/// IS the callee's address (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>), so it
/// gets a plain <see cref="CvmRelocationType.AbsoluteAddress"/> relocation against its own operand
/// instead -- the same relocation a <c>.word</c> or <c>pushlit</c> label/import operand would get.
/// <c>br</c>/<c>cbr</c>/<c>slit</c> are different again
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/>): each one's word is a
/// fixed tag OR'd with a signed value, known completely at assemble time -- no relocation, no node.
/// <c>br</c> packs an 11-bit offset and <c>cbr</c> (renamed 2026-09-09 from the old, unconfirmed "ifbr"
/// placeholder -- see <see cref="CvmInstructionSet.ConditionalBranchTag"/>'s own remarks for Stefan's
/// exact, hardware-confirmed bit pattern) packs a 10-bit offset -- the two no longer share a field width
/// -- (what an offset is relative to is no longer an open question either: confirmed against real
/// hardware, the address of the word right after the branch's own opcode word, plus the offset) and
/// both accept EITHER a literal offset OR a label defined earlier in the same file's same section,
/// resolved to that same relative-offset computation (see <see cref="EmitEmbeddedSignedValue"/>'s own
/// remarks for the exact formula, which reads its field width straight off each shape's own
/// <c>ValueBitMask</c> rather than assuming br's and cbr's are equal); <c>slit</c> packs a wider 12-bit
/// value with a narrower tag, accepts only a literal (it isn't an address computation at all -- per
/// Stefan, it loads its value directly into the F18 interpreter's own R register). Node 606's eight
/// frame-pointer ops (<c>enter</c>, <c>adjust</c>, <c>stl</c>, <c>stp</c>, <c>ldl</c>, <c>ldp</c>,
/// <c>lal</c>, <c>lap</c>) are shaped the same way as br/cbr/slit -- a fixed tag OR'd with a literal
/// value, no relocation, no node -- except each packs an UNSIGNED 8-bit value
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>, emitted by
/// <see cref="EmitEmbeddedUnsignedValue"/>), never a signed one. Node 306's six address-register ops
/// (<c>ldar</c>, <c>star</c>, <c>inca</c>, <c>deca</c>, <c>lda</c>, <c>sta</c>, added 2026-09-06) are the
/// same EmbeddedUnsignedValue shape too, just with a genuinely narrower 2-bit register-index operand
/// that isn't at bit 0 upward -- see <see cref="EmitEmbeddedUnsignedValue"/>'s own remarks on
/// <c>ValueBitShift</c> -- and a FLAGGED collision with <c>slit</c>'s own tag range (see
/// <see cref="CvmInstructionSet.LoadAddressRegisterMnemonic"/>'s own remarks): assembling any of the six
/// by name is unaffected by that collision, only disassembling an already-assembled word is ambiguous.
/// Node 511's four register-file ops (<c>rld</c>, <c>rst</c>, <c>rpop</c>, <c>rpush</c>, added
/// 2026-09-07, <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>) are NOT YET
/// SUPPORTED by this assembler at all -- they need both a live-node-resolved base AND an embedded
/// operand at once, which this assembler's relocation-based resolution of tagged mnemonics has no way to
/// express (see that encoding's own remarks); a line using one of the four fails outright with a clear
/// error rather than silently dropping the register operand. Only
/// <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>'s own immediately-resolving assembler
/// supports them today.
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

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress)
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

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue)
          {
            // "br"/"cbr"/"slit"-style: also no separate tag word -- the instruction's one and only
            // word is shape.Tag (its own fixed high bits) OR'd with a signed value packed into
            // shape.ValueBitMask's low bits (11 bits for br, 10 for cbr -- renamed and re-confirmed
            // 2026-09-09, one bit narrower than br's, see ConditionalBranchTag's own remarks -- 12 for
            // slit -- EmitEmbeddedSignedValue reads the width straight off the shape, so it needs no
            // per-mnemonic special-casing here or there). Fully self-describing from a literal operand
            // alone, so unlike the tagged mnemonics below this needs no placeholder/relocation/external
            // symbol at all. br/cbr ALSO accept a label operand now (see EmitEmbeddedSignedValue's own
            // remarks for the relative-offset computation) -- slit does not, since it isn't an address
            // computation at all.
            bool supportsRelativeLabel = shape.Mnemonic is CvmInstructionSet.BranchMnemonic or CvmInstructionSet.ConditionalBranchMnemonic;
            EmitEmbeddedSignedValue(codeSection, shape, line.Args[0], line.LineNumber, codeSection.Words.Count, supportsRelativeLabel, labelOffsets, section, imported, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue)
          {
            // Node 606's eight ops: also no separate tag word -- shape.Tag OR'd with an UNSIGNED value
            // packed into shape.ValueBitMask's low bits (8 bits for all eight of them). Also fully
            // self-describing, so also no placeholder/relocation/external symbol.
            EmitEmbeddedUnsignedValue(codeSection, shape, line.Args[0], line.LineNumber, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
          {
            // Node 511's four register-file ops (rld/rst/rpop/rpush) need BOTH a live-node-resolved base
            // (which function -- exactly what the generic tagged path below already provides via its own
            // CvmOpcode relocation) AND a user-supplied embedded register operand packed into the SAME
            // word -- something CvmRelocationType.CvmOpcode has no way to express: it resolves an entire
            // word from a single symbol name at link time, with no room for a second, per-instance
            // operand value. Falling through to the generic tagged path below would silently DROP the
            // register operand (it only emits a trailing operand word for CvmOperandEncoding.TrailingWord,
            // which this isn't) -- so this errors out loudly instead of doing that. Only
            // Ga144.Evb.Ide.Services.CvmAssemblyLanguage's own immediately-resolving assembler (the CVM
            // Debugger's own Assembly Code editor) supports these four mnemonics today; revisit here once
            // a real linker exists and CvmRelocationType grows a shape that can carry a second operand.
            errors.Add($"line {line.LineNumber}: \"{shape.Mnemonic}\" is not yet supported by this assembler -- use the CVM Debugger's own Assembly Code editor instead.");
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

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.TrailingWord)
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

  /// <summary>
  /// Emits a <c>br</c>/<c>cbr</c>/<c>slit</c> word: <paramref name="shape"/>.Tag OR'd with a signed
  /// literal value packed into <paramref name="shape"/>.ValueBitMask's low bits -- reading the field
  /// width straight off the shape (11 bits for br, 10 for cbr -- renamed and re-confirmed 2026-09-09,
  /// see ConditionalBranchTag's own remarks -- 12 for slit) is what lets one method serve every
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/> mnemonic without a
  /// per-mnemonic branch here; adding a fourth one someday needs no change to this method at all, only
  /// a new <see cref="CvmInstructionSet.Instructions"/> entry with its own tag and mask.
  ///
  /// <paramref name="supportsRelativeLabel"/> (true only for br/cbr -- <c>slit</c> isn't an address
  /// computation at all, so a label operand there wouldn't mean anything) additionally accepts a label
  /// defined earlier in pass 1, exactly the mechanical computation this method's own remarks used to
  /// flag as "known but not yet written": what the offset is relative to is confirmed against real
  /// hardware to be the address right after the branch's own opcode word, so the packed value is simply
  /// <c>targetLabelOffset - (thisInstructionOffset + 1)</c>, where <paramref name="thisInstructionOffset"/>
  /// is this word's own section-relative offset (the caller passes <c>codeSection.Words.Count</c>
  /// before adding this word, mirroring how every other operand kind already gets its offset from pass
  /// 1's per-line cursor). The label must be defined in the SAME section as the branch itself -- a
  /// relative offset across sections isn't meaningful -- and an <c>.import</c>ed name is rejected for
  /// the same reason a cross-file relative offset isn't computable at assemble time (unlike
  /// <c>call</c>'s absolute-address relocation, there is no <see cref="CvmRelocationType"/> for "this
  /// many words from here, resolved at link time"). A non-numeric, unknown, or out-of-range operand is
  /// always a hard error, never a silently truncated or zero-filled word.
  /// </summary>
  private static void EmitEmbeddedSignedValue(
      CvmSection targetSection,
      CvmInstructionSet.CvmInstructionShape shape,
      string operand,
      int lineNumber,
      int thisInstructionOffset,
      bool supportsRelativeLabel,
      IReadOnlyDictionary<string, (string Section, int Offset)> labelOffsets,
      string section,
      ISet<string> imported,
      List<string> errors)
  {
    int valueBitMask = shape.ValueBitMask;
    int maxValue = valueBitMask >> 1;
    int minValue = -(maxValue + 1);
    int bitWidth = System.Numerics.BitOperations.PopCount((uint)valueBitMask);

    int value;
    if (TryParseSignedNumericLiteral(operand, out value))
    {
      // fall through to the range check below.
    }
    else if (supportsRelativeLabel && labelOffsets.TryGetValue(operand, out (string Section, int Offset) target))
    {
      if (target.Section != section)
      {
        errors.Add($"line {lineNumber}: \"{shape.Mnemonic} {operand}\" cannot branch across sections (\"{operand}\" is in \".section {target.Section}\", this instruction is in \".section {section}\").");
        targetSection.Words.Add(shape.Tag);
        return;
      }

      value = target.Offset - (thisInstructionOffset + 1);
    }
    else if (supportsRelativeLabel && imported.Contains(operand))
    {
      errors.Add($"line {lineNumber}: \"{shape.Mnemonic}\" cannot branch to \".import\"ed \"{operand}\" -- its offset from here is not known until link time.");
      targetSection.Words.Add(shape.Tag);
      return;
    }
    else
    {
      string operandKinds = supportsRelativeLabel ? "a literal signed value or a label defined in this file" : "a literal signed value";
      errors.Add($"line {lineNumber}: \"{operand}\" is not {operandKinds} -- \"{shape.Mnemonic}\" does not support anything else as an operand.");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    if (value < minValue || value > maxValue)
    {
      errors.Add($"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s signed {bitWidth}-bit value ({minValue}..{maxValue}).");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    targetSection.Words.Add(shape.Tag | (value & valueBitMask));
  }

  /// <summary>
  /// Emits an <c>enter</c>/<c>adjust</c>/<c>stl</c>/<c>stp</c>/<c>ldl</c>/<c>ldp</c>/<c>lal</c>/<c>lap</c>
  /// (or, since 2026-09-06, node 306's <c>ldar</c>/<c>star</c>/<c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c>)
  /// word: <paramref name="shape"/>.Tag OR'd with an UNSIGNED literal value, left-shifted by
  /// <paramref name="shape"/>.ValueBitShift, packed into <paramref name="shape"/>.ValueBitMask's bits.
  /// This mirrors <see cref="EmitEmbeddedSignedValue"/> exactly except for the range check and parse:
  /// every one of these mnemonics' own table entry gives an unsigned range, never a signed one, so there
  /// is no negative half to accept and <see cref="TryParseNumericLiteral"/> (not
  /// <see cref="TryParseSignedNumericLiteral"/>) is the right parser. ValueBitShift is 0 for node 606's
  /// eight ops (the operand packs straight into ValueBitMask's bits, unchanged since this method was
  /// first written) and 1 for node 306's six (their 2-bit register-index operand sits at bits 2-1, not
  /// bit 0 upward -- see <see cref="CvmInstructionSet.CvmInstructionShape.ValueBitShift"/>'s own
  /// remarks), so the legal input range is <c>0..(ValueBitMask &gt;&gt; ValueBitShift)</c>, not
  /// <c>0..ValueBitMask</c>, whenever a shift is in play. Like <see cref="EmitEmbeddedSignedValue"/>,
  /// this does NOT (yet) accept a label or import operand, and a non-numeric or out-of-range literal is
  /// a hard error, never silently truncated or zero-filled.
  /// </summary>
  private static void EmitEmbeddedUnsignedValue(
      CvmSection targetSection,
      CvmInstructionSet.CvmInstructionShape shape,
      string operand,
      int lineNumber,
      List<string> errors)
  {
    int valueBitMask = shape.ValueBitMask;
    int shift = shape.ValueBitShift;
    int maxValue = valueBitMask >> shift;
    int bitWidth = System.Numerics.BitOperations.PopCount((uint)valueBitMask);

    if (!TryParseNumericLiteral(operand, out int value))
    {
      errors.Add($"line {lineNumber}: \"{operand}\" is not a literal unsigned value -- \"{shape.Mnemonic}\" does not (yet) support a label/import operand.");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    if (value < 0 || value > maxValue)
    {
      errors.Add($"line {lineNumber}: {value} does not fit in \"{shape.Mnemonic}\"'s unsigned {bitWidth - shift}-bit value (0..{maxValue}).");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    targetSection.Words.Add(shape.Tag | ((value << shift) & valueBitMask));
  }

  /// <summary>Like <see cref="TryParseNumericLiteral"/>, but also accepts a leading '-' for a negative decimal or hex magnitude.</summary>
  private static bool TryParseSignedNumericLiteral(string text, out int value)
  {
    if (text.StartsWith('-') && TryParseNumericLiteral(text[1..], out int magnitude))
    {
      value = -magnitude;
      return true;
    }

    return TryParseNumericLiteral(text, out value);
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