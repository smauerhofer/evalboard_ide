using System.Windows;
using Ga144.Evb.Ide.ViewModels;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Code-behind for the "GA144 simulator" window. Owns the dictionary-by-coordinate reuse pattern for
/// per-node <see cref="SimulatorNodeWindow"/>s (same pattern as <see cref="PostMortemChipWindow"/>'s own
/// node windows) -- a second click on an already-open node re-activates it instead of opening a duplicate.
/// </summary>
public partial class SimulatorChipWindow : Window
{
  private readonly SimulatorChipViewModel _viewModel;
  private readonly Dictionary<int, SimulatorNodeWindow> _openNodeWindows = [];

  public SimulatorChipWindow(SimulatorChipViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
  }

  private void OnNodeClick(object sender, RoutedEventArgs e)
  {
    if (sender is not FrameworkElement { Tag: SimulatorNodeCellViewModel node })
    {
      return;
    }

    if (_openNodeWindows.TryGetValue(node.Coordinate, out SimulatorNodeWindow? existing))
    {
      existing.Activate();
      return;
    }

    SimulatorNodeDetailViewModel detail;
    try
    {
      detail = _viewModel.BuildNodeDetail(node.Coordinate);
    }
    catch (Exception exception)
    {
      MessageBox.Show(this, exception.Message, "GA144 simulator", MessageBoxButton.OK, MessageBoxImage.Error);
      return;
    }

    // Owner = this chip window, so WPF's own owned-window mechanism closes every open node window
    // automatically when the chip window closes -- same as PostMortemChipWindow's own node windows.
    var window = new SimulatorNodeWindow(detail, _viewModel) { Owner = this };
    window.Closed += (_, _) => _openNodeWindows.Remove(node.Coordinate);
    _openNodeWindows[node.Coordinate] = window;
    window.Show();
  }
}
