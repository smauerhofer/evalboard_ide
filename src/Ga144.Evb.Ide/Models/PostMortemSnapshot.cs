namespace Ga144.Evb.Ide.Models;

/// <summary>
/// One "Core Dump" capture of an entire GA144's live state, taken through Kraken
/// right after a fresh reset (see <see cref="ViewModels.CvmDebuggerViewModel"/>'s
/// Core Dump command). Every node except 708 -- the Kraken head, which has no
/// live-state read path of its own; it IS the transport -- is captured: A, IO,
/// RAM, ROM, and both stacks. Reading the stacks is destructive
/// (<see cref="Services.KrakenLiveController.ReadParameterStackAsync"/>/
/// <see cref="Services.KrakenLiveController.ReadReturnStackAsync"/> pop them off
/// the live node as part of reading them, and nothing here writes them back) --
/// confirmed acceptable for a post-mortem snapshot taken after a reset with
/// nothing running.
///
/// Persisted as a standalone YAML file via <see cref="Services.PostMortemSnapshotStore"/>,
/// independent of the project's own workspace file, so a snapshot survives and
/// can be shared/compared long after the project itself has moved on.
/// </summary>
public sealed class PostMortemSnapshot
{
  public int SchemaVersion { get; set; } = 1;
  public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
  public Ga144ChipRole ChipRole { get; set; }
  public string ChipName { get; set; } = string.Empty;

  /// <summary>
  /// The owning project's <see cref="Ga144Project.DefaultNodeColor"/> at the moment
  /// this snapshot was taken -- captured as a plain hex string, not a live project
  /// reference, so the post-mortem window renders "with the colors of the current
  /// project" (as requested) even after the project's own default color later
  /// changes, or the snapshot is exported and re-imported into a different session
  /// entirely.
  /// </summary>
  public string ProjectDefaultNodeColor { get; set; } = NodeColorPalette.DefaultColor;

  public Dictionary<int, PostMortemNodeSnapshot> Nodes { get; set; } = [];

  public void Normalize()
  {
    ChipName ??= string.Empty;
    ProjectDefaultNodeColor = NodeColorPalette.IsPaletteColor(ProjectDefaultNodeColor)
        ? ProjectDefaultNodeColor
        : NodeColorPalette.DefaultColor;
    Nodes ??= [];
    foreach (KeyValuePair<int, PostMortemNodeSnapshot> entry in Nodes)
    {
      entry.Value.Coordinate = entry.Key;
      entry.Value.Normalize();
    }
  }
}

/// <summary>
/// One node's captured live state within a <see cref="PostMortemSnapshot"/>.
/// <see cref="Error"/> is set (with every other field left at its unread default)
/// when reading this node failed -- one node's transport failure does not abort
/// the rest of a Core Dump, the same per-node policy
/// <see cref="ViewModels.ChipViewModel.VerifyAllRomsAsync"/> already uses for its
/// own node-by-node scan.
/// </summary>
public sealed class PostMortemNodeSnapshot
{
  public int Coordinate { get; set; }
  public int A { get; set; }
  public int Io { get; set; }

  /// <summary>This node's own real F18A hardware carry flag (0 or 1) at capture time -- genuine per-node
  /// ALU state, read via a temporary Extended Arithmetic Mode re-focus (see
  /// <see cref="Services.KrakenSession.ReadCarryAsync"/>). Unrelated to the CVM's own node-405 software
  /// carry emulation, which is a completely separate, higher-level concept.</summary>
  public int Carry { get; set; }

  public List<int> Ram { get; set; } = [];
  public List<int> Rom { get; set; } = [];

  /// <summary>Bottom-to-top, same convention as <see cref="Services.KrakenLiveController.ReadParameterStackAsync"/>
  /// -- the last entry is T (top of stack), the one before it S.</summary>
  public List<int> ParameterStack { get; set; } = [];

  /// <summary>Bottom-to-top, same convention as <see cref="Services.KrakenLiveController.ReadReturnStackAsync"/>
  /// -- the last entry is R (top of stack).</summary>
  public List<int> ReturnStack { get; set; } = [];

  /// <summary>This node's own color override at capture time, mirroring
  /// <see cref="Ga144NodeConfiguration.Color"/> -- null means "follow this
  /// snapshot's own <see cref="PostMortemSnapshot.ProjectDefaultNodeColor"/>",
  /// exactly like a live project.</summary>
  public string? Color { get; set; }

  /// <summary>Non-null only when reading this node failed; every other field is
  /// then left at its default (not meaningful) rather than a partially-read
  /// state.</summary>
  public string? Error { get; set; }

  public void Normalize()
  {
    Ram ??= [];
    Rom ??= [];
    ParameterStack ??= [];
    ReturnStack ??= [];
    Color = string.IsNullOrWhiteSpace(Color) ? null : Color.Trim();
  }
}