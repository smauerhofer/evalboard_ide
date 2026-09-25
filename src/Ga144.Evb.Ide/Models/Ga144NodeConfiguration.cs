namespace Ga144.Evb.Ide.Models;

public sealed class Ga144NodeConfiguration
{
  public int Coordinate { get; set; }
  public bool Enabled { get; set; }
  public string SourceCode { get; set; } = string.Empty;
  public List<string> RamWords { get; set; } = [];
  public StartupConfiguration Startup { get; set; } = new();

  /// <summary>
  /// This node's own color override, one of the 48 <see cref="NodeColorPalette"/> swatches
  /// (NodeEditorWindow's "Node color" picker), or null to follow the owning project's
  /// <see cref="Ga144Project.DefaultNodeColor"/> instead -- null is the ordinary state for
  /// every node that has never had its own color picked. Copying a node to another project
  /// (NodeEditorViewModel.CopyCurrentSourceTo, ChipViewModel.CopyAllNodesToProject) writes the
  /// node's *resolved* color here explicitly, so the copy looks the same in the destination
  /// project even if that project's own default differs.
  /// </summary>
  public string? Color { get; set; }

  /// <summary>
  /// ADDED 2026-09-22, per Stefan directly: "post-mortem is too slow now. i am not interested in all
  /// nodes ... make a checkbox that decides if the node should be read in post-mortem analysis." A
  /// SEPARATE flag from <see cref="Enabled"/> (which governs the CVM boot stream/roster, see
  /// <see cref="Cvm.CvmBootStreamBuilder.GetConfiguredCoordinates"/>) -- a node can be included in one,
  /// the other, both, or neither; nothing here implies anything about the other. Consulted only by
  /// <see cref="ViewModels.CvmDebuggerViewModel.CoreDumpAsync"/>: when false, that node's own
  /// registers/stacks/RAM/ROM are not read during a Core Dump (the expensive part -- 64+64 RAM/ROM
  /// words alone dwarf everything else per node), though the tentacle wire walk still passes through it
  /// exactly as before ("the tentacle mechanism for reading the nodes do not change," Stefan's own
  /// words) since later nodes on the same tentacle still need to be reached through it. Defaults to
  /// true so an existing project, or one saved before this field existed, keeps reading every node (the
  /// old behavior) until Stefan actually unchecks the ones he does not care about.
  /// </summary>
  public bool PostMortemEnabled { get; set; } = true;

  public static Ga144NodeConfiguration Create(int coordinate) => new()
  {
    Coordinate = coordinate,
    Enabled = false,
    PostMortemEnabled = true,
    Startup = new StartupConfiguration()
  };

  public void Normalize()
  {
    SourceCode ??= string.Empty;
    RamWords ??= [];
    Startup ??= new StartupConfiguration();
    Startup.Normalize();
    Color = string.IsNullOrWhiteSpace(Color) ? null : Color.Trim();
  }
}

public sealed class StartupConfiguration
{
  public string EntryPoint { get; set; } = "0x000";
  public string P { get; set; } = "0x000";
  public string A { get; set; } = "0x000";
  public string B { get; set; } = "0x000";
  public string Io { get; set; } = "0x00000";
  public List<string> ReturnStack { get; set; } = [];
  public List<string> ParameterStack { get; set; } = [];

  public void Normalize()
  {
    EntryPoint ??= "0x000";
    P ??= "0x000";
    A ??= "0x000";
    B ??= "0x000";
    Io ??= "0x00000";
    ReturnStack ??= [];
    ParameterStack ??= [];
  }
}