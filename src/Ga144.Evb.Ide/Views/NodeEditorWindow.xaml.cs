using Ga144.Evb.Ide.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// ADDED 2026-10-01, alongside splitting the old single "Save node" button into "Save" and "Save &amp;
/// Close" -- see <see cref="NodeEditorWindow.Saved"/>'s own remarks. Both buttons raise the same event;
/// this is the one piece of information the owner needs to tell them apart: whether to close the editor
/// once the save itself is done.
/// </summary>
public sealed class NodeEditorSavedEventArgs(bool closeAfterSave) : EventArgs
{
  public bool CloseAfterSave { get; } = closeAfterSave;
}

public partial class NodeEditorWindow : Window
{
  private readonly NodeEditorViewModel _viewModel;
  private CompileDiagnosticsWindow? _diagnosticsWindow;

  public NodeEditorWindow(NodeEditorViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Title = $"Node {viewModel.NodeCoordinate} editor";
    _viewModel.DiagnosticsRequested += OnDiagnosticsRequested;
    Closed += OnEditorClosed;
  }

  /// <summary>
  /// Raised when "Save" or "Save &amp; Close" is clicked. Non-modal (this window is opened with
  /// <c>Show()</c>, not <c>ShowDialog()</c> -- see ChipWindow.OnNodeClick's own remarks), so there is no
  /// DialogResult for the owner to read back after the fact: the owner subscribes to this instead, does
  /// the actual apply/refresh work (NodeEditorViewModel.Apply, saving the ROM library, redrawing the
  /// chip), and closes this window itself -- but ONLY when <see cref="NodeEditorSavedEventArgs.CloseAfterSave"/>
  /// says to.
  ///
  /// SPLIT 2026-10-01, per Stefan: "'Save node' must be renamed to 'Save' and must not close the node
  /// window, just save the changes. A new button 'Save &amp; Close' must be placed right to 'Save' which
  /// saves all the changes and closes the node window." Before this, a single "Save node" button always
  /// implied close-after-save (the exact same division of labor the old ShowDialog()==true branch had,
  /// just event-driven instead of return-value-driven) -- "Save &amp; Close" (<see cref="OnSaveAndCloseClick"/>)
  /// keeps that old behavior; "Save" (<see cref="OnSaveClick"/>) is new, and leaves the window open so
  /// editing can continue immediately after a save.
  /// </summary>
  public event EventHandler<NodeEditorSavedEventArgs>? Saved;

  private void OnDiagnosticsRequested(string header, string diagnostics)
  {
    // Reuse a single non-modal diagnostics window for this editor.
    if (_diagnosticsWindow is null)
    {
      _diagnosticsWindow = new CompileDiagnosticsWindow { Owner = this };
      _diagnosticsWindow.Closed += (_, _) => _diagnosticsWindow = null;
    }

    _diagnosticsWindow.ShowDiagnostics(header, diagnostics);
  }

  private void OnEditorClosed(object? sender, System.EventArgs e)
  {
    _viewModel.DiagnosticsRequested -= OnDiagnosticsRequested;
    if (_diagnosticsWindow is not null)
    {
      _diagnosticsWindow.Close();
      _diagnosticsWindow = null;
    }
  }

