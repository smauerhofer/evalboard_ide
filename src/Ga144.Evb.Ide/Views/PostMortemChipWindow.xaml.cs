using System.Windows;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;
using Microsoft.Win32;

namespace Ga144.Evb.Ide.Views;

public partial class PostMortemChipWindow : Window
{
  private readonly PostMortemChipViewModel _viewModel;

  // Keyed by coordinate so a second click on the same node re-activates its already-open window
  // instead of opening a duplicate -- same reuse pattern NodeEditorWindow's own diagnostics window
  // uses, generalized from a single nullable field to a dictionary since up to 143 of these can be
  // open at once.
  private readonly Dictionary<int, PostMortemNodeWindow> _openNodeWindows = [];

  public PostMortemChipWindow(PostMortemChipViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    _viewModel.SnapshotReplaced += OnSnapshotReplaced;
    Closed += OnClosed;
  }

  /// <summary>Replaces the data this window shows with a freshly captured snapshot -- used when Core
  /// Dump runs again while this window is still open, so a second dump reuses the same window (and
  /// its own Export/Import) instead of piling up a new one every time. Goes through the view model's
  /// own LoadSnapshot (not a DataContext swap) specifically so SnapshotReplaced still fires and closes
  /// every node window left over from the previous snapshot.</summary>
  public void LoadSnapshot(PostMortemSnapshot snapshot) => _viewModel.LoadSnapshot(snapshot);

  private void OnNodeClick(object sender, RoutedEventArgs e)
  {
    if (sender is not FrameworkElement { Tag: PostMortemNodeViewModel node })
    {
      return;
    }

    if (node.IsHeadPlaceholder)
    {
      MessageBox.Show(
          this,
          $"Node {node.CoordinateText} is the Kraken head and has no live-state read path of its own, so it was not part of this Core Dump.",
          "Post-mortem node",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      return;
    }

    if (_openNodeWindows.TryGetValue(node.Coordinate, out PostMortemNodeWindow? existing))
    {
      existing.Activate();
      return;
    }

    PostMortemNodeDetailViewModel detail;
    try
    {
      detail = _viewModel.BuildNodeDetail(node.Coordinate);
    }
    catch (Exception exception)
    {
      MessageBox.Show(this, exception.Message, "Post-mortem node", MessageBoxButton.OK, MessageBoxImage.Error);
      return;
    }

    // Owner = this chip window, so WPF's own owned-window mechanism closes every open node window
    // automatically when the chip window closes -- no extra bookkeeping needed for that requirement.
    var window = new PostMortemNodeWindow(detail) { Owner = this };
    window.Closed += (_, _) => _openNodeWindows.Remove(node.Coordinate);
    _openNodeWindows[node.Coordinate] = window;
    window.Show();
  }

  private void OnSnapshotReplaced(object? sender, EventArgs e)
  {
    // Every open node window is bound to a PostMortemNodeDetailViewModel built from the snapshot that
    // Import just replaced -- close them all rather than leave them showing stale data under a title
    // that no longer matches what the chip window itself is now displaying.
    foreach (PostMortemNodeWindow window in _openNodeWindows.Values.ToList())
    {
      window.Close();
    }
  }

  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.SnapshotReplaced -= OnSnapshotReplaced;
    // WPF's own Owner mechanism already closes every remaining owned PostMortemNodeWindow when this
    // window closes; nothing further is needed here for that.
  }

  private async void OnExportClick(object sender, RoutedEventArgs e)
  {
    var dialog = new SaveFileDialog
    {
      Filter = "YAML files (*.yaml)|*.yaml|All files (*.*)|*.*",
      FileName = $"post-mortem-{_viewModel.Snapshot.ChipRole}-{_viewModel.Snapshot.CapturedAtUtc:yyyyMMdd-HHmmss}.yaml"
    };

    if (dialog.ShowDialog(this) != true)
    {
      return;
    }

    try
    {
      await _viewModel.ExportAsync(dialog.FileName);
      MessageBox.Show(this, $"Saved to \"{dialog.FileName}\".", "Export post-mortem snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      MessageBox.Show(this, $"Export failed: {exception.Message}", "Export post-mortem snapshot", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private async void OnImportClick(object sender, RoutedEventArgs e)
  {
    var dialog = new OpenFileDialog
    {
      Filter = "YAML files (*.yaml)|*.yaml|All files (*.*)|*.*"
    };

    if (dialog.ShowDialog(this) != true)
    {
      return;
    }

    try
    {
      string message = await _viewModel.ImportAsync(dialog.FileName);
      MessageBox.Show(this, message, "Import post-mortem snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception exception)
    {
      MessageBox.Show(this, $"Import failed: {exception.Message}", "Import post-mortem snapshot", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}