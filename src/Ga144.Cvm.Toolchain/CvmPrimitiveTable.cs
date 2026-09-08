using System.Globalization;

namespace Ga144.Cvm.Toolchain;

/// <summary>
/// A "primitive table" (.gaprim): a map from a built-in CVM instruction mnemonic (<c>nop</c>,
/// <c>pushlit</c>, <c>push</c>, <c>pop</c>, <c>ret</c>, node 506's <c>leave</c>, node 408's comparison
/// ops, and so on -- every mnemonic <see cref="CvmAssembler"/> leaves as an
/// <see cref="CvmSymbolBinding.External"/> symbol with a <see cref="CvmRelocationType.CvmOpcode"/>
/// relocation, see that assembler's own remarks) to its own COMPLETE, FINAL opcode word -- tag and
/// node-resolved address already combined -- exactly the word <see cref="CvmLinker"/> should write into
/// that mnemonic's every occurrence once linked.
///
/// <b>CORRECTED (2026-09-08): an entry is the whole final word, not a bare node address.</b> An earlier
/// version of this class stored only a 15-bit node ADDRESS per mnemonic, on the assumption every
/// primitive resolves via the single fixed <c>0x8000 | address</c> convention <see cref="CvmLinker"/>
/// then re-applied. That assumption does not hold for CVM2: per <see cref="CvmInstructionSet"/>'s own
/// remarks, different primitive families tag completely differently depending on which node implements
/// them (node 507's own local-execute family tags at <c>0x8800</c>, node 506's <c>leave</c>/frame ops at
/// <c>0x9xxx</c>, node 508's <c>ldg</c>/<c>stg</c> at <c>0xA000</c>, node 509's unary-arithmetic family
/// at <c>0xB000</c>, node 407's long-call family at <c>0xC000</c>, node 406's binary-arithmetic family at
/// <c>0xE000</c>/<c>0xE400</c>, node 408's comparison family at <c>0xF000</c>/<c>0xF400</c>, and so on --
/// see the IDE project's own <c>Ga144.Evb.Ide.Services.CvmAssemblyLanguage</c> for where each tag is
/// actually derived). There is no single tag bit this class -- or <see cref="CvmLinker"/> -- could apply
/// uniformly, so a primitive-table entry has to already BE the finished word: whatever produces this
/// table (see below) is the one thing that actually knows which node, and therefore which tag, backs a
/// given mnemonic; <see cref="CvmLinker"/> itself stays completely tag-agnostic and just writes the
/// looked-up value in, masked to 16 bits.
///
/// <b>Where this data comes from.</b> The primary source today is
/// <c>Ga144.Evb.Ide.Services.CvmPrimitiveTableExporter</c>: given the specific GA144 chip project a C
/// project names as its own "chip project" (<c>CProjectMetadata.ChipProjectId</c>/<c>ChipProjectRole</c>),
/// it compiles that chip's own current CVM2 interpreter node mesh purely in software (the same nodes,
/// the same way, <c>Ga144.Evb.Ide.ViewModels.CvmDebuggerViewModel.CompileStandaloneCvmNodes</c> already
/// does for the CVM Debugger) and reads each mnemonic's resolved word straight out of
/// <c>CvmAssemblyLanguage.BuildEncodeTable</c> -- the SAME live computation the debugger's own
/// assembler/disassembler already trusts, so there is exactly one place in this whole project that knows
/// "which node backs which mnemonic, and at what tag," and everything else (this table, <c>galink</c>,
/// the C project's own Build) only ever reads its answer. This deliberately means a primitive table is
/// only ever as fresh as whatever chip project/node source it was built against -- there is still no
/// staleness stamp (see below) -- but it also means adding, renaming, or repointing a CVM2 mnemonic
/// (an ongoing, frequent thing per <see cref="CvmInstructionSet"/>'s own revision history) needs no
/// change here or in <see cref="CvmLinker"/> at all: the next export simply reflects it.
///
/// A hand-written <c>.gaprim</c> file (see the format below) still works too -- <see cref="Parse"/>
/// doesn't care where a table came from -- useful for exercising <c>galink</c> as a stand-alone CLI tool
/// without the IDE, or against a fixed, known set of opcodes for testing.
///
/// <b>File format</b> -- a plain text file, one entry per line, deliberately as simple as <c>.casm</c>
/// source itself rather than a new binary GAFF chunk kind (this is a small, human-authorable/
/// diffable snapshot, not a program):
/// <code>
/// ; comment (a line starting with ';' or '#', or blank, is ignored)
/// nop = 0x8801
/// leave = 0x9037
/// ldg = 0xA03A
/// </code>
/// A name may appear only once; a duplicate is a load error naming both line numbers. Each value must
/// fit a 16-bit CVM word (<see cref="CvmWordCodec.WordMask"/>) -- the FULL range, unlike a plain
/// <c>call</c> target -- since it is the finished, already-tagged word, not a node-local address.
///
/// <b>Staleness detection -- NOT YET IMPLEMENTED</b>: the design doc's own plan is for a future export to
/// stamp a build identifier into this table so a stale one (the node recompiled since it was exported)
/// can at least be warned about; no such stamp exists in this format yet, and nothing here checks for
/// one -- today, freshness is only as good as when <c>CvmPrimitiveTableExporter</c> was last run, which
/// happens automatically at the start of every Build that has a chip project configured.
/// </summary>
public sealed class CvmPrimitiveTable
{
  public static readonly CvmPrimitiveTable Empty = new();

