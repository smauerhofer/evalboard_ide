using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Code-behind for the C Debugger window -- deliberately a near-duplicate of
/// <see cref="CvmDebuggerWindow"/>'s own code-behind, since every one of these handlers (core dump,
/// memory row double-click, PC auto-scroll, log auto-scroll, copy log, window-close teardown) exists
/// here for the exact same reason it exists there: <see cref="CDebuggerViewModel.Debugger"/> is a
/// genuine, independent <see cref="CvmDebuggerViewModel"/> instance, not a lookalike, so it raises the
/// exact same events and exposes the exact same no-UI-dependency methods that view's own code-behind
/// already handles.
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
    _viewModel.Debugger.MemoryRows.CollectionChanged += OnMemoryRowsCollectionChanged;
    Closed += OnClosed;
  }

  // Stops any in-flight Continue and releases the still-open serial port -- see
  // CvmDebuggerWindow.xaml.cs's own OnClosed remarks; the wrapped Debugger owns the port for its whole
  // life exactly the same way a top-level CvmDebuggerViewModel does.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.Debugger.CoreDumpCompleted -= OnCoreDumpCompleted;
    _viewModel.Debugger.MemoryRows.CollectionChanged -= OnMemoryRowsCollectionChanged;
    _viewModel.Debugger.Cancel();
  }

  // Same PC auto-scroll convention as CvmDebuggerWindow.xaml.cs's own OnMemoryRowsCollectionChanged --
  // see its own remarks. Subscribed to Debugger.MemoryRows directly, since Debugger (above) is a
  // genuine CvmDebuggerViewModel instance, not a lookalike.
  private void OnMemoryRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
  {
    if (e.NewItems is null)
    {
      return;
    }

    foreach (object item in e.NewItems)
    {
      if (item is CvmMemoryRowViewModel { IsCurrentPc: true } row)
      {
        MemoryListView.ScrollIntoView(row);
        break;
      }
    }
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

  // Same double-click convention as CvmDebuggerWindow.xaml.cs's own OnMemoryRowDoubleClick -- a single
  // click only selects a row, and a double-click anywhere in it forwards that row's own FlatAddress
  // straight to the wrapped Debugger, which owns the actual breakpoint list and simulated SRAM.
  private void OnMemoryRowDoubleClick(object sender, MouseButtonEventArgs e)
  {
    if (e.OriginalSource is DependencyObject source
        && FindAncestor<ListViewItem>(source) is not null
        && sender is ListView { SelectedItem: CvmMemoryRowViewModel row })
    {
      _viewModel.Debugger.ToggleBreakpointAtAddress(row.FlatAddress);
    }
  }

  // Same as CvmDebuggerWindow.xaml.cs's own FindAncestor -- walks up the visual tree from a clicked
  // element to find its nearest ancestor of type T, or null if none exists (e.g. a double-click landed
  // in the ListView's own empty space below the last row).
  private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
  {
    while (current is not null)
    {
      if (current is T match)
      {
        return match;
      }

      current = VisualTreeHelper.GetParent(current);
    }

    return null;
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