using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class CvmDebuggerWindow : Window
{
  private readonly CvmDebuggerViewModel _viewModel;

  public CvmDebuggerWindow(CvmDebuggerViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Closed += OnClosed;
  }

  // Stops any in-flight Continue and releases the still-open serial port -- this session, unlike
  // SramSimulatorViewModel/SramTentacleViewModel's Kraken-request-based ones, owns the port for its
  // whole life, so closing the window must actually tear it down, not just cancel a pending call.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.Cancel();
  }

  private void OnLogTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
      LogTextBox.ScrollToEnd();
}
