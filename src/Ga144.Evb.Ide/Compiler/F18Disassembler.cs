using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Compiler;

/// <summary>
/// One decoded slot inside an F18A instruction word: either a plain 5-bit opcode
/// (<see cref="IsControlTransfer"/> false), or a slot-0/1/2 control-transfer
/// opcode (jump/call/next/if/-if) together with its TRUE absolute destination
/// address (DB001 2.3.1) -- a control transfer, when present, is always the
/// LAST slot actually present in the word: no slot after it exists, because its
/// address field occupies the bits later slots would otherwise use.
/// <see cref="Destination"/> is always the full reconstructed address, not the
/// raw field bits a slot 1/2 transfer's word actually stores -- see
/// <see cref="F18Disassembler.Decode"/>'s own <c>ReconstructDestination</c> for
/// why a slot 1 (8-bit field) or slot 2 (3-bit field) transfer's stored bits
/// alone are usually NOT the whole address (2026-09-30 bug fix).
/// </summary>
public sealed record F18DisassembledSlot(int SlotIndex, byte Opcode, string Mnemonic, bool IsControlTransfer, int? Destination)
{
  public override string ToString() => Format(null);

  /// <summary>
  /// Same rendering as <see cref="ToString"/>, but for a control transfer's own embedded destination
  /// address, appends " (&lt;name&gt;)" when that address is recognized -- either as one of this
  /// project's own compiled labels (<paramref name="labelsByAddress"/>, from
  /// <see cref="F18Disassembler.BuildLabelsByAddress"/>, e.g. a "jump"/"call" to another word in the
  /// SAME compiled image) or as one of the 19 documented port/multiport addresses
  /// (<see cref="PortAddressNames"/>, e.g. "right"/"r---" -- a "jump port" embedding a live-focus
  /// target rather than a real code address, exactly what <see cref="Services.KrakenProtocol.BuildFocus"/>
  /// compiles). The two never collide (every multiport address is 0x100 or above; every RAM/ROM
  /// address a label can name is below that), so checking labels first is safe either way -- done
  /// first here only because a project's own label is the more specific, useful name on the rare
  /// occasion both happen to be supplied and somehow agree.
  /// </summary>
  public string Format(IReadOnlyDictionary<int, string>? labelsByAddress)
  {
    if (!IsControlTransfer)
    {
      return Mnemonic;
    }

    int destination = Destination!.Value;
    string hex = $"0x{destination:X3}";

    // Bit P9 (0x200, F18InstructionSet.ExtendedArithmeticBit) rides along in a compiled address
    // without being part of memory decoding (see F18Compiler's own ToPhysicalIndex remarks) -- a
    // slot-0 control transfer's destination field is wide enough (10 bits) to carry it whenever the
    // call/jump was compiled under '+cy'. Bit 6 (0x40, F18InstructionSet.MemoryMirrorBit) is not
    // part of decoding either -- it only picks which of the two mirrored copies of a node's 64-word
    // RAM/ROM the address falls in (see that constant's own remarks); a transfer that targets the
    // OTHER copy of the same word from where its label was recorded is still the same word. Both
    // bits are masked out here the same way BuildLabelsByAddress already masks them out of its own
    // keys, or a transfer compiled under Extended Arithmetic Mode, or one that (deliberately or not)
    // names the mirrored address, would never match its own target's label. The raw, unmasked
    // destination is still what is actually encoded and shown in 'hex' above -- only the LOOKUP
    // ignores these two bits, never the displayed value.
    //
    // BUG FIX 2026-09-30, per Stefan directly ("jump 0x1A5 (rlu) in the disassembly is wrong. it
    // should be jump 0x1A5 (rdlu)"): this masking must NEVER be applied to a fixed port/multiport
    // address (F18InstructionSet.NamedMultiportCalls/Constants, 0x105-0x1F5) -- only to a genuine
    // RAM/ROM location-counter address (0x000-0x0FF, or its EAM twin 0x200-0x2FF), where P9/the
    // mirror bit really do just ride along incidentally. Bit 6 happens to also be the "down" bit in
    // the multiport encoding, so masking it out unconditionally collapses e.g. "rdlu" (0x1A5) onto
    // "r-lu" (0x1E5 = 0x1A5 | MemoryMirrorBit) -- two DIFFERENT, real hardware addresses -- as if
    // they were the same RAM/ROM word's two mirrored copies, which they are not: every RAM/ROM (and
    // EAM) address has bit 8 (0x100) clear, while every one of the 15 documented multiport addresses
    // has it set, so that bit alone reliably tells which kind of address this is.
    // See F18Disassembler.MaskLocationCounterBits's own remarks for the shared test.
    int lookupAddress = F18Disassembler.MaskLocationCounterBits(destination);
    string? name = labelsByAddress?.GetValueOrDefault(lookupAddress) ?? PortAddressNames.TryGetName(destination);
    return name is null ? $"{Mnemonic} {hex}" : $"{Mnemonic} {hex} ({name})";
  }
}

