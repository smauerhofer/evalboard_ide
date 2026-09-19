using System.Windows;
using System.Windows.Media;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>One row of a stack in the post-mortem node window -- <see cref="Label"/> is "T"/"S"/"R"
/// for the top-of-stack cell(s), "[n]" otherwise, and <see cref="ValueText"/> is the raw value with
/// symbolic port-name display applied (<see cref="PortAddressNames"/>).</summary>
public sealed record PostMortemStackRow(string Label, string ValueText);

/// <summary>One decoded word row (RAM or ROM) in the post-mortem node window, column order matching
/// <see cref="Views.PostMortemNodeWindow"/>'s own left-to-right layout: <see cref="AddressHex"/> (the
/// 10-bit address, 3 hex digits, no "0x" prefix), <see cref="Label"/> (the name this project's
/// currently compiled source binds to this address -- a colon-definition entry point or an explicit
/// label -- or null when no such symbol lands exactly here), <see cref="HexText"/> (the raw on-chip
/// 18-bit word content, 5 hex digits, no "0x" prefix), <see cref="Disassembly"/> (the on-chip word's
/// decoded mnemonics), then the same pair for the compiled comparison word: <see cref="CompiledHexText"/>
/// and <see cref="CompiledDisassembly"/>. The compiled-side values, and <see cref="IsMismatch"/>, are
/// only meaningful when this project's currently compiled source for the node was available for
/// comparison -- otherwise they're left null/false. <see cref="MayBeLiteral"/> flags a word that might
/// be an inline literal rather than real code (see
/// <see cref="F18DisassembledWord.MayConsumeNextWordAsLiteral"/> on the PRECEDING word).</summary>
public sealed record PostMortemWordRow(
    string AddressHex,
    string? Label,
    string HexText,
    string Disassembly,
    bool MayBeLiteral,
    bool IsMismatch,
    string? CompiledHexText,
    string? CompiledDisassembly);

/// <summary>
/// Drives one non-modal "post-mortem node" window (<see cref="Views.PostMortemNodeWindow"/>):
/// registers, both stacks (with symbolic port-name display for values that match a known port
/// address, per DB013 SS7.1.4), and a RAM/ROM disassembly with differences from this project's
/// currently compiled source highlighted -- <see cref="HasCompiledComparison"/> is false, and every
/// row's <see cref="PostMortemWordRow.CompiledDisassembly"/>/<see cref="PostMortemWordRow.IsMismatch"/>
/// left at their defaults, when no live project chip was available to compile against (e.g. a
/// snapshot imported without one).
/// </summary>
public sealed class PostMortemNodeDetailViewModel
{
  public PostMortemNodeDetailViewModel(
      PostMortemNodeSnapshot snapshot,
      string projectDefaultColor,
      IReadOnlyList<int>? compiledRamWords,
      IReadOnlyList<int>? compiledRomWords,
      IReadOnlyDictionary<string, F18ExportedSymbol>? compiledRamSymbols = null,
      IReadOnlyDictionary<string, F18ExportedSymbol>? compiledRomSymbols = null)
  {
    Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    string effectiveColor = string.IsNullOrWhiteSpace(snapshot.Color) ? projectDefaultColor : snapshot.Color;
    Brush = NodeColorOption.BrushFromHex(effectiveColor);

    AText = PortAddressNames.Format(snapshot.A);
    IoText = snapshot.Io.ToString("X5");

    ParameterStackRows = BuildStackRows(snapshot.ParameterStack, topLabels: ["T", "S"]);
    ReturnStackRows = BuildStackRows(snapshot.ReturnStack, topLabels: ["R"]);

    RamRows = BuildWordRows(snapshot.Coordinate, snapshot.Ram, baseAddress: 0x000, compiledRamWords, compiledRamSymbols);
    RomRows = BuildWordRows(snapshot.Coordinate, snapshot.Rom, baseAddress: 0x080, compiledRomWords, compiledRomSymbols);

    HasCompiledComparison = compiledRamWords is not null || compiledRomWords is not null;
  }

  public PostMortemNodeSnapshot Snapshot { get; }
  public Brush Brush { get; }
  public string CoordinateText => $"{Snapshot.Coordinate:000}";
  public string AText { get; }
  public string IoText { get; }
  public bool HasError => Snapshot.Error is not null;
  public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;
  public bool HasCompiledComparison { get; }

  public IReadOnlyList<PostMortemStackRow> ParameterStackRows { get; }
  public IReadOnlyList<PostMortemStackRow> ReturnStackRows { get; }
  public IReadOnlyList<PostMortemWordRow> RamRows { get; }
  public IReadOnlyList<PostMortemWordRow> RomRows { get; }

