using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class SramTentacleWindow : Window
{
  private readonly SramTentacleViewModel _viewModel;

  public SramTentacleWindow(SramTentacleViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Closed += OnClosed;
  }

  // Unlike KrakenNodeControlWindow, this view model owns no COM handle or
  // erected-Kraken lifetime of its own -- it only issues requests through the
  // already-shared KrakenLiveController -- so teardown is just cancelling any
  // in-flight operation, not an async close sequence.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.Cancel();
  }
}
