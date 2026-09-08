using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Loads a GA144 project's own chip configuration straight from the single shared workspace document
/// every window in this app already reads (<see cref="ConfigurationPathProvider"/>/
/// <see cref="YamlConfigurationStore"/>) and the single shared ROM library
/// (<see cref="Ga144RomLibraryStore"/>) -- fresh from disk, independent of whatever
/// <c>MainWindowViewModel</c> or GA144 project window happens to be open right now, or whether one is
/// open at all.
///
/// Added 2026-09-08 to let <see cref="Ga144.Evb.Ide.ViewModels.CProjectViewModel.Build"/> resolve a C
/// project's own <see cref="CProjectMetadata.ChipProjectId"/>/<see cref="CProjectMetadata.ChipProjectRole"/>
/// into an actual <see cref="Ga144ChipConfiguration"/> to hand <see cref="CvmPrimitiveTableExporter"/>,
/// and to back a "choose a chip project" picker dialog with the list of GA144 projects that exist to
/// choose from. Deliberately its own small service rather than a dependency on
/// <c>MainWindowViewModel</c>: a C project's Build can run (and this resolution needs to work) whether
/// or not the GA144 hardware side of the IDE is open at all.
/// </summary>
public sealed class ChipProjectResolver
{
  private readonly ConfigurationPathProvider _pathProvider;

  public ChipProjectResolver(ConfigurationPathProvider? pathProvider = null)
  {
    // null here means "no --config command-line override" -- ConfigurationPathProvider itself already
    // checks the GA144IDE_DATA environment variable before falling back to the per-user
    // LocalApplicationData folder (see its own remarks), so this alone reaches the exact same
    // workspace/ROM-library files every other window in this app reads by default.
    _pathProvider = pathProvider ?? new ConfigurationPathProvider(null);
  }

  public sealed record ProjectSummary(Guid Id, string Name);

  public sealed class Result
  {
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string ProjectName { get; init; } = "";
    public Ga144ChipConfiguration? Chip { get; init; }
    public Ga144RomLibrary? RomLibrary { get; init; }
    public IReadOnlyList<F18MacroDefinition> UserMacros { get; init; } = [];
  }

  /// <summary>Every GA144 project in the shared workspace, for a chip-project picker to list. Loads the
  /// workspace fresh each call -- this is meant for an occasional picker dialog, not a hot path.</summary>
  public IReadOnlyList<ProjectSummary> ListProjects()
  {
    IdeWorkspace workspace = LoadWorkspace();
    return [.. workspace.Projects.Select(project => new ProjectSummary(project.Id, project.Name))];
  }

  /// <summary>
  /// Resolves <paramref name="projectId"/>/<paramref name="role"/> into that project's own chip
  /// configuration, the shared ROM library, and that project's own user macros -- everything
  /// <see cref="CvmPrimitiveTableExporter.Export"/> needs. Fails with a clear message (never throws for
  /// an ordinary "not found" case) if the project no longer exists -- e.g. it was deleted from the
  /// workspace after a C project was pointed at it.
  /// </summary>
  public Result Resolve(Guid projectId, Ga144ChipRole role)
  {
    IdeWorkspace workspace = LoadWorkspace();
    Ga144Project? project = workspace.Projects.FirstOrDefault(p => p.Id == projectId);
    if (project is null)
    {
      return new Result
      {
        Success = false,
        ErrorMessage = $"the chip project this C project references (id {projectId}) no longer exists in the workspace -- choose a chip project again.",
      };
    }

    Ga144RomLibrary romLibrary = LoadRomLibrary();
    Ga144ChipConfiguration chip = project.GetChip(role);
    return new Result
    {
      Success = true,
      ProjectName = project.Name,
      Chip = chip,
      RomLibrary = romLibrary,
      UserMacros = project.UserMacros,
    };
  }

  private IdeWorkspace LoadWorkspace()
  {
    using var store = new YamlConfigurationStore(_pathProvider.ConfigurationPath, _pathProvider.LegacyJsonPath);
    IdeWorkspace workspace = store.LoadAsync().GetAwaiter().GetResult();
    workspace.Normalize();
    return workspace;
  }

  private Ga144RomLibrary LoadRomLibrary()
  {
    using var store = new Ga144RomLibraryStore(_pathProvider.RomLibraryPath);
    Ga144RomLibrary library = store.LoadAsync().GetAwaiter().GetResult();
    library.Normalize();
    return library;
  }
}