  /// <summary>
  /// Plain-text rendering of exactly what the "Registers"/"Parameter stack"/"Return stack" panel at
  /// the top of <see cref="Views.PostMortemNodeWindow"/> shows -- same values, same top-first stack
  /// order, same symbolic port-name formatting (<see cref="PortAddressNames"/>) already baked into
  /// <see cref="AText"/>/<see cref="IoText"/>/<see cref="PostMortemStackRow.ValueText"/>. This is the
  /// "Copy" button's own source of truth, so the clipboard always mirrors what is on screen at the
  /// moment it is clicked rather than re-deriving it from <see cref="Snapshot"/> a second, possibly
  /// divergent way.
  /// </summary>
  public string BuildClipboardText()
  {
    var lines = new List<string>
    {
      $"Node {CoordinateText}",
      $"A:  {AText}",
      $"IO: {IoText}",
      string.Empty,
      "Parameter stack (top first):"
    };

    foreach (PostMortemStackRow row in ParameterStackRows)
    {
      lines.Add($"{row.Label}: {row.ValueText}");
    }

    lines.Add(string.Empty);
    lines.Add("Return stack (top first):");
    foreach (PostMortemStackRow row in ReturnStackRows)
    {
      lines.Add($"{row.Label}: {row.ValueText}");
    }

    return string.Join(Environment.NewLine, lines);
  }

  // Storage is bottom-to-top (see KrakenLiveController.ReadParameterStackAsync/ReadReturnStackAsync's
  // own remarks), so labels are assigned counting IN from the end, then the display order is reversed
  // to show the top of stack first -- the order someone reading a stack dump actually wants.
  private static IReadOnlyList<PostMortemStackRow> BuildStackRows(IReadOnlyList<int> bottomToTop, IReadOnlyList<string> topLabels)
  {
    var rows = new List<PostMortemStackRow>(bottomToTop.Count);
    for (int index = 0; index < bottomToTop.Count; index++)
    {
      int fromTop = bottomToTop.Count - 1 - index;
      string label = fromTop < topLabels.Count ? topLabels[fromTop] : $"[{fromTop}]";
      rows.Add(new PostMortemStackRow(label, PortAddressNames.Format(bottomToTop[index])));
    }

    rows.Reverse();
    return rows;
  }

  private static IReadOnlyList<PostMortemWordRow> BuildWordRows(
      int coordinate,
      IReadOnlyList<int> words,
      int baseAddress,
      IReadOnlyList<int>? compiledWords,
      IReadOnlyDictionary<string, F18ExportedSymbol>? compiledSymbols)
  {
    IReadOnlyList<F18DisassembledWord> decoded = F18Disassembler.DisassembleImage(words, baseAddress);

    HashSet<int> mismatchedAddresses = compiledWords is null
        ? []
        : [.. RomComparison.Compare(coordinate, compiledWords, words, baseAddress).Mismatches.Select(mismatch => mismatch.Address)];

    Dictionary<int, int> compiledByAddress = [];
    if (compiledWords is not null)
    {
      for (int index = 0; index < compiledWords.Count; index++)
      {
        compiledByAddress[baseAddress + index] = compiledWords[index];
      }
    }

    Dictionary<int, string> labelsByAddress = BuildLabelMap(compiledSymbols);

    var rows = new List<PostMortemWordRow>(decoded.Count);
    // MayConsumeNextWordAsLiteral describes the NEXT word (the one a '@p'/'!p' in THIS word would
    // fetch/store), so the flag on a row is carried over from the PRECEDING word's decode, not its
    // own -- see F18DisassembledWord's own remarks.
    bool previousMayConsumeAsLiteral = false;
    foreach (F18DisassembledWord word in decoded)
    {
      bool hasCompiledWord = compiledByAddress.TryGetValue(word.Address, out int compiledWord);
      string? compiledHexText = hasCompiledWord ? compiledWord.ToString("X5") : null;
      string? compiledDisassembly = hasCompiledWord ? F18Disassembler.Decode(word.Address, compiledWord).ToString() : null;
      string? label = labelsByAddress.TryGetValue(word.Address, out string? labelName) ? labelName : null;

      rows.Add(new PostMortemWordRow(
          word.Address.ToString("X3"),
          label,
          word.RawWord.ToString("X5"),
          word.ToString(),
          previousMayConsumeAsLiteral,
          mismatchedAddresses.Contains(word.Address),
          compiledHexText,
          compiledDisassembly));

      previousMayConsumeAsLiteral = word.MayConsumeNextWordAsLiteral;
    }

    return rows;
  }

  // A colon-definition entry point (F18ExportKind.Word) and a plain in-body label
  // (F18ExportKind.Label) can both name an address; Word always wins when both land on the same
  // address (added second, unconditionally overwriting), since it's the more meaningful name for
  // that spot. F18ExportKind.Constant is skipped entirely -- its Value is a compile-time constant,
  // not an address, so it never belongs in an address-keyed map.
  private static Dictionary<int, string> BuildLabelMap(IReadOnlyDictionary<string, F18ExportedSymbol>? symbols)
  {
    var labelsByAddress = new Dictionary<int, string>();
    if (symbols is null)
    {
      return labelsByAddress;
    }

    foreach (F18ExportedSymbol symbol in symbols.Values)
    {
      if (symbol.Kind == F18ExportKind.Label)
      {
        labelsByAddress.TryAdd(symbol.Value, symbol.Name);
      }
    }

    foreach (F18ExportedSymbol symbol in symbols.Values)
    {
      if (symbol.Kind == F18ExportKind.Word)
      {
        labelsByAddress[symbol.Value] = symbol.Name;
      }
    }

    return labelsByAddress;
  }
}