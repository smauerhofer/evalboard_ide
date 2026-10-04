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
/// <b>SUPERSEDED 2026-10-03 (CVM redesign): the scall/lcall/"call"-pseudo-mnemonic paragraph just below is
/// historical.</b> <c>scall</c> and <c>lcall</c> are retired (their rows are commented out in
/// <see cref="CvmInstructionSet.Instructions"/>), and <c>call</c> is now a real two-word, node-509-resolved
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> instruction like <c>link</c>/<c>jmp</c>,
/// assembled by the generic default case with no special handling here. The redesigned VM's other new words
/// (<c>pop</c>/<c>push</c>/<c>dpop</c>/<c>dpush</c>/<c>rinc</c>/<c>rdec</c>/<c>radd</c> with a 4-bit register
/// operand, <c>popi</c>/<c>pushi</c>/<c>dpopi</c>/<c>dpushi</c> without one, <c>jmp</c> with an address word)
/// all assemble through that same generic path -- the register ones via the existing
/// <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/> mechanism (the register
/// index rides on <see cref="CvmRelocation.EmbeddedValue"/>, the linker ORs it into the resolved base word).
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
    (List<ParsedLine>? parsedLines, string? parseError) = Parse(source);
    if (parsedLines is null)
    {
      errors.Add(parseError!);
      return (null, errors);
    }

    List<ParsedLine> lines = parsedLines;

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

        // case "call": -- RETIRED as an assembler-level pseudo-mnemonic 2026-10-03 (CVM redesign). It used
        // to be sized here (scall 1 word / lcall 2 words) and lowered to one of those two in pass 2. Both
        // are gone: "call" is now an ordinary node-509-resolved TrailingWord opcode (CvmInstructionSet Id
        // 198, 2 words: the opcode word, resolved by the linker through a CvmOpcode relocation keyed on
        // "call", then the address word), so it reaches the generic default case below like "link"/"jmp".
        // The old iterative-relaxation discussion that lived here (a linker-level scall shrink pass) is moot.

        // ADDED 2026-10-02, for the "CVM_pipeline" table's own cbr, RENAMED the same day to if
        // (CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord -- see that
        // enum case's own remarks, and CvmInstructionSet.IfMnemonic's own remarks for the full,
        // now-FULLY-confirmed account). Every open question Stefan was asked is now answered -- the
        // "cond" field is a node-406-resolved reference (not a plain number), the branch's own FINAL
        // polarity (the intuitive "goto label when the condition holds" reading, achieved by Stefan's
        // own change to node 406's flag evaluation, not anything this assembler encodes), the register
        // field's hard r0-r15 limit (4 bits, by design), and the complete three-operand syntax
        // "if <reg> <cond> <target>".
        //
        // REAL SUPPORT ADDED here the same day, per Stefan directly asking for the missing MECHANISM
        // (not just a confirmation) to be invented: every other tagged mnemonic this assembler resolves
        // against a live node leaves a single CvmRelocationType.CvmOpcode relocation keyed by MNEMONIC
        // against CvmPrimitiveTable -- but "if"'s own "cond" field resolves per NAMED CONDITION VALUE
        // (ten different node-406 symbols), not per mnemonic, a shape BuildEncodeTable's own
        // Instructions list structurally excludes (see that list's own remarks). The invented mechanism:
        // ten synthetic CvmPrimitiveTable entries, one per condition name, each the complete BASE word
        // (tag | resolved-cond-address<<4, register left at 0) -- computed by
        // Ga144.Evb.Ide.Services.CvmAssemblyLanguage.BuildIfConditionEncodeTable (the condition-keyed
        // sibling of BuildEncodeTable) and merged into the exported table by
        // Ga144.Evb.Ide.Services.CvmPrimitiveTableExporter, under synthetic keys
        // CvmInstructionSet.IfPrimitiveNamePrefix + CvmInstructionSet.IfConditionPrimitiveKeyByName[name]
        // (e.g. "if.eq0" for "==0" -- never the raw condition name itself, which would corrupt
        // CvmPrimitiveTable's own ".gaprim" text format for the four names containing "=" -- see that
        // dictionary's own remarks). This case below resolves the condition name to that same synthetic
        // key and emits a CvmRelocationType.CvmOpcode relocation against it, with the literal register
        // operand carried on CvmRelocation.EmbeddedValue -- exactly node 308's own address-register
        // mechanism, reused verbatim; CvmLinker.cs/CvmPrimitiveTable.cs themselves need NO changes.
        //
        // The third operand (the branch target) is a SEPARATE trailing word -- a signed relative offset,
        // same-section-label-only (mirroring EmitEmbeddedSignedValue's own br/cbr convention, see
        // EmitIfTargetWord's own remarks for exactly how and why it differs) -- resolved independently of
        // the relocation above, since a relative offset to a same-file label is fully known at assemble
        // time and needs no linker involvement at all.
        case CvmInstructionSet.IfMnemonic:
          {
            if (line.Args.Count != 3)
            {
              errors.Add($"line {line.LineNumber}: \"if\" requires exactly three operands (register, condition, target), e.g. \"if 0, ==0, loop\".");
              break;
            }

            if (!CvmInstructionSet.IfConditionPrimitiveKeyByName.ContainsKey(line.Args[1]))
            {
              string validConditionNames = string.Join(", ", CvmInstructionSet.IfConditionPrimitiveKeyByName.Keys);
              errors.Add($"line {line.LineNumber}: \"if\" does not recognize condition \"{line.Args[1]}\" -- valid conditions are: {validConditionNames}.");
              break;
            }

            sectionCursors[section] += 2; // this shape's own fixed WordLength -- tag word + trailing offset word, regardless of operand values.
            break;
          }

        // ADDED 2026-10-03, per Stefan: "cond <reg> <cond>" (CvmInstructionSet.CondMnemonic) -- the one-word
        // sibling of "if" (no trailing offset word): evaluates the condition on the register and pushes the
        // result onto the stack. Two operands, comma-separated like everything else in this assembler:
        // "cond r0, ==0". The condition resolves through the same ten synthetic primitive-table entries as
        // "if", under the "cond." prefix (CvmInstructionSet.CondPrimitiveNamePrefix).
        case CvmInstructionSet.CondMnemonic:
          {
            if (line.Args.Count != 2)
            {
              errors.Add($"line {line.LineNumber}: \"cond\" requires exactly two operands (register, condition), e.g. \"cond r0, ==0\".");
              break;
            }

            if (!CvmInstructionSet.IfConditionPrimitiveKeyByName.ContainsKey(line.Args[1]))
            {
              string validCondConditionNames = string.Join(", ", CvmInstructionSet.IfConditionPrimitiveKeyByName.Keys);
              errors.Add($"line {line.LineNumber}: \"cond\" does not recognize condition \"{line.Args[1]}\" -- valid conditions are: {validCondConditionNames}.");
              break;
            }

            sectionCursors[section] += 1; // this shape's own fixed WordLength -- one word, no trailing word.
            break;
          }

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
          //
          // ADDED 2026-10-02: next32 (CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords) joins this
          // same two-operand group, same reasoning as litm/lit2 -- two separate trailing words, each a
          // plain literal/label operand (see CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords' own
          // remarks). if (CvmOperandEncoding.EmbeddedUnsignedValuePairWithTrailingWord, renamed from cbr
          // 2026-10-02) never reaches this generic branch at all -- it has its own dedicated,
          // 3-operand case above (register, condition name, branch target), since this switch's own
          // requiredArgCount/EmitOperandWord machinery has no group for a mnemonic needing a
          // per-condition-name (not per-mnemonic) node-resolved relocation plus a separately-resolved
          // relative-offset trailing word; see that case's own remarks for the real encoding.
          // ADDED 2026-10-03: push2 accepts ONE operand (a single 32-bit number, encoded hi-lo) or TWO
          // (two 16-bit numbers, hi then lo) -- both always three memory words (opcode, hi, lo).
          if (string.Equals(shape.Mnemonic, CvmInstructionSet.Push2Mnemonic, StringComparison.Ordinal))
          {
            if (line.Args.Count is not (1 or 2))
            {
              errors.Add($"line {line.LineNumber}: \"push2\" takes one 32-bit operand (\"push2 0x12345678\") or two 16-bit operands (\"push2 0x1234, 0x5678\").");
              break;
            }

            sectionCursors[section] += shape.WordLength;
            break;
          }

          // ADDED 2026-10-04: rlit/dlit take TWO operands (register, literal) although their shape is
          // NodeResolvedEmbeddedValue: "rlit r1, 0x1234", "dlit d1, 0x12345678".
          bool isRegisterLiteral = shape.Mnemonic is CvmInstructionSet.RegisterLiteralMnemonic or CvmInstructionSet.DoubleLiteralMnemonic;
          int requiredArgCount = isRegisterLiteral || shape.Encoding is CvmInstructionSet.CvmOperandEncoding.EmbeddedUnsignedValuePair or CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords or CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords ? 2 : shape.HasOperand ? 1 : 0;
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

        // case "call": -- RETIRED 2026-10-03 (CVM redesign), see this method's own pass-1 remarks above.
        // The pass-2 lowering to scall/lcall that lived here is gone; the generic default case emits the
        // 0x8000|Id placeholder, the CvmOpcode relocation and the trailing address word.

        case CvmInstructionSet.IfMnemonic:
          {
            // See this method's own pass-1 remarks on "if" for the full design. "if" has no
            // CvmInstructionSet.Instructions-driven generic dispatch path (its 3-operand shape doesn't fit
            // the generic default case's own one/two-operand assumption), so, like "call" and "literal"
            // above, it is written out directly here.
            CvmInstructionSet.CvmInstructionShape ifShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.IfMnemonic)!;
            CvmSection ifSection = objectFile.GetOrAddSection(section);

            // 2026-10-03: "if r0, ==0, loop" -- the register may be written rN (CvmInstructionSet.
            // TryParseSingleRegisterToken) or as a plain number.
            int ifRegister;
            if (CvmInstructionSet.TryParseSingleRegisterToken(CvmInstructionSet.IfMnemonic, line.Args[0], ifShape.SecondValueBitMask, out ifRegister, out string? ifRegisterTokenError))
            {
              if (ifRegisterTokenError is not null)
              {
                errors.Add($"line {line.LineNumber}: {ifRegisterTokenError}");
                ifRegister = 0;
              }
            }
            else if (!TryParseNumericLiteral(line.Args[0], out ifRegister) || ifRegister < 0 || ifRegister > ifShape.SecondValueBitMask)
            {
              errors.Add($"line {line.LineNumber}: \"{line.Args[0]}\" is not a valid register for \"if\" -- expected r0..r{ifShape.SecondValueBitMask} (or a plain number 0..{ifShape.SecondValueBitMask}; r0-r15 only, per Stefan directly), e.g. \"if r0, ==0, loop\".");
              ifRegister = 0; // keep going -- pass 1 already fixed this instruction's own 2-word size and every later label's address against it; emitting a wrong-but-correctly-SIZED word here (same resilience convention the generic NodeResolvedEmbeddedValue branch below already uses for an out-of-range register) keeps the rest of the file's layout intact even though this run will fail overall.
            }

            // Pass 1 already rejected an unrecognized condition name outright, so this lookup cannot fail
            // here -- unlike the register check just above, there is no sensible default base word to
            // substitute for an unknown condition (which of ten would it be?), so this is asserted, not
            // defended against a second time.
            if (CvmInstructionSet.IsRegisterlessCondition(line.Args[1]) && ifRegister != 0)
            {
              errors.Add($"line {line.LineNumber}: \"if {line.Args[1]}\" takes no register -- write \"if {line.Args[1]} then label\" (the register field is encoded as 0), not \"{line.Args[0]}\".");
              ifRegister = 0;
            }

            string ifConditionKey = CvmInstructionSet.IfConditionPrimitiveKeyByName[line.Args[1]];
            string ifPrimitiveName = CvmInstructionSet.IfPrimitiveNamePrefix + ifConditionKey;

            int ifOpcodeOffset = ifSection.Words.Count;
            // Same 0x8000|Id self-describing placeholder convention as every other node-resolved mnemonic
            // below -- readable in an unlinked dump even though the linker (not this placeholder) supplies
            // the real, tagged word via the CvmOpcode relocation just below.
            ifSection.Words.Add(0x8000 | ifShape.Id);
            externalSymbols.Add(ifPrimitiveName);
            objectFile.Relocations.Add(new CvmRelocation
            {
              SectionName = section,
              WordOffset = ifOpcodeOffset,
              SymbolName = ifPrimitiveName,
              Type = CvmRelocationType.CvmOpcode,
              // The register operand is a plain literal this assembler already knows in full right now --
              // exactly node 308's own address-register mechanism, reused verbatim (see this file's own
              // remarks on that family, and CvmLinker's own CvmOpcode case) -- never shifted, since "if"'s
              // own SecondValueBitShift is 0 (the register field sits at bits 3-0 already).
              EmbeddedValue = ifRegister & ifShape.SecondValueBitMask,
            });

            // The third operand -- the branch target -- is this shape's own SEPARATE trailing word (a
            // signed relative offset), resolved independently of the relocation above: see
            // EmitIfTargetWord's own remarks for why this needs its own method rather than reusing
            // EmitEmbeddedSignedValue or EmitOperandWord as-is.
            EmitIfTargetWord(objectFile, section, line.Args[2], line.LineNumber, ifOpcodeOffset, labelOffsets, imported, errors);
            break;
          }

        case CvmInstructionSet.CondMnemonic:
          {
            // See pass 1's own "cond" remarks. Written out directly (like "if" just above) rather than going
            // through the generic default case, because the second operand is a condition NAME. Same
            // CvmOpcode-relocation mechanism as "if": the linker supplies 0x9000 | cond<<4 from the synthetic
            // "cond.<key>" primitive-table entry, EmbeddedValue carries the literal register.
            CvmInstructionSet.CvmInstructionShape condShape = CvmInstructionSet.TryGetShape(CvmInstructionSet.CondMnemonic)!;
            CvmSection condSection = objectFile.GetOrAddSection(section);

            int condRegister;
            if (CvmInstructionSet.TryParseSingleRegisterToken(CvmInstructionSet.CondMnemonic, line.Args[0], condShape.SecondValueBitMask, out condRegister, out string? condRegisterTokenError))
            {
              if (condRegisterTokenError is not null)
              {
                errors.Add($"line {line.LineNumber}: {condRegisterTokenError}");
                condRegister = 0;
              }
            }
            else if (!TryParseNumericLiteral(line.Args[0], out condRegister) || condRegister < 0 || condRegister > condShape.SecondValueBitMask)
            {
              errors.Add($"line {line.LineNumber}: \"{line.Args[0]}\" is not a valid register for \"cond\" -- expected r0..r{condShape.SecondValueBitMask} (or a plain number 0..{condShape.SecondValueBitMask}), e.g. \"cond r0, ==0\".");
              condRegister = 0; // keep going -- pass 1 already fixed this instruction's own size; a wrong-but-correctly-SIZED word keeps the layout intact (this run fails overall anyway).
            }

            // Pass 1 already rejected an unrecognized condition name, so this lookup cannot fail here.
            if (CvmInstructionSet.IsRegisterlessCondition(line.Args[1]) && condRegister != 0)
            {
              errors.Add($"line {line.LineNumber}: \"cond {line.Args[1]}\" takes no register -- write \"cond {line.Args[1]}\" (the register field is encoded as 0), not \"{line.Args[0]}\".");
              condRegister = 0;
            }

            string condPrimitiveName = CvmInstructionSet.CondPrimitiveNamePrefix + CvmInstructionSet.IfConditionPrimitiveKeyByName[line.Args[1]];

            int condOpcodeOffset = condSection.Words.Count;
            condSection.Words.Add(0x8000 | condShape.Id); // same 0x8000|Id self-describing placeholder convention as "if" -- the linker supplies the real word.
            externalSymbols.Add(condPrimitiveName);
            objectFile.Relocations.Add(new CvmRelocation
            {
              SectionName = section,
              WordOffset = condOpcodeOffset,
              SymbolName = condPrimitiveName,
              Type = CvmRelocationType.CvmOpcode,
              EmbeddedValue = condRegister & condShape.SecondValueBitMask,
            });
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
            //
            // CONFIRMED 2026-09-30 (same day), per Stefan: "scall 0 does not exist. 'nop' has precedence."
            // -- see CvmInstructionSet.ShortCallMinimumAddress's own remarks. Only catchable here for a
            // literal operand, known right now, without a label/import resolution: a label/import operand
            // that happens to resolve to address 0 only once CvmLinker runs is NOT (yet) caught anywhere --
            // a pre-existing gap in this relocation's own range checking (the same gap CVM2's OLD call
            // always had for its own 15-bit range), not something newly introduced here.
            if (string.Equals(shape.Mnemonic, CvmInstructionSet.ShortCallMnemonic, StringComparison.Ordinal) &&
                TryParseNumericLiteral(line.Args[0], out int scallLiteralValue) && scallLiteralValue == 0)
            {
              errors.Add($"line {line.LineNumber}: \"scall 0\" does not exist -- address 0 is reserved for \"nop\" (the word scall would produce, 0x0000, is identical to nop's own); use \"lcall 0\" instead.");
              break;
            }

            EmitOperandWord(
                objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors,
                maxValue: shape.ValueBitMask,
                rangeDescription: $"a {System.Numerics.BitOperations.PopCount((uint)shape.ValueBitMask)}-bit \"{shape.Mnemonic}\" target (0x0000-0x{shape.ValueBitMask:X4})");
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTrailingWord)
          {
            // "lcall"-style (2026-09-30, new VM; joined 2026-10-02 by "next16" -- see
            // CvmInstructionSet.Instructions' own remarks on Id 187): shape.Tag is already fully known
            // -- no node, no linker, no relocation for the tag word itself, exactly like FixedOpcode's
            // own nop just below -- but unlike nop, a real operand follows: the target address/literal,
            // in a full trailing word, resolved exactly like a TrailingWord mnemonic's own trailing
            // operand (literal, label, or import all resolve the same way via EmitOperandWord's default
            // full-word range). Checked here, ahead of the generic tagged-mnemonic path further down
            // (which would otherwise treat this shape's tag word as needing a live-node CvmOpcode
            // relocation, which it never does).
            codeSection.Words.Add(shape.Tag);
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
            break;
          }

          if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.FixedOpcodeWithTwoTrailingWords)
          {
            // ADDED 2026-10-02, for the "CVM_pipeline" table's own "next32" -- see that encoding's own
            // remarks. Exactly like FixedOpcodeWithTrailingWord just above, except TWO trailing operand
            // words follow the self-describing tag word instead of one; each resolves independently via
            // EmitOperandWord, same as litm/lit2's own two trailing words (see
            // CvmOperandEncoding.TwoTrailingWords' own remarks) just with no live-node tag to resolve.
            codeSection.Words.Add(shape.Tag);
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
            EmitOperandWord(objectFile, section, line.Args[1], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
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
            //
            // JOINED 2026-10-02 by "sbr" (the "CVM_pipeline" table's own short, relative branch, Id
            // 194 -- CvmInstructionSet.ShortBranchMnemonic): br/cbr (the names this check originally
            // listed) are CVM2's OLD, now fully-retired mnemonics -- neither has a live Instructions row
            // any more, so this check can no longer actually match either of them; it is kept, unedited,
            // as the historical record, with sbr added alongside rather than replacing it. Allowing sbr a
            // label operand is a reasonable, low-risk extension of the SAME convention (not asserted by
            // Stefan in so many words): the table itself calls sbr's own row "relative", the same word
            // used for br/cbr's own relative-offset behavior above, and sbr's own ValueBitMask/shift are
            // otherwise identical in spirit (a signed offset packed into the low bits of a self-describing
            // word) -- flagged here rather than silently assumed elsewhere.
            bool supportsRelativeLabel = shape.Mnemonic is CvmInstructionSet.BranchMnemonic or CvmInstructionSet.ConditionalBranchMnemonic or CvmInstructionSet.ShortBranchMnemonic;
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
            // ADDED 2026-10-03: "rN" (16-bit register) / "dN" (double register, dpop/dpush) are accepted
            // alongside a plain number -- see CvmInstructionSet.TryParseRegisterOperand.
            int registerIndex;
            if (CvmInstructionSet.TryParseRegisterOperand(shape, line.Args[0], out registerIndex, out string? registerTokenError))
            {
              if (registerTokenError is not null)
              {
                errors.Add($"line {line.LineNumber}: {registerTokenError}");
                registerIndex = 0;
              }
            }
            else if (!TryParseNumericLiteral(line.Args[0], out registerIndex) || registerIndex < 0 || registerIndex > shape.ValueBitMask)
            {
              string exampleToken = CvmInstructionSet.IsDoubleRegisterMnemonic(shape.Mnemonic) ? "d1" : "r1";
              errors.Add($"line {line.LineNumber}: \"{line.Args[0]}\" is not a valid register for \"{shape.Mnemonic}\" -- expected {exampleToken[0]}0..{exampleToken[0]}{shape.ValueBitMask} (or a plain number 0..{shape.ValueBitMask}), e.g. \"{shape.Mnemonic} {exampleToken}\".");
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

          if (shape.Mnemonic == CvmInstructionSet.RegisterLiteralMnemonic)
          {
            // ADDED 2026-10-04: rlit -- the register is already in the opcode word (embedded value above);
            // the second operand is the 16-bit literal word (a number, or a label/import = absolute address).
            EmitOperandWord(objectFile, section, line.Args[1], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
          }
          else if (shape.Mnemonic == CvmInstructionSet.DoubleLiteralMnemonic)
          {
            // ADDED 2026-10-04: dlit -- one 32-bit number, stored LOW word first, then HIGH (node 509's
            // m/dnext reads "lo hi"; push2 is the other way round). Literal only, no label.
            if (TryParsePush2WideLiteral(line.Args[1], out uint dlitValue))
            {
              codeSection.Words.Add((int)(dlitValue & 0xFFFF));
              codeSection.Words.Add((int)(dlitValue >> 16));
            }
            else
            {
              errors.Add($"line {line.LineNumber}: \"{line.Args[1]}\" is not a 32-bit number (-2147483648..4294967295) -- \"dlit d1, x\" needs a numeric literal, no label.");
              codeSection.Words.Add(0);
              codeSection.Words.Add(0);
            }
          }
          else if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.TrailingWord)
          {
            EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
          }
          else if (shape.Encoding == CvmInstructionSet.CvmOperandEncoding.TwoTrailingWords)
          {
            // litm/lit2 only (2026-09-27) -- the SECOND operand word, immediately after the first;
            // otherwise identical to the TrailingWord branch just above.
            //
            // push2 (2026-10-03) with ONE operand: a single 32-bit number, encoded hi-lo as two literal
            // words (hi first). With two operands it falls through to the two-word path below (x = hi,
            // y = lo), like litm/lit2 -- there a label is allowed, as a 16-bit address.
            if (string.Equals(shape.Mnemonic, CvmInstructionSet.Push2Mnemonic, StringComparison.Ordinal) && line.Args.Count == 1)
            {
              if (TryParsePush2WideLiteral(line.Args[0], out uint push2Value))
              {
                codeSection.Words.Add((int)(push2Value >> 16));
                codeSection.Words.Add((int)(push2Value & 0xFFFF));
              }
              else
              {
                errors.Add($"line {line.LineNumber}: \"{line.Args[0]}\" is not a 32-bit number (-2147483648..4294967295) -- \"push2 x\" with one operand needs a numeric literal, no label; use \"push2 hi, lo\" for two 16-bit values.");
                codeSection.Words.Add(0);
                codeSection.Words.Add(0);
              }
            }
            else
            {
              EmitOperandWord(objectFile, section, line.Args[0], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
              EmitOperandWord(objectFile, section, line.Args[1], line.LineNumber, labelOffsets, imported, externalSymbols, errors);
            }
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

  /// <summary>
  /// ADDED 2026-10-03, for the one-operand form of <c>push2</c>: a decimal or "0x" hex literal (optional
  /// leading '-') that fits 32 bits as a signed or unsigned number (-2147483648..4294967295), returned as
  /// its unsigned 32-bit pattern.
  /// </summary>
  private static bool TryParsePush2WideLiteral(string text, out uint value)
  {
    value = 0;
    bool negative = text.StartsWith('-');
    string body = negative ? text[1..] : text;
    long magnitude;
    if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      if (!long.TryParse(body.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out magnitude))
      {
        return false;
      }
    }
    else if (!long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude))
    {
      return false;
    }

    long signedValue = negative ? -magnitude : magnitude;
    if (signedValue < int.MinValue || signedValue > uint.MaxValue)
    {
      return false;
    }

    value = unchecked((uint)signedValue);
    return true;
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
  /// ADDED 2026-10-02, for "if" alone -- emits this shape's own SEPARATE trailing word (the branch's
  /// signed relative offset), per Stefan directly asking for the real mechanism to be built. This is
  /// NOT <see cref="EmitEmbeddedSignedValue"/> reused as-is, for two reasons: (1) "if"'s own offset is a
  /// full, standalone 16-bit word, not a narrower field packed into the SAME word as a tag -- there is
  /// no <c>shape.Tag</c> to OR it into, and no <c>ValueBitMask</c>-derived width to range-check against
  /// (the whole word IS the offset, masked only to 16 bits, exactly like
  /// Ga144.Evb.Ide.Services.CvmAssemblyLanguage.EncodeIfInstruction's own trailing word on the CVM
  /// Debugger side); (2) it is unconditionally relative (mirroring that same method's own
  /// <c>supportsRelativeLabel: true</c> case for br/cbr, with no "literal-only" mode to support, since
  /// "if" always takes a label-or-literal third operand per Stefan's own confirmed syntax). A literal
  /// operand is used exactly as typed, with no address arithmetic at all. A label operand resolves to
  /// <c>labelOffset - (thisInstructionOffset + 2)</c> -- SAME convention as, and flagged for the SAME
  /// reason as, Ga144.Evb.Ide.Services.CvmAssemblyLanguage.EncodeIfInstruction's own identical formula
  /// is flagged there: an extrapolation of br/cbr's own hardware-confirmed
  /// ONE-WORD convention (<paramref name="thisInstructionOffset"/> + 1, see
  /// <see cref="EmitEmbeddedSignedValue"/>'s own remarks) to this TWO-word shape, not independently
  /// confirmed against real hardware for "if" specifically -- if Stefan confirms otherwise, the fix is
  /// this one line's "+ 2". <paramref name="thisInstructionOffset"/> is the TAG word's own
  /// section-relative offset (the caller passes <c>ifOpcodeOffset</c>, not this trailing word's own
  /// offset), matching that same method's own <c>instructionAddress</c> parameter
  /// exactly. Same same-section-only, no-cross-file-relative-relocation restriction as
  /// <see cref="EmitEmbeddedSignedValue"/>'s own label branch, for the identical reason: there is no
  /// <see cref="CvmRelocationType"/> for "this many words from here, resolved at link time."
  /// </summary>
  private static void EmitIfTargetWord(
      CvmObjectFile objectFile,
      string section,
      string operand,
      int lineNumber,
      int thisInstructionOffset,
      IReadOnlyDictionary<string, (string Section, int Offset)> labelOffsets,
      ISet<string> imported,
      List<string> errors)
  {
    CvmSection targetSection = objectFile.GetOrAddSection(section);

    if (TryParseSignedNumericLiteral(operand, out int literalOffset))
    {
      targetSection.Words.Add(literalOffset & CvmWordCodec.WordMask);
      return;
    }

    if (labelOffsets.TryGetValue(operand, out (string Section, int Offset) target))
    {
      if (target.Section != section)
      {
        errors.Add($"line {lineNumber}: \"if\" cannot branch to \"{operand}\" across sections (\"{operand}\" is in \".section {target.Section}\", this instruction is in \".section {section}\").");
        targetSection.Words.Add(0);
        return;
      }

      targetSection.Words.Add((target.Offset - (thisInstructionOffset + 2)) & CvmWordCodec.WordMask);
      return;
    }

    if (imported.Contains(operand))
    {
      errors.Add($"line {lineNumber}: \"if\" cannot branch to \".import\"ed \"{operand}\" -- its offset from here is not known until link time (there is no relative-offset relocation).");
      targetSection.Words.Add(0);
      return;
    }

    errors.Add($"line {lineNumber}: \"{operand}\" is not a literal signed value or a label defined in this file -- \"if\"'s own branch target does not support anything else.");
    targetSection.Words.Add(0);
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

  // FitsShortCallRange -- DELETED 2026-10-03 together with scall/lcall and the "call" pseudo-mnemonic
  // (CVM redesign); see this file's own pass-1 "call" remarks.

  private sealed record ParsedLine(int LineNumber, string? Label, string? Directive, IReadOnlyList<string> Args);

  private static (List<ParsedLine>? Lines, string? Error) Parse(string source)
  {
    // ADDED 2026-10-02, per Stefan directly asking for C-style "/* ... */" block comments, additional
    // to (not replacing) the ";"/"//" line comments StripComment already strips per-line below -- see
    // StripBlockComments' own remarks for why this has to run as a separate, whole-source pre-pass
    // BEFORE the line split just below, rather than folding into StripComment itself.
    (string? sourceWithoutBlockComments, string? blockCommentError) = StripBlockComments(source.Replace("\r\n", "\n"));
    if (sourceWithoutBlockComments is null)
    {
      return (null, blockCommentError);
    }

    var lines = new List<ParsedLine>();
    string[] rawLines = sourceWithoutBlockComments.Split('\n');
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

      // ADDED 2026-10-03, per Stefan: "true"/"false" need no register -- "if true then label1", "if true, label1",
      // "cond true". An implicit "r0" is inserted so every later check sees the normal register-first form
      // (the register field is encoded as 0).
      if (string.Equals(keyword, CvmInstructionSet.IfMnemonic, StringComparison.OrdinalIgnoreCase) && argsText.Length > 0)
      {
        string firstWord = argsText.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
        if (CvmInstructionSet.IsRegisterlessCondition(firstWord))
        {
          string implicitRegisterText = (argsText.Contains(',') ? "r0, " : "r0 ") + argsText;
          if (implicitRegisterText.Contains(','))
          {
            args = new List<string>(implicitRegisterText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
          }
          else
          {
            args = new List<string> { implicitRegisterText };
          }
        }
      }
      else if (string.Equals(keyword, CvmInstructionSet.CondMnemonic, StringComparison.OrdinalIgnoreCase) &&
               args.Count == 1 && CvmInstructionSet.IsRegisterlessCondition(args[0]))
      {
        args = new List<string> { "r0", args[0] };
      }

      if (string.Equals(keyword, CvmInstructionSet.IfMnemonic, StringComparison.OrdinalIgnoreCase))
      {
        args = NormalizeIfArguments(args);
      }

      lines.Add(new ParsedLine(lineNumber, label, keyword, args));
    }

    return (lines, null);
  }

  /// <summary>
  /// ADDED 2026-10-03: <c>if</c> accepts an optional <c>then</c> before its target, for readability --
  /// <c>if r0, ==0, then test2</c>, <c>if r0, ==0 then test2</c> or the all-space form
  /// <c>if r0 ==0 then test2</c> -- and the word is dropped here, so every later check sees the plain
  /// three operands (register, condition, target). The existing comma form without <c>then</c> is
  /// unchanged; a label that is itself named "then" still works as the last operand.
  /// </summary>
  private static List<string> NormalizeIfArguments(List<string> args)
  {
    static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    if (args.Count == 1)
    {
      string[] words = Words(args[0]);
      if (words.Length == 4 && string.Equals(words[2], "then", StringComparison.OrdinalIgnoreCase))
      {
        return [words[0], words[1], words[3]];
      }

      return words.Length == 3 ? new List<string> { words[0], words[1], words[2] } : args;
    }

    if (args.Count == 2)
    {
      string[] words = Words(args[1]);
      if (words.Length == 3 && string.Equals(words[1], "then", StringComparison.OrdinalIgnoreCase))
      {
        return [args[0], words[0], words[2]];
      }

      return args;
    }

    if (args.Count == 3)
    {
      string[] words = Words(args[2]);
      if (words.Length == 2 && string.Equals(words[0], "then", StringComparison.OrdinalIgnoreCase))
      {
        return [args[0], args[1], words[1]];
      }
    }

    return args;
  }

  /// <summary>
  /// ADDED 2026-10-02, per Stefan directly asking for C-style <c>/* ... */</c> block comments,
  /// ADDITIONAL to (not replacing) the <c>;</c>/<c>//</c> line comments <see cref="StripComment"/>
  /// already strips -- that method is left completely unchanged, and is still what handles those two.
  /// A block comment can span multiple lines, which <see cref="StripComment"/>'s own per-line design
  /// (<see cref="Parse"/> splits the source into lines FIRST, then strips each line independently)
  /// cannot express at all, so this runs as a separate pre-pass over the WHOLE source text, before it
  /// is ever split into lines -- <see cref="Parse"/> calls this first and only then splits the result.
  ///
  /// Every character inside a <c>/* ... */</c> span is dropped, including the delimiters themselves,
  /// EXCEPT a newline, which is always preserved verbatim. That is what keeps every line number
  /// <see cref="Parse"/>/<see cref="Assemble"/> report in their own error messages accurate across a
  /// multi-line comment, exactly as if the commented-out lines were still there, just empty. A closed
  /// comment is replaced by a single space rather than nothing at all, so two tokens written adjacent
  /// to a comment with no surrounding whitespace (e.g. <c>"x/*note*/y"</c>) still tokenize as two
  /// separate words instead of silently fusing into one -- the same convention a C preprocessor uses.
  ///
  /// An unterminated comment (no matching <c>*/</c> before the source ends) is a hard parse error,
  /// reported against the LINE the <c>/*</c> itself started on, not the end of the file, since that is
  /// where a person fixing it needs to look -- surfaced through <see cref="Parse"/>'s own new
  /// <c>Error</c> return value, exactly like every other parse/assemble failure in this class.
  ///
  /// A <c>/*</c> or <c>*/</c> appearing inside an already-recognized <c>;</c>/<c>//</c> line comment is
  /// NOT specially handled here -- by design, this runs BEFORE <see cref="StripComment"/>'s own
  /// per-line stripping, so a stray <c>/*</c> after a <c>;</c>/<c>//</c> still opens a real block
  /// comment (and a lone <c>*/</c> with no preceding <c>/*</c> is simply ordinary text, left untouched,
  /// same as any other character outside a recognized comment). Stefan's own source has not been seen
  /// to mix the two this way; if that ever matters, a fix belongs here, not in
  /// <see cref="StripComment"/>.
  /// </summary>
  private static (string? Result, string? Error) StripBlockComments(string source)
  {
    var output = new System.Text.StringBuilder(source.Length);
    int line = 1;
    int i = 0;
    while (i < source.Length)
    {
      char c = source[i];
      if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
      {
        int startLine = line;
        i += 2;
        bool closed = false;
        while (i < source.Length)
        {
          if (source[i] == '\n')
          {
            output.Append('\n');
            line++;
            i++;
            continue;
          }

          if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
          {
            i += 2;
            closed = true;
            break;
          }

          i++;
        }

        if (!closed)
        {
          return (null, $"line {startLine}: unterminated \"/*\" comment -- no matching \"*/\" found before the end of the file.");
        }

        output.Append(' ');
        continue;
      }

      output.Append(c);
      if (c == '\n')
      {
        line++;
      }

      i++;
    }

    return (output.ToString(), null);
  }

  private static string StripComment(string line)
  {
    int semicolon = line.IndexOf(';');
    int slashSlash = line.IndexOf("//", StringComparison.Ordinal);
    int cut = semicolon < 0 ? slashSlash : (slashSlash < 0 ? semicolon : Math.Min(semicolon, slashSlash));
    return cut < 0 ? line : line[..cut];
  }
}