  public IReadOnlyDictionary<string, int> Entries { get; private init; } = new Dictionary<string, int>(StringComparer.Ordinal);

  /// <summary>Builds a table directly from already-resolved (mnemonic -&gt; complete final opcode word) pairs -- what <c>CvmPrimitiveTableExporter</c> uses instead of round-tripping through <see cref="Parse"/>'s own text format. Every value is masked to 16 bits, so a caller need not pre-mask.</summary>
  public static CvmPrimitiveTable FromEntries(IReadOnlyDictionary<string, int> entries)
  {
    var masked = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach ((string name, int value) in entries)
    {
      masked[name] = value & CvmWordCodec.WordMask;
    }

    return new CvmPrimitiveTable { Entries = masked };
  }

  public bool TryGetOpcode(string mnemonic, out int opcode) => Entries.TryGetValue(mnemonic, out opcode);

  /// <summary>Merges this table with <paramref name="other"/> -- entries in <paramref name="other"/> win on a name collision, so later <c>--primitives</c> files on a command line can override earlier ones, same convention as a traditional linker's search-path ordering.</summary>
  public CvmPrimitiveTable MergedWith(CvmPrimitiveTable other)
  {
    var merged = new Dictionary<string, int>(Entries, StringComparer.Ordinal);
    foreach ((string name, int opcode) in other.Entries)
    {
      merged[name] = opcode;
    }

    return new CvmPrimitiveTable { Entries = merged };
  }

  public static (CvmPrimitiveTable? Table, IReadOnlyList<string> Errors) Parse(string text)
  {
    var errors = new List<string>();
    var entries = new Dictionary<string, int>(StringComparer.Ordinal);
    var definedAtLine = new Dictionary<string, int>(StringComparer.Ordinal);

    string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    for (int index = 0; index < lines.Length; index++)
    {
      int lineNumber = index + 1;
      string line = lines[index].Trim();
      if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
      {
        continue;
      }

      int equals = line.IndexOf('=');
      if (equals < 0)
      {
        errors.Add($"line {lineNumber}: expected \"name = opcode\", got \"{line}\".");
        continue;
      }

      string name = line[..equals].Trim();
      string opcodeText = line[(equals + 1)..].Trim();
      if (name.Length == 0)
      {
        errors.Add($"line {lineNumber}: missing a mnemonic name before \"=\".");
        continue;
      }

      if (!TryParseOpcode(opcodeText, out int opcode))
      {
        errors.Add($"line {lineNumber}: \"{opcodeText}\" is not a valid opcode word (decimal or 0x-prefixed hex).");
        continue;
      }

      if ((uint)opcode > (uint)CvmWordCodec.WordMask)
      {
        errors.Add($"line {lineNumber}: \"{name}\"'s value 0x{opcode:X} does not fit in a 16-bit CVM word (0x0000-0x{CvmWordCodec.WordMask:X}).");
        continue;
      }

      if (definedAtLine.TryGetValue(name, out int firstLine))
      {
        errors.Add($"line {lineNumber}: \"{name}\" was already defined on line {firstLine}.");
        continue;
      }

      definedAtLine[name] = lineNumber;
      entries[name] = opcode;
    }

    return errors.Count > 0 ? (null, errors) : (new CvmPrimitiveTable { Entries = entries }, errors);
  }

  private static bool TryParseOpcode(string text, out int value)
  {
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
  }
}