/// <summary>
/// One decoded 18-bit F18A instruction word: 1 to 4 slots (fewer than 4 exactly
/// when a control transfer ends the word early -- see <see cref="F18DisassembledSlot"/>).
/// </summary>
public sealed record F18DisassembledWord(int Address, int RawWord, IReadOnlyList<F18DisassembledSlot> Slots)
{
  /// <summary>
  /// True when some slot in this word is '@p' or '!p' -- the two opcodes that
  /// advance P by fetching/storing the word AT P, which is how a compiled
  /// literal ends up inline in the following memory cell. This is a per-word
  /// HEURISTIC, not a certainty: recognizing an inline literal for sure requires
  /// tracing actual control flow from a known entry point (which word runs, and
  /// therefore which word is consumed as data, depends on execution order, not
  /// just static layout), and this disassembler decodes every word
  /// independently, the same way a raw memory dump has no other choice. Treat
  /// the following word's decode as unreliable when this is true.
  /// </summary>
  public bool MayConsumeNextWordAsLiteral => Slots.Any(slot => slot.Opcode is 0x08 or 0x0C);

  public override string ToString() => Format(null);

  /// <summary>Same as <see cref="ToString"/>, but with each control-transfer slot's own destination
  /// annotated via <see cref="F18DisassembledSlot.Format"/> -- see that method's own remarks.</summary>
  public string Format(IReadOnlyDictionary<int, string>? labelsByAddress) =>
      string.Join(" ", Slots.Select(slot => slot.Format(labelsByAddress)));
}

/// <summary>
/// Decodes raw F18A instruction words (RAM or ROM, 18 bits each) back into their
/// slot-packed mnemonics. This is a genuine F18-opcode-space disassembler --
/// distinct from, and not reusable with, <see cref="CvmAssemblyLanguage.DisassemblePage0"/>,
/// which decodes an entirely different, higher-level CVM-interpreter bytecode
/// space out of a running CVM helper node's own RAM contents.
///
/// Every opcode value and bit layout here is taken directly from this project's
/// own ENCODERS in <see cref="F18InstructionSet"/> (TryEncodePackedInstruction,
/// EncodeSlot0Control, TryEncodePackedControl) and from F18Compiler's own
/// control-transfer opcode assignments (jump=0x02, call=0x03, unext=0x04 [a
/// plain, addressless opcode -- it loops within the same word], next=0x05,
/// if=0x06, -if=0x07) -- decoding is simply those encoders run in reverse, never
/// a separate guess at the ISA.
/// </summary>
public static class F18Disassembler
{
  // Canonical mnemonic per plain (non-control-transfer) opcode value. Built
  // explicitly rather than by inverting F18InstructionSet.Opcodes, because
  // several source spellings alias the same opcode there (e.g. "@p"/"@p+",
  // "inv"/"not", "."/"nop", "!p"/"!p+") -- one canonical spelling is picked per
  // value for display.
  private static readonly IReadOnlyDictionary<byte, string> PlainMnemonics = new Dictionary<byte, string>
  {
    [0x00] = "ret",
    [0x01] = "ex",
    [0x04] = "unext",
    [0x08] = "@p",
    [0x09] = "@+",
    [0x0A] = "@b",
    [0x0B] = "@",
    [0x0C] = "!p",
    [0x0D] = "!+",
    [0x0E] = "!b",
    [0x0F] = "!",
    [0x10] = "+*",
    [0x11] = "2*",
    [0x12] = "2/",
    [0x13] = "not",
    [0x14] = "+",
    [0x15] = "and",
    [0x16] = "xor",
    [0x17] = "drop",
    [0x18] = "dup",
    [0x19] = "r>",
    [0x1A] = "over",
    [0x1B] = "a",
    [0x1C] = "nop",
    [0x1D] = ">r",
    [0x1E] = "b!",
    [0x1F] = "a!",
  };

