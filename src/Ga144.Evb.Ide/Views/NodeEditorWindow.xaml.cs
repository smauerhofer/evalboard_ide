using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class NodeEditorWindow : Window
{
  private readonly NodeEditorViewModel _viewModel;
  private CompileDiagnosticsWindow? _diagnosticsWindow;

  public NodeEditorWindow(NodeEditorViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Title = $"Node {viewModel.NodeCoordinate} editor";
    _viewModel.DiagnosticsRequested += OnDiagnosticsRequested;
    Closed += OnEditorClosed;
  }

  private void OnDiagnosticsRequested(string header, string diagnostics)
  {
    // Reuse a single non-modal diagnostics window for this editor.
    if (_diagnosticsWindow is null)
    {
      _diagnosticsWindow = new CompileDiagnosticsWindow { Owner = this };
      _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
    }

    _diagnosticsWindow.ShowDiagnostics(header, diagnostics);
  }

  private void OnEditorClosed(object? sender, System.EventArgs e)
  {
    _viewModel.DiagnosticsRequested -= OnDiagnosticsRequested;
    if (_diagnosticsWindow is not null)
    {
      _diagnosticsWindow.Close();
      _diagnosticsWindow = null;
    }
  }

  private void OnOnlineKrakenClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.KrakenRoute is not { IsHead: false } route)
    {
      MessageBox.Show(this, _viewModel.KrakenOnlineHint, "Online Kraken", MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    if (_viewModel.Node.Coordinate != route.Coordinate)
    {
      throw new InvalidOperationException("The node's Kraken route no longer matches the editor.");
    }

    KrakenNodeControlViewModel? controlViewModel = null;
    try
    {
      controlViewModel = new KrakenNodeControlViewModel(
          route,
          _viewModel.KrakenController,
          _viewModel.CompileGeneratedRomWords,
          _viewModel.CompileExpandedRomSource);
      var window = new KrakenNodeControlWindow(controlViewModel)
      {
        Owner = this
      };
      window.ShowDialog();
    }
    catch (OperationCanceledException)
    {
      // Normal cancellation during close is not an error.
    }
    catch (Exception exception)
    {
      // This wraps the whole dialog lifetime (open, use, and close), so the
      // failure is not necessarily an "open" failure. Report it plainly.
      MessageBox.Show(
          this,
          $"The Online Kraken window for node {_viewModel.NodeCoordinate} reported an error.\n\n{exception}",
          "Online Kraken error",
          MessageBoxButton.OK,
          MessageBoxImage.Error);

      if (controlViewModel is not null)
      {
        try
        {
          controlViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
          // Preserve the original UI error.
        }
      }
    }
  }

  private void OnSaveClick(object sender, RoutedEventArgs e)
  {
    DialogResult = true;
  }

  private void OnCopyToProjectClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.OtherProjects.Count == 0)
    {
      MessageBox.Show(
          this,
          "There are no other projects open to copy into. Create another project first.",
          "Copy to project",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      return;
    }

    var pickerViewModel = new CopyNodeToProjectViewModel(
        _viewModel.OtherProjects,
        _viewModel.ChipRole,
        _viewModel.NodeCoordinate);
    var picker = new CopyNodeToProjectWindow(pickerViewModel)
    {
      Owner = this
    };

    if (picker.ShowDialog() != true || pickerViewModel.SelectedProject is null)
    {
      return;
    }

    string message = _viewModel.CopyCurrentSourceTo(pickerViewModel.SelectedProject, pickerViewModel.SelectedRole);
    MessageBox.Show(this, message, "Copy to project", MessageBoxButton.OK, MessageBoxImage.Information);
  }
}