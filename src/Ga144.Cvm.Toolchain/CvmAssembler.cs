using System.Globalization;
using System.Text.RegularExpressions;

namespace Ga144.Cvm.Toolchain;

/// <summary>
/// Assembles CVM assembly language source text into a relocatable <see cref="CvmObjectFile"/>.
///
/// <b>2026-09-30: NEW VIRTUAL MACHINE, full reset -- READ THIS FIRST, before the syntax example just
/// below.</b> Per Stefan directly: "there is a new virtual machine. all opcodes are invalid. except that
/// 'nop' has the opcode '0'." Every mnemonic the syntax example below shows (<c>pushlit</c>, <c>push</c>,
/// <c>pop</c>, <c>call</c>, <c>ret</c>, <c>br</c>, <c>cbr</c>, <c>lit</c>, <c>enter</c>, <c>ldp</c>,
/// <c>stl</c>, and everything else <see cref="CvmInstructionSet.Instructions"/> ever listed) is RETIRED
/// as of this reset -- see that list's own remarks at its own top. <c>nop</c> is the one survivor, and its
/// own SHAPE changed too: it is no longer a tagged, node-resolved mnemonic needing a
/// <see cref="CvmRelocationType.CvmOpcode"/> relocation the way this whole doc comment describes below
/// (and the way it always used to be, alongside push/pop/ret) -- it is now
/// <see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcode"/>: a fixed, universally-known literal
/// opcode word, 0x0000, known the moment the mnemonic is, with no live node, no linker, and no relocation
/// involved at all (see that encoding's own remarks). This assembler now emits it directly
/// (<c>shape.Tag</c>, unconditionally) rather than routing it through the generic tagged-mnemonic path.
/// Everything else in this doc comment -- the syntax example, and every paragraph describing a specific
/// retired mnemonic's own encoding -- is kept for historical reference (per this project's own "do not
/// remove any opcodes" convention) but no longer describes a live, assemblable program: assembling
/// anything other than a bare <c>nop</c> today fails to resolve, since <see cref="CvmInstructionSet.TryGetShape"/>
/// no longer recognizes any other mnemonic.
///
/// Syntax, in full (HISTORICAL -- see the reset notice just above):
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
///   lit -100                ; node 509: load a literal signed value into R, -0x200..0x1FF (10 bits) --
///                           ; replaces the retired "slit" (2026-09-09), which used to be a wider,
///                           ; self-describing 12-bit form of this same idea
///   enter 3                 ; node 606: enter stack frame, reserve 3 locals -- unsigned, 0x00..0xFF
///   ldp 1                   ; node 606: load parameter at frame-relative offset 1
///   stl 0                   ; node 606: store to local at frame-relative offset 0
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
/// <c>br</c>/<c>cbr</c>/<c>lit</c> are different again
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
/// <c>ValueBitMask</c> rather than assuming br's and cbr's are equal); <c>lit</c> (node 509's own
/// literal-load form, which absorbed the retired, now-deleted <c>slit</c>'s role 2026-09-09 -- see
/// <see cref="CvmInstructionSet"/>'s own remarks on the 2026-09-09 CVM1-opcode purge) packs a narrower
/// 10-bit value with its own tag, accepts only a literal (it isn't an address computation at all -- per
/// Stefan, it loads its value directly into the F18 interpreter's own R register). Node 606's eight
/// frame-pointer ops (<c>enter</c>, <c>stl</c>, <c>stp</c>, <c>ldl</c>, <c>ldp</c>, plus
/// the now-retired <c>lal</c>/<c>lap</c>/<c>adjust</c>) are shaped the same way as br/cbr/lit -- a fixed tag OR'd with a literal
/// value, no relocation, no node -- except each packs an UNSIGNED 8-bit value
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue"/>, emitted by
/// <see cref="EmitEmbeddedUnsignedValue"/>), never a signed one.
///
/// <b>STALE, CORRECTED 2026-09-09 (this same paragraph used to describe node 306's OLD family as if
/// still current):</b> node 306's original self-describing <c>ldar</c>/<c>star</c>/<c>inca</c>/<c>deca</c>
/// (added 2026-09-06, EmbeddedUnsignedValue, a genuinely narrower 2-bit register-index operand not at
/// bit 0 upward -- see <see cref="EmitEmbeddedUnsignedValue"/>'s own remarks on <c>ValueBitShift</c>)
/// were RETIRED the same day node 306's own source was rewritten around a different, tick-prefixed,
/// NODE-RESOLVED family instead (<c>arinc</c>/<c>ardec</c>/<c>arld</c>/<c>arst</c>/<c>lda</c>/<c>sta</c>,
/// <see cref="CvmInstructionSet.CvmOperandEncoding.None"/>, exactly like <c>nop</c>/<c>push</c>/<c>pop</c>
/// above -- a live compile of node 306, not a literal operand, resolves each one's real word). The OLD
/// family's own mnemonic and tag constants were later deleted outright too (2026-09-09, CVM1-opcode
/// purge -- see <see cref="CvmInstructionSet"/>'s own remarks), so this assembler has never needed to
/// know about node 306's address-register mechanism at all beyond treating <c>arinc</c>/<c>ardec</c>/
/// <c>arld</c>/<c>arst</c>/<c>lda</c>/<c>sta</c> as ordinary external symbols, same as every other
/// tagged/node-resolved mnemonic.
/// Node 511's four register-file ops (<c>rld</c>, <c>rst</c>, <c>rpop</c>, <c>rpush</c>, added
/// 2026-09-07, <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>) were NEVER
/// SUPPORTED by this assembler -- they needed both a live-node-resolved base AND an embedded operand at
/// once, which this assembler's relocation-based resolution of tagged mnemonics has no way to express
/// (see that encoding's own remarks) -- and RETIRED 2026-09-09 (opcode/assembler-vs-node reconciliation
/// audit) after node 511's own re-synced source stopped naming them as separate F18 symbols at all (see
/// <see cref="CvmInstructionSet.LoadRegisterFileMnemonic"/>'s own remarks).
///
/// <b>CORRECTED 2026-09-11:</b> this assembler now DOES support <see cref="CvmOperandEncoding.NodeResolvedEmbeddedValue"/>
/// mnemonics, once node 306's six address-register ops (<c>arinc</c>/<c>ardec</c>/<c>arld</c>/<c>arst</c>/
/// <c>lda</c>/<c>sta</c>) were re-tasked onto this exact shape (per Stefan's own follow-up, "there is
/// more than 1 address register. change the assembler to reflect that" -- see
/// <see cref="CvmInstructionSet.ArithmeticStoreAddressRegisterMnemonic"/>'s own remarks). The missing
/// piece the paragraph above described -- "no way to express a second, per-instance operand value" on a
/// relocation -- is now solved by <see cref="CvmRelocation.EmbeddedValue"/>: the register-index operand
/// is a literal the assembler already knows in full at assemble time (unlike the symbol's own resolved
/// address, which the linker only learns later), so it needs no relocation of its own at all -- it rides
/// along on the SAME <see cref="CvmRelocationType.CvmOpcode"/> relocation the generic tagged-mnemonic
/// path already emits for the live-resolved base word, and the linker simply OR's the two together (see
/// <see cref="Ga144.Cvm.Toolchain.CvmLinker"/>'s own remarks on its <c>CvmOpcode</c> case). Node 511's
/// own rld/rst/rpop/rpush remain retired (no live F18 symbol backs them today), so they don't exercise
/// this path, but nothing about this fix is node-306-specific: any future NodeResolvedEmbeddedValue
/// mnemonic assembles here the same way, automatically, via <see cref="CvmInstructionSet.CvmInstructionShape.ValueBitMask"/>
/// alone.
///
/// This is a two-pass assembler: pass 1 walks every line purely to compute section layout (every
/// instruction's word length is fixed by its mnemonic alone, so a label's final offset never depends
/// on any operand value) and to validate syntax; pass 2 walks the same lines again to actually emit
/// words and relocations, by which point every label's section-relative offset is already known, so
/// forward references (an instruction near the top of a file referring to a label declared near the
/// bottom, like <c>loop</c> above) resolve correctly.
///
/// <b>ADDED 2026-09-30 (same day as the reset), the new VM's own "call" family:</b> <c>scall</c>
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress"/>, generalized off
/// <see cref="CvmInstructionSet.ShortCallAddressMask"/>) and <c>lcall</c>
/// (<see cref="CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord"/>, a new encoding added
/// specifically for it) are real <see cref="CvmInstructionSet.Instructions"/> entries; <c>call</c> itself
/// is a pure pseudo-mnemonic, exactly like <c>literal</c> above, that lowers to whichever of the two fits
/// (see <see cref="CvmInstructionSet.ShortCallMnemonic"/>'s own class-level remarks for the full
/// derivation and the "a label/import operand always means lcall" restriction this class's own two-pass
/// layout forces on it).
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

        case "literal":
          // "literal" (2026-09-27, per Stefan: "change opcode 'literal' so that it uses 'lit' when the
          // constant fits and 'litr' if the constant is too big for 'lit'") -- a pure assembler-level
          // pseudo-mnemonic, not a real CvmInstructionSet.Instructions entry, so it needs its own case
          // here rather than falling into the generic tagged-mnemonic default below. Unlike every other
          // mnemonic in THIS pass (word length fixed by mnemonic alone, per this class's own remarks),
          // "literal"'s word count depends on the operand's VALUE -- 1 word ("lit") if it fits, 2
          // ("litr"'s tag word plus its own trailing operand word) otherwise -- so the value must be
          // known here, in pass 1, same as ".word"'s own already-variable-length case just above. Only a
          // plain numeric literal is supported, same restriction as "lit" itself (see that mnemonic's
          // own remarks): a label's real address isn't resolved until a separate, later link step this
          // assembler doesn't run itself, so there would be no way to know here which of "lit"/"litr" a
          // label operand needs.
          if (line.Args.Count != 1)
          {
            errors.Add($"line {line.LineNumber}: \"literal\" requires exactly one operand, e.g. \"literal 1234\".");
            break;
          }

          if (!TryParseSignedNumericLiteral(line.Args[0], out int literalPass1Value))
          {
            errors.Add($"line {line.LineNumber}: \"literal\" does not support a label operand -- its word count depends on the value, which must be known at assemble time; supply a literal number instead, or use \"litr\" directly for a label's own address.");
            break;
          }

          // ADDED 2026-09-30: "lit"/"litr" were both retired in the new VM's reset (see
          // CvmInstructionSet.Instructions' own remarks at the top of its list) -- "literal" has nothing
          // left to lower to. Checked here, in pass 1, before FitsLitRange even runs (see that method's
          // own guard, which returns false rather than crashing for the same reason) so this fails with
          // one clear, specific message instead of reaching pass 2's own litr-fallback crash.
          if (CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic) is null)
          {
            errors.Add($"line {line.LineNumber}: \"literal\" is not supported -- \"lit\"/\"litr\" were both retired in the new VM's 2026-09-30 reset (see CvmInstructionSet.Instructions' own remarks); \"nop\" is the only valid opcode right now.");
            break;
          }

          sectionCursors[section] += FitsLitRange(literalPass1Value) ? 1 : 2;
          break;

        case "call":
          // "call" (2026-09-30, alongside the new VM's own "scall"/"lcall") -- a pure assembler-level
          // pseudo-mnemonic exactly like "literal" just above, not a real CvmInstructionSet.Instructions
          // entry (see CvmInstructionSet.ShortCallMnemonic's own class-level remarks for the full
          // derivation and Stefan's exact wording): "'call' should try to fit the address into a 'scall'
          // and use 'lcall' if the address does not fit. if an address is unknown at compile time, e.g.
          // an external symbol, 'call' will always use 'lcall'." Its own word count depends on the
          // operand, exactly like "literal" -- 1 word (scall) when a PLAIN LITERAL number fits scall's own
          // 14-bit address field, 2 (lcall's tag word plus its own trailing address word) otherwise.
          //
          // Unlike "literal" (which refuses a label operand outright, since sizing depends on knowing the
          // value up front), "call" is expected to routinely take a label operand -- calling a named
          // subroutine is its whole point -- so a label/import name is never rejected, but per Stefan's own
          // words above it ALWAYS sizes as lcall: this two-pass assembler fixes every instruction's word
          // length here, in pass 1, before most labels' own final section-relative offsets are known (a
          // forward reference is the common case for a subroutine placed after its own caller -- see this
          // class's own remarks), so there is no way to know here whether a label's eventual address would
          // have fit scall without a further, iterative relaxation pass this assembler does not implement.
          // Treating "unresolved right now" the same as "unknown at compile time" (Stefan's own example is
          // an external symbol, but a not-yet-resolved local label is, operationally, exactly the same
          // kind of "can't decide yet" case) keeps this always correct, if occasionally more conservative
          // than a human hand-writing "scall" directly could have been -- a known, accepted trade-off, not
          // a bug, flagged in full in CvmInstructionSet.ShortCallMnemonic's own remarks.
          if (line.Args.Count != 1)
          {
            errors.Add($"line {line.LineNumber}: \"call\" requires exactly one operand, e.g. \"call 0x1234\" or \"call loop\".");
            break;
          }

          sectionCursors[section] += TryParseNumericLiteral(line.Args[0], out int callPass1Value) && (uint)callPass1Value <= (uint)CvmInstructionSet.ShortCallAddressMask
              ? 1
              : 2;
          break;

        default:
          CvmInstructionSet.CvmInstructionShape? shape = CvmInstructionSet.TryGetShape(line.Directive);
          if (shape is null)
          {
            errors.Add($"line {line.LineNumber}: \"{line.Directive}\" is not a known instruction or directive.");
            break;
          }

          // Node 306/305's twelve unified floating-point ops (EmbeddedUnsignedValuePair, 2026-09-16,
          // widened from six to twelve in the 2026-09-21 rework) and node 508's litm/lit2
          // (TwoTrailingWords, 2026-09-27 -- see CvmInstructionSet.LitrMnemonic's own remarks) are the
          // mnemonic families here needing exactly TWO operands -- comma-separated, matching this
          // assembler's own established ".word 1, 2, 3" convention (e.g. "fadd 3, 2", "litm 0x1234,
          // 0x5678"), rather than Stefan's own space-separated "fadd 3 2" example, which describes the
          // CVM Debugger's separate, immediately-resolving assembler (Ga144.Evb.Ide.Services.CvmAssemblyLanguage,
          // whose own tokenizer splits on whitespace, not commas) -- the two assemblers' syntax
          // conventions genuinely differ here, so each keeps its own rather than forcing one into the
          // other's mold. EXCEPT fpop/fpush (CORRECTED 2026-09-21, per Stefan directly: "only 1
          // parameter, the other opcodes have 2") -- those two are plain EmbeddedUnsignedValue, not Pair,
          // so they fall into the one-operand branch below like any other EmbeddedUnsignedValue mnemonic.
          int requiredArgCount = shape.Encoding is CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair or CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords ? 2 : shape.HasOperand ? 1 : 0;
          if (line.Args.Count != requiredArgCount)
          {
            errors.Add(requiredArgCount switch
            {
              2 => $"line {line.LineNumber}: \"{shape.Mnemonic}\" requires exactly two operands, e.g. \"{shape.Mnemonic} 3, 2\".",
              1 => $"line {line.LineNumber}: \"{shape.Mnemonic}\" requires exactly one operand, e.g. \"{shape.Mnemonic} 0x1234\".",
              _ => $"line {line.LineNumber}: \"{shape.Mnemonic}\" does not take an operand."
            });
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

        case "literal":
          {
            // See this method's own pass-1 remarks on "literal" -- pass 1 already validated the arg count
            // and that the operand parses as a plain literal (never a label), so both are re-parsed here,
            // not re-validated. "literal" has no CvmInstructionSet.Instructions entry of its own, so
            // neither "lit"'s self-describing word nor "litr"'s placeholder-plus-relocation is reached via
            // the generic default case below -- both are written out directly here instead. The "!" below
            // is safe by the same invariant the generic default case's own pass-1/pass-2 split already
            // relies on (see this method's own class-level remarks): pass 2 only ever runs once pass 1 has
            // found zero errors across every line, and pass 1's own "literal" case (above) now guards
            // against "lit" being retired (ADDED 2026-09-30) before this point is ever reached.
            TryParseSignedNumericLiteral(line.Args[0], out int literalValue);
            CvmInstructionSet.CvmInstructionShape litShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic)!;
            CvmSection literalSection = objectFile.GetOrAddSection(section);
            if (FitsLitRange(literalValue))
            {
              literalSection.Words.Add(litShape.Tag | (literalValue & litShape.ValueBitMask));
              break;
            }

            // Too big for "lit" -- fall back to "litr": the exact same placeholder-word-plus-CvmOpcode-
            // relocation-plus-trailing-operand-word shape the generic TrailingWord branch below emits for
            // any other node-resolved mnemonic, just written out directly since "literal" bypasses that
            // generic dispatch entirely.
            CvmInstructionSet.CvmInstructionShape litrShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LitrMnemonic)!;
            int litrOpcodeOffset = literalSection.Words.Count;
            literalSection.Words.Add(0x8000 | litrShape.Id);
            externalSymbols.Add(litrShape.Mnemonic);
            objectFile.Relocations.Add(new CvmRelocation
            {
              SectionName = section,
              WordOffset = litrOpcodeOffset,
              SymbolName = litrShape.Mnemonic,
              Type = CvmRelocationType.CvmOpcode,
            });
            literalSection.Words.Add(literalValue & CvmWordCodec.WordMask);
            break;
          }

        case "call":
          {
            // See this method's own pass-1 remarks on "call" for the fitting rule this mirrors, and
            // CvmInstructionSet.ShortCallMnemonic's own class-level remarks for the full derivation. "call"
            // has no CvmInstructionSet.Instructions entry of its own, so neither "scall"'s self-describing
            // word nor "lcall"'s tag-word-plus-trailing-operand-word is reached via the generic default case
            // below -- both are written out directly here instead, exactly like "literal" just above.
            CvmInstructionSet.CvmInstructionShape shortCallShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.ShortCallMnemonic)!;
            CvmInstructionSet.CvmInstructionShape longCallShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LongCallMnemonic)!;
            CvmSection callSection = objectFile.GetOrAddSection(section);

            if (TryParseNumericLiteral(line.Args[0], out int callLiteralValue) && (uint)callLiteralValue <= (uint)shortCallShape.ValueBitMask)
            {
              // Fits scall's own address field -- emit it directly, exactly like a bare "scall" line would
              // (see the generic EmbeddedAddress branch below): no tag, no relocation, the word IS the
              // address, fully known right now.
              callSection.Words.Add(callLiteralValue & shortCallShape.ValueBitMask);
              break;
            }

            // Either too big for scall, or not a plain literal at all (a label or ".import"ed name) --
            // per this method's own pass-1 remarks on "call", either case always lowers to lcall here: no
            // relocation for the TAG word (already fully known, shape.Tag), then the exact same
            // AbsoluteAddress-relocated trailing word a ".word"/"pushlit" label or import operand would get.
            callSection.Words.Add(longCallShape.Tag);
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
            break;
          }

        default:
          CvmInstructionSet.CvmInstructionShape shape = CvmInstructionSet.TryGetShape(line.Directive)!;
          CvmSection codeSection = objectFile.GetOrAddSection(section);

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress)
          {
            // "scall"-style (CVM2's OLD "call" used to be the only mnemonic here -- see
            // CvmInstructionSet.CvmOperandEncoding.EmbeddedAddress's own remarks): there is no tag word
            // at all here -- the instruction's one and only word IS the (eventually resolved) target
            // address, exactly the same relocation a ".word"/"pushlit" label or import operand would get
            // (AbsoluteAddress: "write the resolved address into this word as-is"), just narrower than a
            // full word -- GENERALIZED 2026-09-30 to read the field width straight off shape.ValueBitMask
            // (0x3FFF/14 bits for scall) rather than the single hardcoded CallAddressMask (0x7FFF/15 bits)
            // this branch used exclusively for CVM2's OLD call, so a future EmbeddedAddress mnemonic with
            // yet another width needs no change here either.
            EmitOperandWord(
                objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors,
                maxValue: shape.ValueBitMask,
                rangeDescription: $"a {System.Numerics.BitOperations.PopCount((uint)shape.ValueBitMask)}-bit \"{shape.Mnemonic}\" target (0x0000-0x{shape.ValueBitMask:X4})");
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord)
          {
            // "lcall"-style (2026-09-30, new VM): shape.Tag is already fully known -- no node, no linker,
            // no relocation for the tag word itself, exactly like FixedOpcode's own nop just below -- but
            // unlike nop, a real operand follows: the target address, in a full trailing word, resolved
            // exactly like a TrailingWord mnemonic's own trailing operand (literal, label, or import all
            // resolve the same way via EmitOperandWord's default full-word range). Checked here, ahead of
            // the generic tagged-mnemonic path further down (which would otherwise treat this shape's tag
            // word as needing a live-node CvmOpcode relocation, which it never does).
            codeSection.Words.Add(shape.Tag);
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue)
          {
            // "br"/"cbr"/"lit"-style: also no separate tag word -- the instruction's one and only
            // word is shape.Tag (its own fixed high bits) OR'd with a signed value packed into
            // shape.ValueBitMask's low bits (11 bits for br, 10 for cbr -- renamed and re-confirmed
            // 2026-09-09, one bit narrower than br's, see ConditionalBranchTag's own remarks -- 10 for
            // lit, node 509's own literal-load form, which absorbed the retired slit's role 2026-09-09 --
            // EmitEmbeddedSignedValue reads the width straight off the shape, so it needs no
            // per-mnemonic special-casing here or there). Fully self-describing from a literal operand
            // alone, so unlike the tagged mnemonics below this needs no placeholder/relocation/external
            // symbol at all. br/cbr ALSO accept a label operand now (see EmitEmbeddedSignedValue's own
            // remarks for the relative-offset computation) -- lit does not, since it isn't an address
            // computation at all.
            bool supportsRelativeLabel = shape.Mnemonic is CvmInstructionSet.BranchMnemonic or CvmInstructionSet.ConditionalBranchMnemonic;
            EmitEmbeddedSignedValue(codeSection, shape, line.Args[0], line.LineNumber, codeSection.Words.Count, supportsRelativeLabel, labelOffsets, section, imported, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValue)
          {
            // Node 606's eight ops: also no separate tag word -- shape.Tag OR'd with an UNSIGNED value
            // packed into shape.ValueBitMask's low bits (8 bits for all eight of them). Also fully
            // self-describing, so also no placeholder/relocation/external symbol. Node 306's fpop/fpush
            // ALSO route through here (CORRECTED 2026-09-21, per Stefan: they take only one argument,
            // unlike the other ten floating-point ops below) -- shape.Tag OR'd with just the fff field,
            // shape.ValueBitMask = FloatingPointRegisterFieldBitMask, no second operand at all.
            EmitEmbeddedUnsignedValue(codeSection, shape, line.Args[0], line.LineNumber, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair)
          {
            // Node 306's TEN two-argument floating-point ops (widened from six to twelve in the
            // 2026-09-21 rework, then narrowed back to ten here once fpop/fpush turned out to be
            // one-argument -- see the EmbeddedUnsignedValue branch above): also no separate tag word --
            // shape.Tag OR'd with TWO independently-packed unsigned register operands (fff in
            // shape.ValueBitMask, ggg in shape.SecondValueBitMask). Also fully self-describing, so also
            // no placeholder/relocation/external symbol.
            EmitEmbeddedUnsignedValuePair(codeSection, shape, line.Args[0], line.Args[1], line.LineNumber, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcode)
          {
            // ADDED 2026-09-30, for the new VM's reset (nop, Id 0 -- see CvmInstructionSet.Instructions'
            // own remarks at the top of its list, and CvmOperandEncoding.FixedOpcode's own remarks).
            // Unlike every mnemonic that falls through to the generic tagged path below (None/
            // TrailingWord/TwoTrailingWords/NodeResolvedEmbeddedValue -- all of which need a live node
            // compile to resolve their real opcode, so they get a 0x8000|Id placeholder plus a CvmOpcode
            // relocation for the linker to fill in later), a FixedOpcode mnemonic's real opcode is
            // ALREADY fully known right now, from shape.Tag alone -- no node, no linker, no relocation,
            // no external symbol. Takes no operand either (HasOperand is false for this encoding), so
            // there is nothing else to emit.
            codeSection.Words.Add(shape.Tag);
            break;
          }

          // The address-register family's eight ops (arinc/ardec/arinc2/ardec2/arld/arst/lda/sta --
          // RENUMBERED 2026-09-15 from physical node 306 to physical node 308, see Cvm.Node308Program's
          // own remarks) need BOTH a live-node-resolved base (which function -- exactly what the generic
          // tagged path just below already provides via its own CvmOpcode relocation) AND a user-supplied
          // embedded register operand packed into the SAME word. Unlike the base address, the register
          // operand is a plain literal the assembler already knows in full right now, so it needs no
          // relocation of its own -- it is validated here against the shape's own ValueBitMask
          // (AddressRegisterRegisterFieldBitMask, renamed 2026-09-15 from Node306RegisterFieldBitMask,
          // same 0-7 range) and carried on CvmRelocation.EmbeddedValue for the linker to OR into
          // the resolved base word once that's known (see CvmRelocation.EmbeddedValue's own remarks, and
          // CvmLinker's own remarks on its CvmOpcode case). 0 for every other mnemonic (their shapes carry
          // no ValueBitMask at all under this encoding, so this is simply never reached for them). Node
          // 306's OLD 'fpop (2026-09-15) used to flow through this exact same generic path too -- see
          // CvmInstructionSet.FloatingPointRegisterFieldBitMask's own remarks for why fpop (and the rest
          // of node 306/305's floating-point family) moved to a self-describing path instead, in the
          // 2026-09-21 rework -- EmbeddedUnsignedValuePair for ten of the twelve, but plain
          // EmbeddedUnsignedValue (the branch above, not this one) for fpop/fpush themselves, per
          // Stefan's own same-day correction that those two take only one argument.
          int embeddedRegisterValue = 0;
          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue)
          {
            if (!TryParseNumericLiteral(line.Args[0], out int registerIndex) || registerIndex < 0 || registerIndex > shape.ValueBitMask)
            {
              errors.Add($"line {line.LineNumber}: \"{line.Args[0]}\" is not a valid register index for \"{shape.Mnemonic}\" -- expected 0..{shape.ValueBitMask}, e.g. \"{shape.Mnemonic} 0\".");
              registerIndex = 0;
            }

            // 2026-09-27: shifted left by shape.ValueBitShift before being carried on the relocation, so
            // a register field that does NOT sit at bit 0 (the new node-510 register-access family's own
            // 6-bit register field occupies bits 9-4, not bits 5-0 the way node 511's/node 308's own
            // register fields always have) still lands in the right place once CvmLinker OR's this
            // relocation's own EmbeddedValue straight into the resolved base word -- see CvmLinker.cs's
            // own CvmOpcode case, unchanged by this addition since the shift is already applied here.
            embeddedRegisterValue = registerIndex << shape.ValueBitShift;
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
            EmbeddedValue = embeddedRegisterValue,
          });

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.TrailingWord)
          {
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
          }
          else if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords)
          {
            // litm/lit2 only (2026-09-27) -- the SECOND operand word, immediately after the first;
            // otherwise identical to the TrailingWord branch just above.
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
            EmitOperandWord(objectFile, section, line.Args[1], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
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
  /// Emits a <c>br</c>/<c>cbr</c>/<c>lit</c> word: <paramref name="shape"/>.Tag OR'd with a signed
  /// literal value packed into <paramref name="shape"/>.ValueBitMask's low bits -- reading the field
  /// width straight off the shape (11 bits for br, 10 for cbr -- renamed and re-confirmed 2026-09-09,
  /// see ConditionalBranchTag's own remarks -- 10 for lit, node 509's own literal-load form which
  /// absorbed the retired slit's role 2026-09-09) is what lets one method serve every
  /// <see cref="CvmInstructionSet.CvmOperandEncoding.EmbeddedSignedValue"/> mnemonic without a
  /// per-mnemonic branch here; adding another one someday needs no change to this method at all, only
  /// a new <see cref="CvmInstructionSet.Instructions"/> entry with its own tag and mask.
  ///
  /// <paramref name="supportsRelativeLabel"/> (true only for br/cbr -- <c>lit</c> isn't an address
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
  /// Emits an <c>enter</c>/<c>stl</c>/<c>stp</c>/<c>ldl</c>/<c>ldp</c>
  /// (the now-retired <c>lal</c>/<c>lap</c>/<c>adjust</c> used to belong here too; or, since 2026-09-06, node 306's
  /// <c>ldar</c>/<c>star</c>/<c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c>; or, since 2026-09-10, node
  /// 508's <c>ldg</c>/<c>stg</c>)
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

  /// <summary>
  /// Emits one of node 306/305's twelve unified floating-point ops (<c>fadd</c>/<c>fsub</c>/<c>fmin</c>/
  /// <c>fmax</c>/<c>fmul</c>/<c>fdiv</c>/<c>fmove</c>/<c>fconst</c>/<c>fneg</c>/<c>fabs</c>/<c>fpop</c>/
  /// <c>fpush</c>, 2026-09-16, widened from six to twelve in the 2026-09-21 rework): <paramref name="shape"/>.Tag OR'd with TWO
  /// independently-packed UNSIGNED register operands -- <paramref name="firstOperand"/> (register
  /// <c>fff</c>, the first operand and result register) into <paramref name="shape"/>.ValueBitMask, and
  /// <paramref name="secondOperand"/> (register <c>ggg</c>, the second operand, read-only) into
  /// <paramref name="shape"/>.SecondValueBitMask. Otherwise a straight duplicate of
  /// <see cref="EmitEmbeddedUnsignedValue"/>'s own per-field validate/shift/OR pattern, just run twice.
  /// Like that method, this does NOT (yet) accept a label or import operand for either register, and an
  /// out-of-range or non-numeric literal in either position is a hard error, never silently truncated.
  /// </summary>
  private static void EmitEmbeddedUnsignedValuePair(
      CvmSection targetSection,
      CvmInstructionSet.CvmInstructionShape shape,
      string firstOperand,
      string secondOperand,
      int lineNumber,
      List<string> errors)
  {
    int firstMaxValue = shape.ValueBitMask >> shape.ValueBitShift;
    int secondMaxValue = shape.SecondValueBitMask >> shape.SecondValueBitShift;

    if (!TryParseNumericLiteral(firstOperand, out int first))
    {
      errors.Add($"line {lineNumber}: \"{firstOperand}\" is not a literal unsigned value -- \"{shape.Mnemonic}\" does not (yet) support a label/import operand.");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    if (!TryParseNumericLiteral(secondOperand, out int second))
    {
      errors.Add($"line {lineNumber}: \"{secondOperand}\" is not a literal unsigned value -- \"{shape.Mnemonic}\" does not (yet) support a label/import operand.");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    if (first < 0 || first > firstMaxValue)
    {
      errors.Add($"line {lineNumber}: {first} does not fit in \"{shape.Mnemonic}\"'s first (register) operand (0..{firstMaxValue}).");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    if (second < 0 || second > secondMaxValue)
    {
      errors.Add($"line {lineNumber}: {second} does not fit in \"{shape.Mnemonic}\"'s second (register) operand (0..{secondMaxValue}).");
      targetSection.Words.Add(shape.Tag);
      return;
    }

    targetSection.Words.Add(shape.Tag | ((first << shape.ValueBitShift) & shape.ValueBitMask) | ((second << shape.SecondValueBitShift) & shape.SecondValueBitMask));
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

  /// <summary>
  /// True when <paramref name="value"/> fits <c>lit</c>'s own signed <see cref="CvmInstructionSet.CvmInstructionShape.ValueBitMask"/>
  /// range -- used by the "literal" pseudo-mnemonic's own pass-1/pass-2 cases above to decide "lit" vs
  /// "litr" (see those cases' own remarks). A small, deliberate duplicate of
  /// <see cref="Ga144.Evb.Ide.Services.CvmAssemblyLanguage"/>'s own identically-named helper, per this
  /// project's own standing practice of not sharing code between the two assemblers.
  /// </summary>
  private static bool FitsLitRange(int value)
  {
    // GUARD added 2026-09-30: "lit" was retired in the new VM's reset (see CvmInstructionSet.
    // Instructions' own remarks at the top of its list) -- TryGetShape now returns null for it, where
    // this used to unconditionally assume a shape existed (the "!" null-forgiving operator would
    // otherwise crash with a NullReferenceException the moment a source file used "literal" at all, even
    // in pass 1's own cursor-advancing walk). Returning false routes the caller down its own "too big for
    // lit" / litr path instead, which the "literal" case's own guard (both passes, above) turns into a
    // clean, specific error rather than a crash.
    CvmInstructionSet.CvmInstructionShape? litShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.LitMnemonic);
    if (litShape is null)
    {
      return false;
    }

    int maxValue = litShape.ValueBitMask >> 1;
    int minValue = -(maxValue + 1);
    return value >= minValue && value <= maxValue;
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