  // Slot-0/1/2-only control-transfer opcodes (DB001 2.3.1, DB013 5.3): each
  // carries an embedded destination-address field in place of the word's
  // remaining slots, so they are never in F18InstructionSet.Opcodes/
  // PlainMnemonics -- see F18Compiler.cs's own CompileExplicitControl call
  // sites (0x02 "jump", 0x03 "call", 0x05 "next", 0x06 "if", 0x07 "-if") for
  // the same 5 values, confirmed against this project's own compiler.
  private static readonly IReadOnlyDictionary<byte, string> ControlMnemonics = new Dictionary<byte, string>
  {
    [0x02] = "jump",
    [0x03] = "call",
    [0x05] = "next",
    [0x06] = "if",
    [0x07] = "-if",
  };

  /// <summary>Decodes one raw 18-bit F18A word at the given address into its 1-4 slots.</summary>
  public static F18DisassembledWord Decode(int address, int rawWord)
  {
    int word = rawWord & F18InstructionSet.WordMask;

    // Only the opcode-bit positions (13-17, 8-12, 3-7) are ever XOR-encoded on
    // the wire -- a control transfer's destination field is always inserted
    // RAW (see TryEncodePackedControl's remarks: "The address field is
    // inserted UNENCODED"). So this XOR-decoded view is only ever used to pull
    // out an opcode value, never an address -- addresses always come from
    // 'word' directly.
    int decodedOpcodeBits = word ^ F18InstructionSet.EncodingXor;

    var slots = new List<F18DisassembledSlot>(4);

    byte slot0 = (byte)((decodedOpcodeBits >> 13) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot0, out string? slot0Mnemonic))
    {
      // Slot 0's field is the full 10 bits (F18InstructionSet.AddressFieldWidth(0)) -- already the
      // whole address, no reconstruction needed (mirrors TryEncodeSlot0Control's own full-address
      // encoding).
      slots.Add(new F18DisassembledSlot(0, slot0, slot0Mnemonic, IsControlTransfer: true, word & 0x3FF));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(0, slot0, PlainMnemonics.GetValueOrDefault(slot0, $"?0x{slot0:X2}"), false, null));

