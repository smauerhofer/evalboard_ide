namespace Ga144.Evb.Ide.Models;

public sealed class Ga144NodeConfiguration
{
  public int Coordinate { get; set; }
  public bool Enabled { get; set; }
  public string SourceCode { get; set; } = string.Empty;
  public List<string> RamWords { get; set; } = [];
  public StartupConfiguration Startup { get; set; } = new();

  /// <summary>
  /// This node's own color override, one of the 16 <see cref="NodeColorPalette"/> swatches
  /// (NodeEditorWindow's "Node color" picker), or null to follow the owning project's
  /// <see cref="Ga144Project.DefaultNodeColor"/> instead -- null is the ordinary state for
  /// every node that has never had its own color picked. Copying a node to another project
  /// (NodeEditorViewModel.CopyCurrentSourceTo, ChipViewModel.CopyAllNodesToProject) writes the
  /// node's *resolved* color here explicitly, so the copy looks the same in the destination
  /// project even if that project's own default differs.
  /// </summary>
  public string? Color { get; set; }

  public static Ga144NodeConfiguration Create(int coordinate) => new()
  {
    Coordinate = coordinate,
    Enabled = false,
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