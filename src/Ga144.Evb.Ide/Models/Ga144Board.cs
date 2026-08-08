namespace Ga144.Evb.Ide.Models;

/// <summary>
/// Persistent description of one physical EVB001 or EVB002 board.
/// Hardware identity, FTDI bindings, and jumper state belong to the board and
/// are deliberately independent from the currently selected software project.
/// </summary>
public sealed class Ga144Board
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Name { get; set; } = "GA144 Evalboard";
  public EvalBoardModel Model { get; set; } = EvalBoardModel.Unknown;
  public string? SerialNumber { get; set; }
  public BoardPortBinding? PortA { get; set; }
  public BoardPortBinding? PortB { get; set; }
  public BoardPortBinding? PortC { get; set; }
  public Dictionary<string, bool> Jumpers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  public DateTimeOffset? LastSeenUtc { get; set; }

  public static Ga144Board Create(string name, EvalBoardModel model)
  {
    var board = new Ga144Board
    {
      Name = name,
      Model = model
    };
    board.ApplyDefaultJumpers(overwriteExisting: true);
    return board;
  }

  public static Ga144Board FromLegacy(string name, ProjectBoardConfiguration legacy)
  {
    ArgumentNullException.ThrowIfNull(legacy);

    var board = new Ga144Board
    {
      Name = name,
      Model = legacy.Model,
      SerialNumber = legacy.SerialNumber,
      PortA = legacy.PortA,
      PortB = legacy.PortB,
      PortC = legacy.PortC,
      Jumpers = new Dictionary<string, bool>(legacy.Jumpers ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase)
    };
    board.Normalize();
    return board;
  }

  public void Normalize()
  {
    Name = string.IsNullOrWhiteSpace(Name) ? "GA144 Evalboard" : Name.Trim();
    SerialNumber = string.IsNullOrWhiteSpace(SerialNumber) ? null : SerialNumber.Trim();
    Jumpers ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    Jumpers = new Dictionary<string, bool>(Jumpers, StringComparer.OrdinalIgnoreCase);
    ApplyDefaultJumpers(overwriteExisting: false);
  }

  public void ApplyDefaultJumpers(bool overwriteExisting)
  {
    MigrateLegacyJumper("J23-A", "J23-A-RX", "J23-A-TX");
    MigrateLegacyJumper("J23-B", "J23-B-RX", "J23-B-TX");
    MigrateLegacyJumper("J23-C", "J23-C-RX", "J23-C-TX");
    MigrateLegacyJumper("J22", "J22-HOST", "J22-USB-C", "J22-RC");
    MigrateLegacyJumper("J20", "J20-RESET", "J20-USB-A");

    BoardVisualDefinition definition = BoardVisualCatalog.Get(Model);
    foreach (JumperVisualDefinition jumper in definition.Jumpers)
    {
      if (overwriteExisting || !Jumpers.ContainsKey(jumper.Id))
      {
        Jumpers[jumper.Id] = jumper.DefaultInstalled;
      }
    }
  }

  private void MigrateLegacyJumper(string legacyId, params string[] replacementIds)
  {
    if (!Jumpers.TryGetValue(legacyId, out bool installed))
    {
      return;
    }

    foreach (string replacementId in replacementIds)
    {
      if (!Jumpers.ContainsKey(replacementId))
      {
        Jumpers[replacementId] = installed;
      }
    }

    Jumpers.Remove(legacyId);
  }
}
