using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Code-behind for the C Debugger window -- deliberately a near-duplicate of
/// <see cref="CvmDebuggerWindow"/>'s own code-behind, since every one of these handlers (core dump,
/// memory gutter click, log auto-scroll, copy log, window-close teardown) exists here for the exact
/// same reason it exists there: <see cref="CDebuggerViewModel.Debugger"/> is a genuine, independent
/// <see cref="CvmDebuggerViewModel"/> instance, not a lookalike, so it raises the exact same events and
/// exposes the exact same no-UI-dependency methods that view's own code-behind already handles.
/// </summary>
public partial class CDebuggerWindow : Window
{
  private readonly CDebuggerViewModel _viewModel;

  // Reused (Activate-if-open) the same way CvmDebuggerWindow.xaml.cs itself reuses this whole debugger
  // window -- one post-mortem chip window per debugger window, not a new one every Core Dump.
  private PostMortemChipWindow? _postMortemWindow;

  public CDebuggerWindow(CDebuggerViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    _viewModel.Debugger.CoreDumpCompleted += OnCoreDumpCompleted;
    Closed += OnClosed;
  }

  // Stops any in-flight Continue and releases the still-open serial port -- see
  // CvmDebuggerWindow.xaml.cs's own OnClosed remarks; the wrapped Debugger owns the port for its whole
  // life exactly the same way a top-level CvmDebuggerViewModel does.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.Debugger.CoreDumpCompleted -= OnCoreDumpCompleted;
    _viewModel.Debugger.Cancel();
  }

  private void OnCoreDumpCompleted(PostMortemSnapshot snapshot)
  {
    if (_postMortemWindow is not null)
    {
      _postMortemWindow.LoadSnapshot(snapshot);
      if (_postMortemWindow.WindowState == WindowState.Minimized)
      {
        _postMortemWindow.WindowState = WindowState.Normal;
      }

      _postMortemWindow.Activate();
      return;
    }

    var postMortemViewModel = new PostMortemChipViewModel(snapshot, _viewModel.Debugger.Chip, _viewModel.Debugger.RomLibrary, _viewModel.Debugger.UserMacros);
    var window = new PostMortemChipWindow(postMortemViewModel)
    {
      Owner = this
    };

    _postMortemWindow = window;
    window.Closed += OnPostMortemWindowClosed;
    window.Show();
  }

  private void OnPostMortemWindowClosed(object? sender, EventArgs e)
  {
    if (sender is PostMortemChipWindow window)
    {
      window.Closed -= OnPostMortemWindowClosed;
    }

    _postMortemWindow = null;
  }

  private void OnLogTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
      LogTextBox.ScrollToEnd();

  // Same gutter convention as CvmDebuggerWindow.xaml.cs's own OnMemoryGutterClick -- forwards the
  // clicked row's own FlatAddress straight to the wrapped Debugger, which owns the actual breakpoint
  // list and simulated SRAM.
  private void OnMemoryGutterClick(object sender, MouseButtonEventArgs e)
  {
    if (sender is FrameworkElement { DataContext: CvmMemoryRowViewModel row })
    {
      _viewModel.Debugger.ToggleBreakpointAtAddress(row.FlatAddress);
    }
  }

  private void OnCopyLogClick(object sender, RoutedEventArgs e)
  {
    try
    {
      Clipboard.SetText(LogTextBox.Text ?? string.Empty);
    }
    catch
    {
      // Clipboard can transiently fail if another process holds it; ignore.
    }
  }
}
