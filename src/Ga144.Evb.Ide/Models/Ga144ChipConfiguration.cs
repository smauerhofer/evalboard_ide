using YamlDotNet.Serialization;

namespace Ga144.Evb.Ide.Models;

public sealed class Ga144ChipConfiguration
{
  public Ga144ChipRole Role { get; set; }
  public string Name { get; set; } = string.Empty;
  public List<Ga144NodeConfiguration> Nodes { get; set; } = [];

  /// <summary>
  /// The Kraken structure (head 708 + three fixed tentacles) is a constant of the
  /// GA144 array and the boot protocol, not per-chip configuration. It is never
  /// persisted: it is always the one fixed topology, recreated in memory. Whether
  /// a Kraken is actually running is transient runtime state owned by the live
  /// controller (HardwareErected), not anything stored here. Any legacy 'kraken:'
  /// block in old YAML is ignored on load.
  /// </summary>
  [YamlIgnore]
  public KrakenConfiguration Kraken { get; } = KrakenConfiguration.CreateFixed();

  public static Ga144ChipConfiguration Create(Ga144ChipRole role)
  {
    var chip = new Ga144ChipConfiguration
    {
      Role = role,
      Name = role == Ga144ChipRole.Host ? "Host GA144" : "Target GA144"
    };

    chip.EnsureAllNodes();
    return chip;
  }

  public void Normalize()
  {
    Name = string.IsNullOrWhiteSpace(Name)
    ? Role == Ga144ChipRole.Host ? "Host GA144" : "Target GA144"
    : Name.Trim();
    Nodes ??= [];
    EnsureAllNodes();
    Nodes.Sort((left, right) => left.Coordinate.CompareTo(right.Coordinate));
  }

  public Ga144NodeConfiguration GetNode(int coordinate)
  {
    Normalize();
    return Nodes.First(node => node.Coordinate == coordinate);
  }

  private void EnsureAllNodes()
  {
    var existing = Nodes.ToDictionary(node => node.Coordinate);
    for (int row = 0; row < 8; row++)
    {
      for (int column = 0; column < 18; column++)
      {
        int coordinate = row * 100 + column;
        if (!existing.ContainsKey(coordinate))
        {
          Nodes.Add(Ga144NodeConfiguration.Create(coordinate));
        }
      }
    }
  }
}