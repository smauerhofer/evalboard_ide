namespace Ga144.Evb.Ide.Models;

public sealed class Ga144RomLibrary
{
  public int SchemaVersion { get; set; } = 2;
  public string ChipPart { get; set; } = "GA144-1.20";
  public List<Ga144RomNodeDefinition> Nodes { get; set; } = [];
  public List<F18MacroDefinition> SystemMacros { get; set; } = [];

  public static Ga144RomLibrary CreateDefault()
  {
    var library = new Ga144RomLibrary();
    library.Normalize();
    return library;
  }

  public void Normalize()
  {
    SchemaVersion = 2;
    ChipPart = string.IsNullOrWhiteSpace(ChipPart) ? "GA144-1.20" : ChipPart.Trim();
    Nodes ??= [];
    SystemMacros ??= [];
    F18MacroDefinition.NormalizeList(SystemMacros);

    var valid = Nodes
        .Where(node => IsValidCoordinate(node.Coordinate))
        .GroupBy(node => node.Coordinate)
        .Select(group => group.First())
        .ToDictionary(node => node.Coordinate);

    Nodes.Clear();
    for (var row = 0; row < 8; row++)
    {
      for (var column = 0; column < 18; column++)
      {
        var coordinate = row * 100 + column;
        if (!valid.TryGetValue(coordinate, out var node))
        {
          node = Ga144RomNodeDefinition.Create(coordinate);
        }

        node.Normalize();
        Nodes.Add(node);
      }
    }
  }

  public Ga144RomNodeDefinition GetNode(int coordinate)
  {
    Normalize();
    return Nodes.First(node => node.Coordinate == coordinate);
  }

  private static bool IsValidCoordinate(int coordinate)
  {
    var row = coordinate / 100;
    var column = coordinate % 100;
    return row is >= 0 and <= 7 && column is >= 0 and <= 17;
  }
}

public sealed class Ga144RomNodeDefinition
{
  public int Coordinate { get; set; }
  public string SourceCode { get; set; } = string.Empty;
  public List<string> RomWords { get; set; } = [];

  public static Ga144RomNodeDefinition Create(int coordinate) => new()
  {
    Coordinate = coordinate,
    SourceCode = string.Empty,
    RomWords = []
  };

  public void Normalize()
  {
    SourceCode ??= string.Empty;
    RomWords ??= [];
  }
}
