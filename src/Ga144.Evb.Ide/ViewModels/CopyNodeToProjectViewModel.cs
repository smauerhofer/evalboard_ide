using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

/// <summary>
/// Backs the small "Copy to project…" picker dialog, shared by the node editor's own single-node
/// "Copy to project…" button and (since 2026-09-18) the GA144 chip window's "Copy all nodes to
/// project…" button. Lets the user choose which other open project, and which chip role (Host/Target)
/// within it, receives the copy -- what is actually being copied (one node's live editor state vs.
/// every configured node's persisted model) is entirely up to the caller; this view model only picks
/// the destination.
/// </summary>
public sealed class CopyNodeToProjectViewModel : ObservableObject
{
  private ProjectViewModel? _selectedProject;
  private Ga144ChipRole _selectedRole;

  public CopyNodeToProjectViewModel(
      IReadOnlyList<ProjectViewModel> availableProjects,
      Ga144ChipRole defaultRole,
      string introText)
  {
    AvailableProjects = availableProjects;
    IntroText = introText;
    _selectedProject = availableProjects.Count > 0 ? availableProjects[0] : null;
    _selectedRole = defaultRole;
  }

  public IReadOnlyList<ProjectViewModel> AvailableProjects { get; }

  public string IntroText { get; }

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