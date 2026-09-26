using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    _viewModel.MemoryRows.CollectionChanged += OnMemoryRowsCollectionChanged;
    Closed += OnClosed;
  }

  // Stops any in-flight Continue and releases the still-open serial port -- this session, unlike
  // SramSimulatorViewModel/SramTentacleViewModel's Kraken-request-based ones, owns the port for its
  // whole life, so closing the window must actually tear it down, not just cancel a pending call.
  private void OnClosed(object? sender, EventArgs e)
  {
    Closed -= OnClosed;
    _viewModel.CoreDumpCompleted -= OnCoreDumpCompleted;
    _viewModel.MemoryRows.CollectionChanged -= OnMemoryRowsCollectionChanged;
    _viewModel.Cancel();
  }

  // Keeps the current-PC row visible in the "Simulated SRAM" list while stepping, per Stefan: "when
  // stepping through a program, keep the line where the PC is visible in the Simulated SRAM area."
  // CvmDebuggerViewModel.RefreshMemoryView (see its own remarks) clears MemoryRows and re-adds every
  // row from scratch on every Step/Continue/Run/Pause/breakpoint toggle, so this fires once per
  // refresh for whichever single newly-added row is the current PC. If the PC's own address falls
  // outside the window MemoryBaseText/"Show from:" is currently showing, no row here has IsCurrentPc
  // set, so there is nothing to scroll to -- this only keeps the PC's line visible within whatever
  // page is already being shown, it does not change pages to follow the PC.
  //
  // The actual ScrollIntoView call is deferred to a queued Dispatcher callback rather than called
  // synchronously here, because this handler itself runs synchronously INSIDE the Add() call that
  // raised it -- RefreshMemoryView's own Clear()/Add() loop is still mid-way through adding the
  // remaining rows when the PC's own row goes in. ScrollIntoView triggers an immediate layout pass
  // (ListBox.ScrollIntoView -> OnBringItemIntoView -> UpdateLayout), which reenters the
  // ItemContainerGenerator while its own bookkeeping of "how many adds/removes have I seen since the
  // last Reset" is still short of MemoryRows' actual, still-growing count -- observed in practice as
  // "An ItemsControl is inconsistent with its items source" / "Accumulated count 4 is different from
  // actual count 5". Posting at Background priority (lower than the Normal/Render/Loaded priorities
  // WPF itself uses for pending layout work) guarantees this runs only once the current call stack has
  // fully unwound -- i.e. RefreshMemoryView's loop has finished adding every row and the generator has
  // caught up -- rather than mid-mutation.
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
        Dispatcher.BeginInvoke(() => MemoryListView.ScrollIntoView(row), DispatcherPriority.Background);
        break;
      }
    }
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

  // The simulated SRAM inspector's whole row (originally just the gutter column, added 2026-09-26;
  // widened to the whole row and switched from a single click to a double-click by Stefan's own
  // same-day follow-up: "a double-click in a Simulated SRAM row must toggle a breakpoint to this
  // address. a single-click only selects the row"). A single click still only selects, same as any
  // ordinary ListView, since no handler here runs on it. A double-click anywhere in the row toggles a
  // breakpoint at that row's own address, regardless of which column (or glyph, if any) was actually
  // under the pointer. The view model has no UI dependency of its own (same convention as every other
  // click handler in this file); the only extra work here is confirming the double-click actually
  // landed on a row and not empty space below the last one, which would otherwise still raise
  // MouseDoubleClick on the ListView without having changed SelectedItem.
  private void OnMemoryRowDoubleClick(object sender, MouseButtonEventArgs e)
  {
    if (e.OriginalSource is DependencyObject source
        && FindAncestor<ListViewItem>(source) is not null
        && sender is ListView { SelectedItem: CvmMemoryRowViewModel row })
    {
      _viewModel.ToggleBreakpointAtAddress(row.FlatAddress);
    }
  }

  // Walks up the visual tree from a clicked element to find its nearest ancestor of type T, or null if
  // none exists -- e.g. a double-click landed in the ListView's own empty space below the last row,
  // where there is no ListViewItem to find.
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