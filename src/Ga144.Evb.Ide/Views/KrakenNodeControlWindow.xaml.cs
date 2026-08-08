using System.Windows;
using Ga144.Evb.Ide.ViewModels;

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
        await _viewModel.InitializeAsync();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closeCompleted)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;
        try
        {
            await _viewModel.DisposeAsync();
        }
        finally
        {
            _closeCompleted = true;
            Close();
        }
    }
}
