using System.Windows;
using Ga144.Evb.Ide.ViewModels;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Non-modal simulator detail window for one node -- opened by <see cref="SimulatorChipWindow"/>, which
/// sets <see cref="Window.Owner"/> to itself so every one of these closes automatically when the chip
/// window closes (WPF's own owned-window mechanism, same as <see cref="PostMortemNodeWindow"/>).
///
/// Unlike the post-mortem window (a frozen snapshot), this one is live: it subscribes to the chip view
/// model's own <see cref="SimulatorChipViewModel.Stepped"/> event so it re-<see cref="SimulatorNodeDetailViewModel.Refresh"/>es
/// itself after every Reset/Preset/Step/Go tick, and unsubscribes in its own <see cref="OnClosed"/> -- the exact
/// leak that was caught and avoided in <see cref="SimulatorChipViewModel.BuildNodeDetail"/> (which
/// deliberately does not subscribe on the view model's behalf, since it has no matching "window closed"
/// signal of its own).
/// </summary>
public partial class SimulatorNodeWindow : Window
{
  private readonly SimulatorNodeDetailViewModel _viewModel;
  private readonly SimulatorChipViewModel _chipViewModel;

  public SimulatorNodeWindow(SimulatorNodeDetailViewModel viewModel, SimulatorChipViewModel chipViewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    _chipViewModel = chipViewModel;
    DataContext = viewModel;

    // BuildNodeDetail deliberately does not call Refresh() itself (see its own remarks) -- this window
    // is the one place that both knows it is about to be shown AND will unsubscribe again on Closed, so
    // the initial paint and the live-refresh subscription belong together here.
    _viewModel.Refresh();
    _chipViewModel.Stepped += OnStepped;
    Closed += OnClosed;
  }

  private void OnStepped(object? sender, EventArgs e) => _viewModel.Refresh();

  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _chipViewModel.Stepped -= OnStepped;
  }

  // Same clipboard convention as PostMortemNodeWindow's own Copy registers & stacks button.
  private void OnCopyRegistersAndStacksClick(object sender, RoutedEventArgs e)
  {
    try
    {
      Clipboard.SetText(_viewModel.BuildClipboardText());
    }
    catch (System.Runtime.InteropServices.ExternalException)
    {
      // The clipboard can transiently fail if another process holds it; ignore.
    }
  }
}