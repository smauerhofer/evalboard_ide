namespace Ga144.Evb.Ide.Models;

public sealed class IdeWorkspace
{
    public int SchemaVersion { get; set; } = 7;
    public Guid? ActiveBoardId { get; set; }
    public Guid? ActiveProjectId { get; set; }
    public AppSettings Settings { get; set; } = new();
    public List<Ga144Board> Boards { get; set; } = [];
    public List<Ga144Project> Projects { get; set; } = [];

    public static IdeWorkspace CreateDefault()
    {
        Ga144Board board = Ga144Board.Create("Eval Board 1", EvalBoardModel.EVB002);
        Ga144Project project = Ga144Project.Create("GA144 Project 1");
        return new IdeWorkspace
        {
            ActiveBoardId = board.Id,
            ActiveProjectId = project.Id,
            Boards = [board],
            Projects = [project]
        };
    }

    public void Normalize()
    {
        Settings ??= new AppSettings();
        Boards ??= [];
        Projects ??= [];
        Settings.ScanIntervalMs = Math.Clamp(Settings.ScanIntervalMs, 500, 10_000);
        Settings.BaudRate = Math.Clamp(Settings.BaudRate, 9_600, 1_000_000);

        if (Projects.Count == 0)
        {
            Projects.Add(Ga144Project.Create("GA144 Project 1"));
        }

        foreach (Ga144Project project in Projects)
        {
            project.Normalize();
        }

        MigrateProjectBoards();

        if (Boards.Count == 0)
        {
            Boards.Add(Ga144Board.Create("Eval Board 1", EvalBoardModel.EVB002));
        }

        foreach (Ga144Board board in Boards)
        {
            board.Normalize();
        }

        if (ActiveBoardId is null || Boards.All(board => board.Id != ActiveBoardId))
        {
            ActiveBoardId = Boards[0].Id;
        }

        if (ActiveProjectId is null || Projects.All(project => project.Id != ActiveProjectId))
        {
            ActiveProjectId = Projects[0].Id;
        }

        SchemaVersion = 7;
    }

    private void MigrateProjectBoards()
    {
        foreach (Ga144Project project in Projects)
        {
            ProjectBoardConfiguration? legacy = project.Board;
            if (legacy is null)
            {
                continue;
            }

            legacy.Normalize();
            Ga144Board? existing = FindMatchingBoard(legacy);
            if (existing is null)
            {
                existing = Ga144Board.FromLegacy($"{project.Name} board", legacy);
                Boards.Add(existing);
            }
            else
            {
                MergeLegacyBoard(existing, legacy);
            }

            if (ActiveBoardId is null && ActiveProjectId == project.Id)
            {
                ActiveBoardId = existing.Id;
            }

            project.Board = null;
        }
    }

    private Ga144Board? FindMatchingBoard(ProjectBoardConfiguration legacy)
    {
        if (!string.IsNullOrWhiteSpace(legacy.SerialNumber))
        {
            return Boards.FirstOrDefault(board =>
                board.Model == legacy.Model &&
                string.Equals(board.SerialNumber, legacy.SerialNumber, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static void MergeLegacyBoard(Ga144Board target, ProjectBoardConfiguration legacy)
    {
        target.PortA ??= legacy.PortA;
        target.PortB ??= legacy.PortB;
        target.PortC ??= legacy.PortC;
        foreach ((string key, bool value) in legacy.Jumpers)
        {
            if (!target.Jumpers.ContainsKey(key))
            {
                target.Jumpers[key] = value;
            }
        }
    }
}

public sealed class AppSettings
{
    public bool AutoDetect { get; set; } = true;
    public bool ActiveProbeNewFtdiPorts { get; set; } = true;
    public int ScanIntervalMs { get; set; } = 1500;
    public int BaudRate { get; set; } = 921600;
}
