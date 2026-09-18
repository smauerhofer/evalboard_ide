using System.Collections.ObjectModel;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Drives the read-only post-mortem GA144 window (<see cref="Views.PostMortemChipWindow"/>): shows
/// one <see cref="PostMortemSnapshot"/>'s 143 captured nodes (708, the Kraken head, is never
/// captured) using the same node-color system as the live GA144 window, and builds the detail view
/// model for whichever node is clicked. <see cref="_liveChip"/>/<see cref="_romLibrary"/> are the
/// project's CURRENT chip/ROM library (not part of the snapshot itself) -- used only to compile this
/// project's present-day source for a RAM/ROM comparison; either may be null (nothing to compile
/// against), in which case <see cref="BuildNodeDetail"/> simply omits the comparison.
/// </summary>
public sealed class PostMortemChipViewModel : ObservableObject
{
  private readonly Ga144ChipConfiguration? _liveChip;
  private readonly Ga144RomLibrary? _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private PostMortemSnapshot _snapshot;

  public PostMortemChipViewModel(
      PostMortemSnapshot snapshot,
      Ga144ChipConfiguration? liveChip,
      Ga144RomLibrary? romLibrary,
      IReadOnlyList<F18MacroDefinition>? userMacros)
  {
    _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    _liveChip = liveChip;
    _romLibrary = romLibrary;
    _userMacros = userMacros ?? [];

    Nodes = [];
    RebuildNodes();
  }

  /// <summary>
  /// Raised after <see cref="LoadSnapshot"/> replaces the displayed data (Import). The window's
  /// code-behind closes every currently open post-mortem node window in response, since each one is
  /// bound to a <see cref="PostMortemNodeDetailViewModel"/> built from the PREVIOUS snapshot.
  /// </summary>
  public event EventHandler? SnapshotReplaced;

  public PostMortemSnapshot Snapshot => _snapshot;

  public string Title =>
      $"GA144 post-mortem -- {_snapshot.ChipName} ({_snapshot.ChipRole}), captured {_snapshot.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

  public ObservableCollection<PostMortemNodeViewModel> Nodes { get; private set; }

  /// <summary>Replaces the displayed snapshot in place (used by Import) -- same window, new data.</summary>
  public void LoadSnapshot(PostMortemSnapshot snapshot)
  {
    _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    RebuildNodes();
    OnPropertyChanged(nameof(Snapshot));
    OnPropertyChanged(nameof(Title));
    SnapshotReplaced?.Invoke(this, EventArgs.Empty);
  }

  public Task ExportAsync(string path) => PostMortemSnapshotStore.ExportAsync(path, _snapshot);

  public async Task<string> ImportAsync(string path)
  {
    PostMortemSnapshot imported = await PostMortemSnapshotStore.ImportAsync(path);
    LoadSnapshot(imported);
    return $"Imported {imported.Nodes.Count} node(s) from \"{Path.GetFileName(path)}\", captured {imported.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}.";
  }

  /// <summary>
  /// Builds the detail view model for one node's post-mortem window, resolving a comparison against
  /// this project's currently compiled RAM/ROM when a live chip/ROM library is available. A node
  /// whose current source does not even compile just yields no comparison (not an error) -- the
  /// detail window still opens, showing only the captured post-mortem data.
  /// </summary>
  public PostMortemNodeDetailViewModel BuildNodeDetail(int coordinate)
  {
    if (!_snapshot.Nodes.TryGetValue(coordinate, out PostMortemNodeSnapshot? nodeSnapshot))
    {
      throw new ArgumentOutOfRangeException(nameof(coordinate), $"No post-mortem data was captured for node {coordinate:000}.");
    }

    IReadOnlyList<int>? compiledRamWords = null;
    IReadOnlyList<int>? compiledRomWords = null;
    IReadOnlyDictionary<string, F18ExportedSymbol>? compiledRamSymbols = null;
    IReadOnlyDictionary<string, F18ExportedSymbol>? compiledRomSymbols = null;
    if (_liveChip is not null && _romLibrary is not null)
    {
      try
      {
        var compileService = new F18NodeCompilationService(_liveChip, _romLibrary, _userMacros);
        F18NodeCompilationResult compiled = compileService.CompileNode(coordinate);
        compiledRamWords = compiled.Ram.Success ? compiled.Ram.Words : null;
        compiledRomWords = compiled.Rom.Success ? compiled.Rom.Words : null;
        compiledRamSymbols = compiled.Ram.Success ? compiled.Ram.Symbols : null;
        compiledRomSymbols = compiled.Rom.Success ? compiled.Rom.Symbols : null;
      }
      catch
      {
        // A comparison is a nice-to-have, not essential to viewing the post-mortem data -- if this
        // node's current project source cannot be compiled at all, the detail window simply shows
        // the captured data with no "compiled vs. on-chip" diff instead of failing to open.
      }
    }

    return new PostMortemNodeDetailViewModel(
        nodeSnapshot,
        _snapshot.ProjectDefaultNodeColor,
        compiledRamWords,
        compiledRomWords,
        compiledRamSymbols,
        compiledRomSymbols);
  }

  // Builds exactly 144 cells, one per coordinate, in the same descending-row/ascending-column order
  // the live GA144 grid uses (ChipViewModel.RebuildNodes) -- NOT just however many entries happen to
  // be in _snapshot.Nodes. A PostMortemSnapshot never has an entry for 708 (no live-state read path
  // of its own), and a partial/failed dump could be missing others too; the fixed 18x8 UniformGrid
  // fills purely by item order, so leaving a gap in this list would silently shift every following
  // node by one position instead of just showing an empty/placeholder cell where it belongs.
  private void RebuildNodes()
  {
    var nodes = new List<PostMortemNodeViewModel>(144);
    for (int row = 7; row >= 0; row--)
    {
      for (int column = 0; column <= 17; column++)
      {
        int coordinate = row * 100 + column;
        _snapshot.Nodes.TryGetValue(coordinate, out PostMortemNodeSnapshot? nodeSnapshot);
        nodes.Add(new PostMortemNodeViewModel(coordinate, nodeSnapshot, _snapshot.ProjectDefaultNodeColor));
      }
    }

    Nodes = [.. nodes];
    OnPropertyChanged(nameof(Nodes));
  }
}