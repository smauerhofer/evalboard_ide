namespace Ga144.Evb.Ide.Models;

public sealed class Ga144Project
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public string Name { get; set; } = "GA144 Project";

  // Schema versions through 5 stored physical-board state inside each project.
  // Version 6 migrates this property into IdeWorkspace.Boards and then clears it.
  public ProjectBoardConfiguration? Board { get; set; }

  public List<Ga144ChipConfiguration> Chips { get; set; } = [];
  public List<F18MacroDefinition> UserMacros { get; set; } = [];

  public static Ga144Project Create(string name)
  {
    var project = new Ga144Project
    {
      Name = name,
      Chips =
        [
            Ga144ChipConfiguration.Create(Ga144ChipRole.Host),
                Ga144ChipConfiguration.Create(Ga144ChipRole.Target)
        ]
    };

    return project;
  }

  // Retained so older call sites and extensions continue to compile. The board
  // model is no longer a project property in schema version 6.
  public static Ga144Project Create(string name, EvalBoardModel _) => Create(name);

  public void Normalize()
  {
    Name = string.IsNullOrWhiteSpace(Name) ? "GA144 Project" : Name.Trim();
    Board?.Normalize();
    Chips ??= [];
    UserMacros ??= [];
    F18MacroDefinition.NormalizeList(UserMacros);

    EnsureChip(Ga144ChipRole.Host);
    EnsureChip(Ga144ChipRole.Target);
    foreach (Ga144ChipConfiguration chip in Chips)
    {
      chip.Normalize();
    }
  }

  public Ga144ChipConfiguration GetChip(Ga144ChipRole role)
  {
    Normalize();
    return Chips.First(chip => chip.Role == role);
  }

  private void EnsureChip(Ga144ChipRole role)
  {
    if (Chips.All(chip => chip.Role != role))
    {
      Chips.Add(Ga144ChipConfiguration.Create(role));
    }
  }
}

public sealed class ProjectBoardConfiguration
{
  public EvalBoardModel Model { get; set; } = EvalBoardModel.EVB002;
  public string? SerialNumber { get; set; }
  public BoardPortBinding? PortA { get; set; }
  public BoardPortBinding? PortB { get; set; }
  public BoardPortBinding? PortC { get; set; }
  public Dictionary<string, bool> Jumpers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

  public void Normalize()
  {
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
    MigrateLegacyJumper("J37", "J37-1", "J37-2");

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