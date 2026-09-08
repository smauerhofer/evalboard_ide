using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Turns a live GA144 chip's own CVM2 interpreter node mesh into a <see cref="CvmPrimitiveTable"/>
/// <see cref="Ga144.Cvm.Toolchain.CvmLinker"/> can link a Program-kind C project's assembled objects
/// against -- the missing piece that used to leave <c>galink</c> fully implemented but never actually
/// callable from a real Build, since nothing could yet supply it a primitive table without a
/// hand-written ".gaprim" file.
///
/// Added 2026-09-08, wiring galink into <see cref="Ga144.Evb.Ide.ViewModels.CProjectViewModel.Build"/>.
/// Deliberately mirrors <see cref="Ga144.Evb.Ide.ViewModels.CvmDebuggerViewModel.CompileStandaloneCvmNodes"/>
/// almost exactly -- same node list (<see cref="CvmNodeMesh.StandaloneCoordinates"/>), same
/// graceful-omission-of-a-broken-node behavior, same underlying <see cref="F18NodeCompilationService"/> --
/// because it needs to answer exactly the same question ("what does mnemonic X resolve to against THIS
/// chip's own current CVM2 mesh") that the CVM Debugger's own standalone assembler already answers, just
/// packaged as a <see cref="CvmPrimitiveTable"/> a Build step can hand to <c>galink</c> instead of as an
/// in-memory encode table an interactive assembler consults directly.
///
/// <b>Per Stefan (2026-09-08): "the exact instruction set is still a moving target. use what is
/// available now in CVM2, and make changes when new instructions become available."</b> This exporter
/// therefore hard-codes no mnemonic-to-opcode table of its own at all -- every value it produces comes
/// straight out of <see cref="CvmAssemblyLanguage.BuildEncodeTable"/>, the SAME live computation the CVM
/// Debugger's own assembler/disassembler trusts, driven by whatever CVM2 node sources happen to be
/// saved on the chip right now. Adding, renaming, or repointing a mnemonic needs no change here --
/// only <see cref="CvmNodeMesh.StandaloneCoordinates"/> needs a new entry if an entirely new NODE joins
/// the mesh (an even rarer event than a mnemonic changing within an existing node).
///
/// <b>What gets exported, and why not everything in the encode table does:</b> only entries with
/// <c>OperandIsEmbedded == false</c> -- i.e. <see cref="CvmInstructionSet.CvmOperandEncoding.None"/> and
/// <see cref="CvmInstructionSet.CvmOperandEncoding.TrailingWord"/> mnemonics -- are the ones
/// <see cref="CvmAssembler"/> actually leaves as an external symbol with a
/// <see cref="CvmRelocationType.CvmOpcode"/> relocation for a linker to resolve (see that assembler's
/// own remarks); everything else (<c>EmbeddedAddress</c>, <c>EmbeddedSignedValue</c>,
/// <c>EmbeddedUnsignedValue</c>) assembles to a complete, self-describing word with no relocation at
/// all, so a primitive-table entry for one would never be looked up. Node 511's four
/// <c>NodeResolvedEmbeddedValue</c> mnemonics (<c>rld</c>/<c>rst</c>/<c>rpop</c>/<c>rpush</c>) are
/// excluded the same way (they also have <c>OperandIsEmbedded == true</c>) -- consistent with
/// <see cref="CvmAssembler"/> itself refusing to assemble them at all today (see its own remarks).
/// </summary>
public static class CvmPrimitiveTableExporter
{
  public sealed class Result
  {
    public required bool Success { get; init; }
    public CvmPrimitiveTable Table { get; init; } = CvmPrimitiveTable.Empty;
    public required IReadOnlyList<string> Messages { get; init; }
  }

  /// <summary>
  /// Compiles <paramref name="chip"/>'s own current CVM2 node mesh (<see cref="CvmNodeMesh.StandaloneCoordinates"/>)
  /// purely in software and derives a primitive table from the result. A node that fails to compile is
  /// skipped, not fatal -- the export only fails outright if EVERY node in the mesh fails, the same
  /// graceful "some mnemonics just won't resolve" degradation <see cref="CvmAssemblyLanguage"/> already
  /// applies for a node missing from its own encode table for any other reason -- so a program that
  /// never touches a mnemonic belonging to one currently-broken node can still link cleanly.
  /// </summary>
  public static Result Export(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition>? userMacros = null)
  {
    var compilationService = new F18NodeCompilationService(chip, romLibrary, userMacros);
    var compiledRam = new Dictionary<int, F18CompileResult>();
    var messages = new List<string>();

    foreach (int coordinate in CvmNodeMesh.StandaloneCoordinates)
    {
      F18NodeCompilationResult compiled = compilationService.CompileNode(coordinate);
      if (!compiled.Ram.Success)
      {
        messages.Add($"node {coordinate:000}'s RAM source does not currently compile -- primitives it implements will not resolve.");
        continue;
      }

      compiledRam[coordinate] = compiled.Ram;
    }

    if (compiledRam.Count == 0)
    {
      messages.Add("no node in the CVM2 mesh compiled successfully -- no primitive table could be exported.");
      return new Result { Success = false, Table = CvmPrimitiveTable.Empty, Messages = messages };
    }

    var encodeTable = CvmAssemblyLanguage.BuildEncodeTable(compiledRam);
    var entries = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (KeyValuePair<string, (int Opcode, int WordLength, bool HasOperand, bool OperandIsEmbedded, int EmbeddedValueMask)> pair in encodeTable)
    {
      if (pair.Value.OperandIsEmbedded)
      {
        // Node 511's rld/rst/rpop/rpush -- not something CvmRelocationType.CvmOpcode can express (see
        // this class's own remarks); CvmAssembler itself never emits a relocation asking for one.
        continue;
      }

      entries[pair.Key] = pair.Value.Opcode;
    }

    var table = CvmPrimitiveTable.FromEntries(entries);
    messages.Add($"exported {entries.Count} primitive(s) from {compiledRam.Count} compiled node(s).");
    return new Result { Success = true, Table = table, Messages = messages };
  }
}
