using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;
using System.Windows;
using Microsoft.Win32;

namespace Ga144.Evb.Ide.Views;

public partial class CvmDebuggerWindow : Window
{
  private readonly CvmDebuggerViewModel _viewModel;

  // Reused (Activate-if-open) the same way ChipWindow.xaml.cs itself reuses this whole debugger
  // window -- one post-mortem chip window per debugger window, not a new one every Core Dump.
  private PostMortemChipWindow? _postMortemWindow;

  public CvmDebuggerWindow(CvmDebuggerViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    _viewModel.CoreDumpCompleted += OnCoreDumpCompleted;
    Closed += OnClosed;
  }

  // Stops any in-flight Continue and releases the still-open serial port -- this session, unlike
  // SramSimulatorViewModel/SramTentacleViewModel's Kraken-request-based ones, owns the port for its
  // whole life, so closing the window must actually tear it down, not just cancel a pending call.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.CoreDumpCompleted -= OnCoreDumpCompleted;
    _viewModel.Cancel();
  }

  // The view model has no UI dependency of its own (same convention as LoadImageFile's own
  // OpenFileDialog above) -- it only builds the PostMortemSnapshot and raises this event; opening the
  // actual window happens here.
  private void OnCoreDumpCompleted(PostMortemSnapshot snapshot)
  {
    if (_postMortemWindow is not null)
    {
      // Goes through the window's own LoadSnapshot (not a DataContext swap) so its view model's
      // SnapshotReplaced event fires and closes every node window left over from the previous dump.
      _postMortemWindow.LoadSnapshot(snapshot);
      if (_postMortemWindow.WindowState == WindowState.Minimized)
      {
        _postMortemWindow.WindowState = WindowState.Normal;
      }

      _postMortemWindow.Activate();
      return;
    }

    var postMortemViewModel = new PostMortemChipViewModel(snapshot, _viewModel.Chip, _viewModel.RomLibrary, _viewModel.UserMacros);
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

  // The dialog itself lives here, not in the view model -- same convention as every other file-picking
  // action in this IDE (e.g. CProjectWindow's own "Add existing..." handlers): the view model has no UI
  // dependency of its own, so it only ever sees the resulting path, via LoadImageFile.
  private void OnLoadImageClick(object sender, RoutedEventArgs e)
  {
    var dialog = new OpenFileDialog { Title = "Load linked image", Filter = "CVM images (*.gaimg)|*.gaimg|All files (*.*)|*.*" };
    if (dialog.ShowDialog(this) == true)
    {
      _viewModel.LoadImageFile(dialog.FileName);
    }
  }

  // Same pattern as CompileDiagnosticsWindow's own "Copy all": the transaction log keeps growing and
  // auto-scrolling while a Continue is in flight, which makes manually click-dragging a selection
  // over it impractical -- a single button that grabs the whole log is the reliable way to copy it.
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