  private void OnOnlineKrakenClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.KrakenRoute is not { IsHead: false } route)
    {
      MessageBox.Show(this, _viewModel.KrakenOnlineHint, "Online Kraken", MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    if (_viewModel.Node.Coordinate != route.Coordinate)
    {
      throw new InvalidOperationException("The node's Kraken route no longer matches the editor.");
    }

    KrakenNodeControlViewModel? controlViewModel = null;
    try
    {
      controlViewModel = new KrakenNodeControlViewModel(
          route,
          _viewModel.KrakenController,
          _viewModel.CompileGeneratedRomWords,
          _viewModel.CompileExpandedRomSource);
      var window = new KrakenNodeControlWindow(controlViewModel)
      {
        Owner = this
      };
      window.ShowDialog();
    }
    catch (OperationCanceledException)
    {
      // Normal cancellation during close is not an error.
    }
    catch (Exception exception)
    {
      // This wraps the whole dialog lifetime (open, use, and close), so the
      // failure is not necessarily an "open" failure. Report it plainly.
      MessageBox.Show(
          this,
          $"The Online Kraken window for node {_viewModel.NodeCoordinate} reported an error.\n\n{exception}",
          "Online Kraken error",
          MessageBoxButton.OK,
          MessageBoxImage.Error);

      if (controlViewModel is not null)
      {
        try
        {
          controlViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
          // Preserve the original UI error.
        }
      }
    }
  }

  /// <summary>Saves without closing -- see <see cref="Saved"/>'s own remarks on the 2026-10-01 split.</summary>
  private void OnSaveClick(object sender, RoutedEventArgs e)
  {
    Saved?.Invoke(this, new NodeEditorSavedEventArgs(closeAfterSave: false));
  }

  /// <summary>Saves and closes -- the old "Save node" behavior. See <see cref="Saved"/>'s own remarks.</summary>
  private void OnSaveAndCloseClick(object sender, RoutedEventArgs e)
  {
    Saved?.Invoke(this, new NodeEditorSavedEventArgs(closeAfterSave: true));
  }

  private void OnCancelClick(object sender, RoutedEventArgs e)
  {
    TryCancel();
  }

  // Preserves the old IsCancel="True" Escape-to-close convenience now that this window is non-modal
  // (IsCancel itself only works on a window shown via ShowDialog -- it sets DialogResult, which throws
  // on a Show()-opened window). Routed through TryCancel (not a bare Close()) so Escape gets the exact
  // same discard-confirmation Cancel does -- see TryCancel's own remarks; otherwise Escape would be a
  // silent back door around the prompt Cancel itself now shows.
  private void OnPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key == Key.Escape)
    {
      TryCancel();
      e.Handled = true;
    }
  }

  /// <summary>
  /// ADDED 2026-10-01, per Stefan: "if the node window, after I modified the source and press cancel, a
  /// popup dialog must open asking 'Do you want discard the modifications?' with the option 'Yes' and
  /// 'No'." "Yes" discards whatever is only sitting in the editor's own fields right now (nothing this
  /// session has done since the last Save/Save &amp; Close, if any -- see
  /// <see cref="NodeEditorViewModel.IsDirty"/>'s own remarks) and closes the window; "No" leaves the
  /// window open with every edit untouched. Skips the prompt entirely when
  /// <see cref="NodeEditorViewModel.IsDirty"/> is false -- there is nothing to discard, so Cancel/Escape
  /// just close immediately, exactly as they always did.
  /// </summary>
  private void TryCancel()
  {
    if (_viewModel.IsDirty)
    {
      MessageBoxResult result = MessageBox.Show(
          this,
          "Do you want discard the modifications?",
          "Cancel",
          MessageBoxButton.YesNo,
          MessageBoxImage.Question);

      if (result != MessageBoxResult.Yes)
      {
        return;
      }
    }

    Close();
  }

  // Clears this node's own color override so it goes back to following the project's default.
  private void OnUseProjectDefaultColorClick(object sender, RoutedEventArgs e)
  {
    _viewModel.UseProjectDefaultColor();
  }

  private void OnCopyToProjectClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.OtherProjects.Count == 0)
    {
      MessageBox.Show(
          this,
          "There are no other projects open to copy into. Create another project first.",
          "Copy to project",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      return;
    }

    var pickerViewModel = new CopyNodeToProjectViewModel(
        _viewModel.OtherProjects,
        _viewModel.ChipRole,
        $"Copy node {_viewModel.NodeCoordinate}'s RAM source and startup state into the same node " +
        "coordinate in another project. ROM is shared across every project and does not need to be copied.");
    var picker = new CopyNodeToProjectWindow(pickerViewModel)
    {
      Owner = this
    };

    if (picker.ShowDialog() != true || pickerViewModel.SelectedProject is null)
    {
      return;
    }

    string message = _viewModel.CopyCurrentSourceTo(pickerViewModel.SelectedProject, pickerViewModel.SelectedRole);
    MessageBox.Show(this, message, "Copy to project", MessageBoxButton.OK, MessageBoxImage.Information);
  }

  // The RAM/ROM source tabs' line-number gutter (NodeEditorWindow.xaml's own remarks:
  // SourceLineNumberGutterText/RomSourceLineNumberGutterText) puts the actual editable text in a
  // multi-line TextBox with its own scrollbars turned off (VerticalScrollBarVisibility="Disabled"),
  // relying on an ANCESTOR ScrollViewer to do the real scrolling so the gutter and the text always move
  // together. A known WPF quirk defeats the mouse wheel there: a multi-line TextBox's built-in
  // PART_ContentHost scroll viewer still swallows the (bubbling) MouseWheel event before it can reach
  // that ancestor, even though the TextBox itself has nothing left to scroll -- so without this handler,
  // spinning the wheel over the source text (or its gutter) does nothing at all. Wiring this to the
  // outer ScrollViewer's PreviewMouseWheel (a TUNNELING event, delivered top-down before the TextBox
  // ever gets a chance to swallow the later bubbling MouseWheel) lets it apply the scroll and mark the
  // event handled first, so the wheel always scrolls the whole gutter+source pair together no matter
  // where the cursor is over it -- the gutter, the source text, or the text's own independent
  // horizontal-scroll region.
  private void OnGutteredSourceScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
  {
    if (sender is not ScrollViewer scrollViewer)
    {
      return;
    }

    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
    e.Handled = true;
  }
}