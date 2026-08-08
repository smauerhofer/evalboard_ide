namespace Ga144.Evb.Ide.Models;

public sealed class Ga144ChipConfiguration
{
    public Ga144ChipRole Role { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Ga144NodeConfiguration> Nodes { get; set; } = [];
    public KrakenConfiguration Kraken { get; set; } = new();

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
        Kraken ??= new KrakenConfiguration();
        Kraken.Normalize();
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
