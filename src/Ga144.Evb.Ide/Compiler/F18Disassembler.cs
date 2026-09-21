using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Compiler;

/// <summary>
/// One decoded slot inside an F18A instruction word: either a plain 5-bit opcode
/// (<see cref="IsControlTransfer"/> false), or a slot-0/1/2 control-transfer
/// opcode (jump/call/next/if/-if) together with the destination address packed
/// into the word's remaining low bits (DB001 2.3.1) -- a control transfer, when
/// present, is always the LAST slot actually present in the word: no slot after
/// it exists, because its address field occupies the bits later slots would
/// otherwise use.
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
    int lookupAddress = destination & ~(F18InstructionSet.ExtendedArithmeticBit | F18InstructionSet.MemoryMirrorBit);
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
      slots.Add(new F18DisassembledSlot(0, slot0, slot0Mnemonic, IsControlTransfer: true, word & 0x3FF));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(0, slot0, PlainMnemonics.GetValueOrDefault(slot0, $"?0x{slot0:X2}"), false, null));

    byte slot1 = (byte)((decodedOpcodeBits >> 8) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot1, out string? slot1Mnemonic))
    {
      slots.Add(new F18DisassembledSlot(1, slot1, slot1Mnemonic, IsControlTransfer: true, word & 0xFF));
      return new F18DisassembledWord(address, rawWord, slots);
    }

    slots.Add(new F18DisassembledSlot(1, slot1, PlainMnemonics.GetValueOrDefault(slot1, $"?0x{slot1:X2}"), false, null));

    byte slot2 = (byte)((decodedOpcodeBits >> 3) & 0x1F);
    if (ControlMnemonics.TryGetValue(slot2, out string? slot2Mnemonic))
    {
      slots.Add(new F18DisassembledSlot(2, slot2, slot2Mnemonic, IsControlTransfer: true, word & 0x07));
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
      // BuildLabelsByAddress, for why.
      int address = symbol.Value & ~(F18InstructionSet.ExtendedArithmeticBit | F18InstructionSet.MemoryMirrorBit);
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
}