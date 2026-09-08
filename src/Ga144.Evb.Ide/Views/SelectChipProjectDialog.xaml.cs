using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Lets Stefan point a Program-kind C project at the specific GA144 project/chip Build should compile
/// the live CVM2 interpreter mesh from before linking (<see cref="CProjectMetadata.ChipProjectId"/>/
/// <see cref="CProjectMetadata.ChipProjectRole"/>) -- added 2026-09-08 wiring galink into Build. Mirrors
/// <see cref="TextInputDialog"/>'s own "small reusable dialog with a static Ask helper" shape.
/// </summary>
public partial class SelectChipProjectDialog : Window
{
  private sealed record ListItem(Guid? Id, string Name)
  {
    public override string ToString() => Name;
  }

  public SelectChipProjectDialog()
  {
    InitializeComponent();
  }

  private Guid? SelectedProjectId { get; set; }

  private Ga144ChipRole SelectedRole { get; set; } = Ga144ChipRole.Target;

  private void OnOkClick(object sender, RoutedEventArgs e)
  {
    SelectedProjectId = (ProjectListBox.SelectedItem as ListItem)?.Id;
    SelectedRole = HostRadioButton.IsChecked == true ? Ga144ChipRole.Host : Ga144ChipRole.Target;
    DialogResult = true;
  }

  /// <summary>
  /// Shows the picker modally, pre-selecting <paramref name="currentProjectId"/>/<paramref name="currentRole"/>
  /// if given. Returns null if the user cancelled; otherwise the chosen project id (null for "(none)",
  /// meaning "don't link this project") and chip role.
  /// </summary>
  public static (Guid? ProjectId, Ga144ChipRole Role)? Ask(
      Window owner,
      IReadOnlyList<ChipProjectResolver.ProjectSummary> projects,
      Guid? currentProjectId,
      Ga144ChipRole currentRole)
  {
    var dialog = new SelectChipProjectDialog { Owner = owner };

    var items = new List<ListItem> { new(null, "(none -- do not link this project)") };
    items.AddRange(projects.Select(project => new ListItem(project.Id, project.Name)));

    dialog.ProjectListBox.ItemsSource = items;
    dialog.ProjectListBox.SelectedItem = items.FirstOrDefault(item => item.Id == currentProjectId) ?? items[0];
    dialog.HostRadioButton.IsChecked = currentRole == Ga144ChipRole.Host;
    dialog.TargetRadioButton.IsChecked = currentRole != Ga144ChipRole.Host;

    return dialog.ShowDialog() == true ? (dialog.SelectedProjectId, dialog.SelectedRole) : null;
  }
}
