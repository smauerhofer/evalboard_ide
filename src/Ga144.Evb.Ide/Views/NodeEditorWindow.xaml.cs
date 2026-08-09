using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class NodeEditorWindow : Window
{
  private readonly NodeEditorViewModel _viewModel;

  public NodeEditorWindow(NodeEditorViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Title = $"Node {viewModel.NodeCoordinate} editor";
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
          _viewModel.CompileGeneratedRomWords);
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
}