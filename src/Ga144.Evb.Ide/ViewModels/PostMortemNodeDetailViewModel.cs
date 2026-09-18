using System.Windows;
using System.Windows.Media;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>One row of a stack in the post-mortem node window -- <see cref="Label"/> is "T"/"S"/"R"
/// for the top-of-stack cell(s), "[n]" otherwise, and <see cref="ValueText"/> is the raw value with
/// symbolic port-name display applied (<see cref="PortAddressNames"/>).</summary>
public sealed record PostMortemStackRow(string Label, string ValueText);

/// <summary>One decoded word row (RAM or ROM) in the post-mortem node window.
/// <see cref="MayBeLiteral"/> flags a word that might be an inline literal rather than real code
/// (see <see cref="F18DisassembledWord.MayConsumeNextWordAsLiteral"/> on the PRECEDING word).
/// <see cref="IsMismatch"/> and <see cref="CompiledDisassembly"/> are only meaningful when this
/// project's currently compiled source for the node was available for comparison.</summary>
public sealed record PostMortemWordRow(string AddressHex, string Disassembly, bool MayBeLiteral, bool IsMismatch, string? CompiledDisassembly);

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
      IReadOnlyList<int>? compiledRomWords)
  {
    Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    string effectiveColor = string.IsNullOrWhiteSpace(snapshot.Color) ? projectDefaultColor : snapshot.Color;
    Brush = NodeColorOption.BrushFromHex(effectiveColor);

    AText = PortAddressNames.Format(snapshot.A);
    IoText = $"0x{snapshot.Io:X5}";

    ParameterStackRows = BuildStackRows(snapshot.ParameterStack, topLabels: ["T", "S"]);
    ReturnStackRows = BuildStackRows(snapshot.ReturnStack, topLabels: ["R"]);

    RamRows = BuildWordRows(snapshot.Coordinate, snapshot.Ram, baseAddress: 0x000, compiledRamWords);
    RomRows = BuildWordRows(snapshot.Coordinate, snapshot.Rom, baseAddress: 0x080, compiledRomWords);

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

  private static IReadOnlyList<PostMortemWordRow> BuildWordRows(int coordinate, IReadOnlyList<int> words, int baseAddress, IReadOnlyList<int>? compiledWords)
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

    var rows = new List<PostMortemWordRow>(decoded.Count);
    // MayConsumeNextWordAsLiteral describes the NEXT word (the one a '@p'/'!p' in THIS word would
    // fetch/store), so the flag on a row is carried over from the PRECEDING word's decode, not its
    // own -- see F18DisassembledWord's own remarks.
    bool previousMayConsumeAsLiteral = false;
    foreach (F18DisassembledWord word in decoded)
    {
      string? compiledDisassembly = compiledByAddress.TryGetValue(word.Address, out int compiledWord)
          ? F18Disassembler.Decode(word.Address, compiledWord).ToString()
          : null;

      rows.Add(new PostMortemWordRow(
          $"0x{word.Address:X3}",
          word.ToString(),
          previousMayConsumeAsLiteral,
          mismatchedAddresses.Contains(word.Address),
          compiledDisassembly));

      previousMayConsumeAsLiteral = word.MayConsumeNextWordAsLiteral;
    }

    return rows;
  }
}
