using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class KrakenNodeControlWindow : Window
{
  private readonly KrakenNodeControlViewModel _viewModel;
  private bool _closeCompleted;

  public KrakenNodeControlWindow(KrakenNodeControlViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Title = $"Online Kraken — node {viewModel.NodeCoordinate}";
    Loaded += OnLoaded;
    Closing += OnClosing;
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    Loaded -= OnLoaded;
    try
    {
      await _viewModel.InitializeAsync();
    }
    catch
    {
      // Initialization is status-only; a failure must not escape the dialog
      // message loop (which the owner would misreport as "unable to open").
    }
  }

  private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
  {
    if (_closeCompleted)
    {
      // Second pass: the deferred close below re-raises Closing. Allow it.
      return;
    }

    // WPF forbids calling Close() (or Show/ShowDialog/setting Visibility) while a
    // window is inside its Closing event. Cancel this close, run async teardown,
    // then re-issue the close on the dispatcher so it happens AFTER this Closing
    // sequence has unwound rather than re-entrantly inside it (which throws
    // InvalidOperationException: "Cannot ... call ... Close ... while a Window is
    // closing").
    e.Cancel = true;
    IsEnabled = false;
    try
    {
      await _viewModel.DisposeAsync();
    }
    catch
    {
      // Teardown is best-effort and must never block or throw out of the close.
    }
    finally
    {
      _closeCompleted = true;
      // Fire-and-forget: the close must run after this Closing event unwinds.
      _ = Dispatcher.BeginInvoke(new Action(Close));
    }
  }
}