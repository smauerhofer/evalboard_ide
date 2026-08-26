using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Backs the small "Copy to project…" picker dialog opened from the node
/// editor. Lets the user choose which other open project, and which chip
/// role (Host/Target) within it, receives a copy of the current node's RAM
/// source and startup state.
/// </summary>
public sealed class CopyNodeToProjectViewModel : ObservableObject
{
  private ProjectViewModel? _selectedProject;
  private Ga144ChipRole _selectedRole;

  public CopyNodeToProjectViewModel(
      IReadOnlyList<ProjectViewModel> availableProjects,
      Ga144ChipRole defaultRole,
      string nodeCoordinateText)
  {
    AvailableProjects = availableProjects;
    NodeCoordinateText = nodeCoordinateText;
    _selectedProject = availableProjects.Count > 0 ? availableProjects[0] : null;
    _selectedRole = defaultRole;
  }

  public IReadOnlyList<ProjectViewModel> AvailableProjects { get; }
  public string NodeCoordinateText { get; }

  public string IntroText =>
      $"Copy node {NodeCoordinateText}'s RAM source and startup state into the same node coordinate in another project. " +
      "ROM is shared across every project and does not need to be copied.";

  public ProjectViewModel? SelectedProject
  {
    get => _selectedProject;
    set => SetProperty(ref _selectedProject, value);
  }

  public Ga144ChipRole SelectedRole
  {
    get => _selectedRole;
    private set
    {
      if (SetProperty(ref _selectedRole, value))
      {
        OnPropertyChanged(nameof(CopyToHost));
        OnPropertyChanged(nameof(CopyToTarget));
      }
    }
  }

  public bool CopyToHost
  {
    get => _selectedRole == Ga144ChipRole.Host;
    set
    {
      if (value)
      {
        SelectedRole = Ga144ChipRole.Host;
      }
    }
  }

  public bool CopyToTarget
  {
    get => _selectedRole == Ga144ChipRole.Target;
    set
    {
      if (value)
      {
        SelectedRole = Ga144ChipRole.Target;
      }
    }
  }
}
