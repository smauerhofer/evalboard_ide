using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class KrakenCheckWindow : Window
{
  private readonly KrakenCheckViewModel _viewModel;
  private bool _disposed;

  public KrakenCheckWindow(KrakenCheckViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Loaded += OnLoaded;
    Closed += OnClosed;
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    Loaded -= OnLoaded;
    await _viewModel.RunAsync();
  }

  private async void OnClosed(object? sender, EventArgs e)
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    await _viewModel.DisposeAsync();
  }
}