    byte slot1 = (byte)((decodedOpcodeBits >> 8) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot1, out string? slot1Mnemonic))
    {
      int destination1 = ReconstructDestination(address, word, slot: 1, slot0);
      slots.Add(new F18DisassembledSlot(1, slot1, slot1Mnemonic, IsControlTransfer: true, destination1));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(1, slot1, PlainMnemonics.GetValueOrDefault(slot1, $"?0x{slot1:X2}"), false, null));

    byte slot2 = (byte)((decodedOpcodeBits >> 3) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot2, out string? slot2Mnemonic))
    {
      int destination2 = ReconstructDestination(address, word, slot: 2, slot0, slot1);
      slots.Add(new F18DisassembledSlot(2, slot2, slot2Mnemonic, IsControlTransfer: true, destination2));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(2, slot2, PlainMnemonics.GetValueOrDefault(slot2, $"?0x{slot2:X2}"), false, null));

    // Slot 3 can never hold a transfer (DB001 2.3.1: "Slot 3 can never hold a
    // transfer"), and only stores its top 3 bits on the wire -- its low 2 bits
    // are always zero (F18InstructionSet.IsSlot3Compatible), so reconstructing
    // shifts the 3 stored bits back up by 2.
    byte slot3 = (byte)((decodedOpcodeBits & 0x07) << 2);
    slots.Add(new F18DisassembledSlot(3, slot3, PlainMnemonics.GetValueOrDefault(slot3, $"?0x{slot3:X2}"), false, null));

    return new F18DisassembledWord(address, rawWord, slots);
  }

  /// <summary>
  /// Reconstructs a slot 1 or slot 2 control transfer's TRUE absolute destination -- BUG FIX
  /// 2026-09-30, per Stefan's own general requirement ("if the target of a jump is a label in the
  /// current node's RAM or ROM, then the name of the label should be appended in parenthesis"),
  /// which surfaced that this had never been implemented at all: until now, <see cref="Decode"/>
  /// stored a slot 1/2 transfer's raw field bits directly as its <see cref="F18DisassembledSlot.Destination"/>
  /// -- correct only by coincidence, for a target small enough to fit entirely within that field (8
  /// bits for slot 1, a mere 3 bits -- 0..7 -- for slot 2). Per DB001 2.3.1 (see
  /// <see cref="F18InstructionSet.AddressFieldWidth"/>'s own remarks, quoted in
  /// <see cref="F18InstructionSet.ControlFitsSlot"/>): "the stored field replaces the low n bits of
  /// the (incremented) value of P at execution time" -- so the raw field is never the whole address
  /// once a program's own addresses exceed the field's own width, which is the ORDINARY case for a
  /// slot 2 transfer (3 bits) anywhere beyond word 7, and therefore for virtually every slot 2
  /// transfer inside ROM (which starts at 0x080). Reconstructing mirrors
  /// <see cref="F18InstructionSet.ControlFitsSlot"/>'s own compile-time formula run in the decode
  /// direction: <c>(nextP &amp; ~mask) | (rawField &amp; mask)</c>, where <c>nextP</c> is P's own
  /// value at the moment this transfer computes its target -- the address immediately after this
  /// instruction word, PLUS one more for every <c>@p</c>/<c>!p</c> (the two P-advancing opcodes,
  /// same test as <see cref="F18DisassembledWord.MayConsumeNextWordAsLiteral"/>) among this SAME
  /// word's own PRECEDING slots, which have already executed, in slot order, before this transfer
  /// slot runs. This is not the same ambiguity <see cref="F18DisassembledWord.MayConsumeNextWordAsLiteral"/>
  /// warns about (whether the FOLLOWING word is data or code, which truly cannot be known
  /// statically) -- counting `@p`/`!p` occurrences among THIS word's own earlier slots is a fixed,
  /// local fact about this one word, true regardless of run-time control flow. Verified against
  /// every genuine (non-data) slot 1 transfer in Stefan's own node 407 disassembly: the reconstructed
  /// value is byte-for-byte identical to the old raw-field value in every one of those cases (their
  /// own targets' high bits above the field width were already zero), so this is a pure correctness
  /// fix with no change to any previously-correct result.
  /// </summary>
  // FLAGGED, not fixed (adversarial review, 2026-09-30): 'nextP' here is a plain "address + 1 +
  // consumed" with no wraparound, whereas F18Compiler's own reference NextAddress wraps P at a
  // node's mirrored-memory-space boundary (e.g. ROM 0x0FF -> 0x080, not 0x100) using that compile
  // session's own _baseAddress/_memory.Length. Decode (and this method) receive only a bare address
  // and word -- no base/word-count context -- so reproducing that wrap exactly would mean changing
  // Decode's own public signature, which ripples into its OTHER three call sites, two of them in the
  // live Ga144SimulatorEngine (Opcodes.cs/​.cs), not just DisassembleImage's own 64-word-at-a-time
  // callers. Not done here without being asked, since it touches real execution-path code well
  // beyond this bug's own scope. Currently inert for every actual caller in this codebase: all of
  // them decode exactly 64 words starting at baseAddress, and the worst case (address 0x0BF, both
  // preceding slots '@p'/'!p') only reaches nextP=0x0C2 -- still well short of wrapping. Would only
  // matter if Decode is ever run across a full 128-word mirrored image, or exposed more generally --
  // worth revisiting then, not before.
  private static int ReconstructDestination(int address, int word, int slot, byte precedingSlot0, byte? precedingSlot1 = null)
  {
    bool AdvancesP(byte opcode) => opcode is 0x08 or 0x0C; // '@p' or '!p'

    int consumed = (AdvancesP(precedingSlot0) ? 1 : 0) + (precedingSlot1 is { } slot1Opcode && AdvancesP(slot1Opcode) ? 1 : 0);
    int nextP = address + 1 + consumed;

    int width = F18InstructionSet.AddressFieldWidth(slot);
    int mask = (1 << width) - 1;
    return (nextP & ~mask) | (word & mask);
  }

  /// <summary>
  /// Decodes a full memory image (typically 64 RAM or 64 ROM words) into one
  /// <see cref="F18DisassembledWord"/> per address, starting at
  /// <paramref name="baseAddress"/> (0x000 for RAM, 0x080 for ROM). Every word
  /// is decoded independently -- see <see cref="F18DisassembledWord.MayConsumeNextWordAsLiteral"/>
  /// for why an inline literal cannot be told apart from real code with
  /// certainty from a static image alone.
  /// </summary>
  public static IReadOnlyList<F18DisassembledWord> DisassembleImage(IReadOnlyList<int> words, int baseAddress = 0x000)
  {
    ArgumentNullException.ThrowIfNull(words);
    var result = new List<F18DisassembledWord>(words.Count);
    for (int index = 0; index < words.Count; index++)
    {
      result.Add(Decode(baseAddress + index, words[index]));
    }

    return result;
  }

  /// <summary>
  /// Maps each address that a compiled word (colon-definition entry point) or an explicit label
  /// names to that symbol's own name -- the shared lookup a "Label" column/gutter uses next to a
  /// disassembled/decoded word image, and what a control-transfer's own destination is checked
  /// against for the "(&lt;name&gt;)" annotation (<see cref="F18DisassembledSlot.Format"/>).
  /// Originally private to <c>PostMortemNodeDetailViewModel</c> (Core Dump's own RAM/ROM comparison
  /// view); pulled out here, unchanged in its "which name wins" rule, so
  /// <see cref="ViewModels.NodeEditorViewModel"/>'s own RAM/ROM display can share exactly the same
  /// logic rather than re-deriving it.
  ///
  /// <paramref name="symbols"/> is <see cref="F18CompileResult.Symbols"/> -- names this compile
  /// defined directly (a colon-definition or an explicit label in THIS source).
  /// <paramref name="externalSymbols"/> is <see cref="F18CompileResult.ExternalSymbols"/> -- names
  /// this compile only RESOLVED, not defined: a genuine cross-node <c>import</c>, or (for a RAM
  /// compile) this SAME node's own just-compiled ROM dictionary, automatically in scope -- e.g. a
  /// call to a ROM-resident routine like "clc" compiles to a real control transfer whose destination
  /// is only known via <see cref="F18CompileResult.ExternalSymbols"/>, never <see cref="F18CompileResult.Symbols"/>,
  /// so omitting it here left such a call's destination unannotated ("call 0x2D3" instead of
  /// "call 0x2D3 (clc)") even though the name was known all along. Both are merged into one
  /// address-keyed map: a name from <paramref name="symbols"/> always wins a same-address collision
  /// over one from <paramref name="externalSymbols"/> (this node's own current source is the more
  /// specific, relevant name for that spot), and within each, a colon-definition's own
  /// <see cref="F18ExportKind.Word"/> entry point always wins over a plain <see cref="F18ExportKind.Label"/>
  /// at the same address. <see cref="F18ExportKind.Constant"/> is skipped entirely -- its
  /// <see cref="F18ExportedSymbol.Value"/> is a compile-time constant, not an address, so it never
  /// belongs in an address-keyed map.
  ///
  /// <paramref name="ownNodeCoordinate"/> (Stefan's own request, 2026-09-21: the node editor's own
  /// RAM/ROM label display should show only labels that belong to THIS node, never a name merely
  /// imported from some OTHER node) restricts which <paramref name="externalSymbols"/> entries are
  /// even considered: when supplied, only those whose own <see cref="F18ExportedSymbol.NodeCoordinate"/>
  /// equals it are kept. This does NOT mean "only genuine imports are filtered and everything else
  /// stays" as a side effect of some other test -- it is exactly that test: this SAME node's own
  /// just-compiled ROM dictionary and the NamedMultiportCalls/CallableRomWords built-ins are all
  /// seeded into <paramref name="externalSymbols"/> under THIS node's own coordinate (see
  /// F18Compiler's own Reset), so "clc" and friends still show; only a name whose
  /// <see cref="F18ExportedSymbol.NodeCoordinate"/> genuinely differs -- i.e. an actual
  /// <c>NNN import</c> -- is dropped. Left null (every other caller, e.g. the Core Dump / Post-Mortem
  /// comparison view), no filtering happens at all, preserving the earlier behavior of showing every
  /// resolved name regardless of which node it came from.
  ///
  /// Every recorded address is also masked against <see cref="F18InstructionSet.ExtendedArithmeticBit"/>
  /// (bit P9, 0x200) before being used as this map's key (Stefan's own request, 2026-09-21) -- P9 is
  /// not part of memory decoding (see F18Compiler's own ToPhysicalIndex remarks) but rides along in
  /// the location counter, so a label defined while Extended Arithmetic Mode was active (inside a
  /// '+cy' section) would otherwise be recorded under an address 0x200 higher than the actual RAM/ROM
  /// cell it names, and never match either that cell's own address or a control transfer's own
  /// (equally P9-tainted) destination field -- see <see cref="F18DisassembledSlot.Format"/>'s matching
  /// mask on the LOOKUP side.
  ///
  /// Also masked against <see cref="F18InstructionSet.MemoryMirrorBit"/> (bit 6, 0x40 -- Stefan's own
  /// request, 2026-09-21): a node's RAM and ROM each mirror their 64 physical words once (RAM
  /// x000-x03F at x040-x07F, ROM x080-x0BF at x0C0-x0FF -- see that constant's own remarks), so a
  /// label whose own address happens to fall in the mirrored half, or a control transfer that
  /// (deliberately, e.g. a wraparound tail call, or not) names the mirrored copy of the same word,
  /// must still be recognized as the exact same word as its un-mirrored twin.
  /// </summary>
  public static Dictionary<int, string> BuildLabelsByAddress(
      IReadOnlyDictionary<string, F18ExportedSymbol>? symbols,
      IReadOnlyDictionary<string, F18ExportedSymbol>? externalSymbols = null,
      int? ownNodeCoordinate = null)
  {
    var labelsByAddress = new Dictionary<int, string>();

    IReadOnlyDictionary<string, F18ExportedSymbol>? ownScopeExternalSymbols = ownNodeCoordinate is null
        ? externalSymbols
        : externalSymbols?.Values
            .Where(symbol => symbol.NodeCoordinate == ownNodeCoordinate.Value)
            .ToDictionary(symbol => symbol.Name, symbol => symbol, StringComparer.OrdinalIgnoreCase);

    // Label pass: TryAdd (first writer wins) -- externalSymbols first, symbols second, so a local
    // Label always outranks a same-address Label coming from an import/ROM-seeded name.
    AddByKind(ownScopeExternalSymbols, F18ExportKind.Label, labelsByAddress, overwrite: false);
    AddByKind(symbols, F18ExportKind.Label, labelsByAddress, overwrite: false);

    // Word pass: plain assignment (last writer wins) -- same order, so a local Word has the final
    // say over both an external Word AND any Label already recorded above.
    AddByKind(ownScopeExternalSymbols, F18ExportKind.Word, labelsByAddress, overwrite: true);
    AddByKind(symbols, F18ExportKind.Word, labelsByAddress, overwrite: true);

    return labelsByAddress;
  }

  private static void AddByKind(
      IReadOnlyDictionary<string, F18ExportedSymbol>? source,
      F18ExportKind kind,
      Dictionary<int, string> target,
      bool overwrite)
  {
    if (source is null)
    {
      return;
    }

    foreach (F18ExportedSymbol symbol in source.Values)
    {
      if (symbol.Kind != kind)
      {
        continue;
      }

      // Masked against P9 and the RAM/ROM mirror bit -- see this method's own caller,
      // BuildLabelsByAddress, for why, and MaskLocationCounterBits's own remarks for why this is
      // conditional on the address NOT being a fixed port/multiport address (2026-09-30 bug fix).
      int address = MaskLocationCounterBits(symbol.Value);
      if (overwrite)
      {
        target[address] = symbol.Name;
      }
      else
      {
        target.TryAdd(address, symbol.Name);
      }
    }
  }

  /// <summary>
  /// Shared by <see cref="AddByKind"/> (when building <see cref="BuildLabelsByAddress"/>'s own
  /// address-keyed map) and <see cref="F18DisassembledSlot.Format"/> (when looking a control
  /// transfer's own destination up in that map) -- the two MUST agree on exactly which bits of an
  /// address are insignificant, or a genuine RAM/ROM label would never match its own target.
  ///
  /// <b>BUG FIX 2026-09-30</b>, per Stefan directly: a raw, unconditional
  /// <c>value &amp; ~(ExtendedArithmeticBit | MemoryMirrorBit)</c> is only correct for a genuine
  /// RAM/ROM location-counter address, where P9 (bit 9, 0x200) and the RAM/ROM mirror bit (bit 6,
  /// 0x40) really do just ride along incidentally, never changing which physical word is meant (see
  /// <see cref="F18InstructionSet.ExtendedArithmeticBit"/>/<see cref="F18InstructionSet.MemoryMirrorBit"/>'s
  /// own remarks). It is WRONG for one of the 15 documented fixed multiport addresses
  /// (<see cref="F18InstructionSet.NamedMultiportCalls"/>, 0x105-0x1F5) or the 4 single-direction
  /// addresses that are a subset of them (0x105-0x1F5 as well) -- bit 6 there is simply the "down"
  /// direction flag, an ordinary, MEANINGFUL part of a real, distinct hardware address, not a mirror
  /// indicator. Masking it out unconditionally silently collapsed e.g. "rdlu" (0x1A5) onto "r-lu"
  /// (0x1E5 = 0x1A5 | 0x40) as if they were the same RAM/ROM word's two mirrored halves, when they
  /// are two entirely different multiport addresses -- observed on real hardware as "jump 0x1A5
  /// (rlu)" where the correct name is "rdlu" (whichever of the two colliding names was seeded/
  /// iterated last simply won the address-keyed dictionary's last-writer-wins rule).
  ///
  /// The fix relies on a fact already established elsewhere in this file/<see cref="F18InstructionSet"/>,
  /// not a new assumption: every genuine RAM/ROM (or EAM) address has bit 8 (0x100) clear --
  /// RAM/ROM's own valid range including both mirrors is 0x000-0x0FF (<see cref="F18InstructionSet.MemoryMirrorBit"/>'s
  /// own remarks), and EAM is that same range OR'd with 0x200 (bit 9), never bit 8 -- while every
  /// one of the 15 multiport addresses and their 4 cardinal-direction subset falls in 0x105-0x1F5,
  /// where bit 8 is always set. So bit 8 alone reliably distinguishes "a compiled label/word address
  /// eligible for P9/mirror masking" from "a fixed hardware port address that must be matched
  /// exactly, bit 6 included."
  /// </summary>
  // internal, not private: F18DisassembledSlot.Format (a separate top-level type in this same file/
  // namespace) calls this too, to keep the lookup side and this map-building side testing the exact
  // same condition -- see this method's own remarks for why they must agree.
  internal static int MaskLocationCounterBits(int address)
  {
    const int PortAddressBit = 0x100;
    if ((address & PortAddressBit) != 0)
    {
      return address;
    }

    return address & ~(F18InstructionSet.ExtendedArithmeticBit | F18InstructionSet.MemoryMirrorBit);
  }

  /// <summary>
  /// Appends a disassembled word's OWN local label after its own address, e.g. <c>"014 (brk)"</c> --
  /// added 2026-09-30 per Stefan directly: "change that the disassembler also displays local labels
  /// after the address." Generalizes the "(&lt;name&gt;)" convention <see cref="F18DisassembledSlot.Format"/>
  /// already uses for a control transfer's own embedded DESTINATION address to the address every
  /// disassembled word is itself listed under -- the row's own address, not a jump/call's target.
  /// Additive, not a replacement: a caller's separate "Label" column (<see cref="BuildLabelsByAddress"/>'s
  /// own map, looked up the same way) is unaffected and still shows the plain name on its own; this
  /// only changes what the ADDRESS text itself reads.
  ///
  /// <paramref name="addressHex"/> is the caller's own already-formatted hex digits (each RAM/ROM
  /// disassembly view has its own digit-count/prefix convention -- e.g. 3 digits, no "0x" -- and this
  /// does not second-guess or reformat that) -- <paramref name="address"/> is the same address as a
  /// plain <see langword="int"/>, used only to look itself up in <paramref name="labelsByAddress"/> via
  /// the same <see cref="MaskLocationCounterBits"/> test <see cref="AddByKind"/> already used to build
  /// that map's own keys, so a masked/canonical match is never missed here either.
  /// </summary>
  public static string FormatAddressWithLabel(string addressHex, int address, IReadOnlyDictionary<int, string>? labelsByAddress)
  {
    string? label = labelsByAddress?.GetValueOrDefault(MaskLocationCounterBits(address));
    return label is null ? addressHex : $"{addressHex} ({label})";
  }
}