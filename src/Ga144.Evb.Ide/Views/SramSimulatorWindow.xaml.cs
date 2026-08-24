using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class SramSimulatorWindow : Window
{
  private readonly SramSimulatorViewModel _viewModel;

  public SramSimulatorWindow(SramSimulatorViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Closed += OnClosed;
  }

  // Same reasoning as SramTentacleWindow: this view model owns no COM handle or erected-Kraken
  // lifetime of its own, just requests through the already-shared KrakenLiveController, so teardown
  // is only cancelling any in-flight operation.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.Cancel();
  }

  private void OnLogTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
      LogTextBox.ScrollToEnd();
}
