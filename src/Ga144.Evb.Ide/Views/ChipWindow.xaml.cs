using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Ga144.Evb.Ide.Views;

public partial class ChipWindow : Window
{
  private const int ChipColumns = 18;
  private const int ChipRows = 8;
  private const double NodeMargin = 8.0;

  private readonly ChipViewModel _viewModel;
  private KrakenCheckWindow? _krakenCheckWindow;
  private KrakenCheckViewModel? _krakenCheckViewModel;

  public ChipWindow(ChipViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Title = viewModel.Title;
    SourceInitialized += OnSourceInitialized;
    Loaded += OnLoaded;
    Closed += OnClosed;
    _viewModel.PropertyChanged += OnViewModelPropertyChanged;
  }


  private void OnSourceInitialized(object? sender, EventArgs e)
  {
    // The chip window contains 144 node controls plus the tentacle overlay.
    // On some Windows/GPU/USB-load combinations the previous transformed
    // Viewbox surface was visibly lost while the check window was updating.
    // Keep this one diagnostic/editor window on WPF's software composition
    // path so a display-driver present/reset cannot blank or misplace the
    // GA144 surface.  This does not affect Kraken timing or serial I/O.
    if (PresentationSource.FromVisual(this) is HwndSource source)
    {
      source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
    }
  }

  private void OnLoaded(object sender, RoutedEventArgs e)
  {
    // Draw after the stretching chip surface has received its real size.
    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(DrawKrakenPaths));
  }

  private void OnChipDrawingGridSizeChanged(object sender, SizeChangedEventArgs e)
  {
    // Arrow geometry is expressed in the *actual* chip-surface coordinate
    // system.  There is deliberately no Viewbox/render transform anymore.
    // Redraw only when the chip window itself changes size; Kraken check
    // progress updates do not touch this visual tree.
    if (_viewModel.KrakenActive && e.NewSize.Width > 1.0 && e.NewSize.Height > 1.0)
    {
      DrawKrakenPaths();
    }
  }

  private async void OnClosed(object? sender, EventArgs e)
  {
    _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    await _viewModel.DisposeAsync();
  }

  private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    // Tentacle arrows reflect runtime erection state (KrakenActive) and the
    // installed topology (KrakenInstalled: install/remove changes the routes).
    // Redraw on either. Other runtime status changes (endpoint text, etc.)
    // must not rebuild the large visual tree.
    if (e.PropertyName == nameof(ChipViewModel.KrakenInstalled)
        || e.PropertyName == nameof(ChipViewModel.KrakenActive))
    {
      Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(DrawKrakenPaths));
    }
  }

  private async void OnNodeClick(object sender, RoutedEventArgs e)
  {
    // Keep the chip visible while Check Kraken runs, but do not allow a
    // second online operation to be started concurrently with the path
    // check. The check window is modeless specifically to avoid the WPF
    // owner disable/enable + nested DispatcherFrame path that was causing
    // the large Viewbox to lose its layout after ShowDialog returned.
    if (_krakenCheckViewModel?.IsBusy == true)
    {
      _krakenCheckWindow?.Activate();
      return;
    }

    if (sender is not Button { Tag: NodeViewModel node })
    {
      return;
    }

    var editorViewModel = new NodeEditorViewModel(
      node.Model,
      _viewModel.Chip,
      _viewModel.RomLibrary,
      _viewModel.Project.Model.UserMacros,
      _viewModel.RomLibraryPath,
      _viewModel.Role,
      node.KrakenRoute,
      _viewModel.KrakenEndpointResolver,
      _viewModel.KrakenController);
    var editor = new NodeEditorWindow(editorViewModel)
    {
      Owner = this
    };

    if (editor.ShowDialog() == true)
    {
      var romChanged = editorViewModel.Apply();
      _viewModel.Project.NotifyProjectChanged();

      if (romChanged)
      {
        try
        {
          await _viewModel.SaveRomLibraryAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
          MessageBox.Show(
              this,
              $"The node was saved to the project, but the system-wide ROM library could not be saved.\n\n{exception.Message}",
              "ROM library save error",
              MessageBoxButton.OK,
              MessageBoxImage.Error);
        }
      }

      // Rebuild node presentation so configured-state and Kraken highlighting are refreshed.
      _viewModel.RefreshNodes();
      DrawKrakenPaths();
    }
  }

  private void OnCheckKrakenClick(object sender, RoutedEventArgs e)
  {
    if (!_viewModel.KrakenInstalled)
    {
      MessageBox.Show(this, "Install the Kraken topology on this chip before running the online check.", "Check Kraken", MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    // Do not use ShowDialog here. A modal owned window disables/re-enables
    // this Window and runs a nested DispatcherFrame. With the very large
    // GA144 ItemsControl/Viewbox/Canvas visual tree that caused WPF to
    // occasionally return with the chip drawing arranged incorrectly or
    // not rendered at all. Keeping the check as an owned *modeless* window
    // avoids that layout path entirely; it has no effect on Kraken serial
    // lifetime or exclusivity.
    if (_krakenCheckWindow is not null)
    {
      if (_krakenCheckWindow.WindowState == WindowState.Minimized)
      {
        _krakenCheckWindow.WindowState = WindowState.Normal;
      }

      _krakenCheckWindow.Activate();
      return;
    }

    try
    {
      var checkViewModel = new KrakenCheckViewModel(
        _viewModel.Chip.Kraken,
        _viewModel.KrakenController);
      var window = new KrakenCheckWindow(checkViewModel)
      {
        Owner = this
      };

      _krakenCheckViewModel = checkViewModel;
      _krakenCheckWindow = window;
      window.Closed += OnKrakenCheckWindowClosed;
      window.Show();
    }
    catch (Exception exception)
    {
      _krakenCheckViewModel = null;
      _krakenCheckWindow = null;
      MessageBox.Show(
          this,
          $"Unable to run the Kraken check.\n\n{exception}",
          "Check Kraken error",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
  }

  private void OnKrakenCheckWindowClosed(object? sender, EventArgs e)
  {
    if (sender is KrakenCheckWindow window)
    {
      window.Closed -= OnKrakenCheckWindowClosed;
    }

    _krakenCheckWindow = null;
    _krakenCheckViewModel = null;
    _viewModel.RefreshKrakenRuntimeStatus();

    // No InvalidateMeasure/Arrange/UpdateLayout calls are intentionally
    // made here. The chip visual tree stayed enabled and arranged while
    // the modeless check window was open, so normal WPF rendering is left
    // untouched.
  }


  private void DrawKrakenPaths()
  {
    KrakenPathCanvas.Children.Clear();
    // Arrows reflect runtime state: only draw when a Kraken is erected this
    // session. The persisted topology alone (KrakenInstalled) must not show
    // arrows after a restart, since the IDE cannot know the chip state then.
    if (!_viewModel.KrakenActive)
    {
      return;
    }

    IReadOnlyDictionary<int, KrakenNodeRoute> routes = _viewModel.KrakenRoutes;
    foreach (KrakenNodeRoute route in routes.Values
                 .Where(item => !item.IsHead && item.PreviousCoordinate.HasValue)
                 .OrderBy(item => item.TentacleNumber)
                 .ThenBy(item => item.Position))
    {
      int previous = route.PreviousCoordinate!.Value;
      DrawArrow(previous, route.Coordinate, BrushForTentacle(route.TentacleNumber));
    }
  }

  private void DrawArrow(int fromCoordinate, int toCoordinate, Brush brush)
  {
    double width = ChipDrawingGrid.ActualWidth;
    double height = ChipDrawingGrid.ActualHeight;
    if (width <= 1.0 || height <= 1.0)
    {
      return;
    }

    double cellWidth = width / ChipColumns;
    double cellHeight = height / ChipRows;
    Point fromCenter = Center(fromCoordinate, cellWidth, cellHeight);
    Point toCenter = Center(toCoordinate, cellWidth, cellHeight);
    Vector direction = toCenter - fromCenter;
    if (direction.LengthSquared < 0.5)
    {
      return;
    }

    direction.Normalize();
    bool horizontal = Math.Abs(direction.X) > Math.Abs(direction.Y);
    double halfCell = horizontal ? cellWidth / 2.0 : cellHeight / 2.0;
    // Stop the line just outside the node button.  Clamp the trim for very
    // small windows so geometry can never invert or produce NaNs.
    double trim = Math.Max(2.0, halfCell - NodeMargin + 1.0);

    Point start = fromCenter + direction * trim;
    Point end = toCenter - direction * trim;

    var line = new Line
    {
      X1 = start.X,
      Y1 = start.Y,
      X2 = end.X,
      Y2 = end.Y,
      Stroke = brush,
      StrokeThickness = 5,
      StrokeStartLineCap = PenLineCap.Round,
      StrokeEndLineCap = PenLineCap.Round,
      SnapsToDevicePixels = true
    };
    KrakenPathCanvas.Children.Add(line);

    const double arrowLength = 9.0;
    const double arrowHalfWidth = 5.0;
    Point arrowBase = end - direction * arrowLength;
    Vector perpendicular = new(-direction.Y, direction.X);
    var head = new Polygon
    {
      Fill = brush,
      Stroke = brush,
      StrokeThickness = 1,
      Points = new PointCollection
      {
        end,
        arrowBase + perpendicular * arrowHalfWidth,
        arrowBase - perpendicular * arrowHalfWidth
      }
    };
    KrakenPathCanvas.Children.Add(head);
  }

  private static Point Center(int coordinate, double cellWidth, double cellHeight)
  {
    int row = coordinate / 100;
    int column = coordinate % 100;
    int visualRow = (ChipRows - 1) - row;
    return new Point((column + 0.5) * cellWidth, (visualRow + 0.5) * cellHeight);
  }

  private static Brush BrushForTentacle(int tentacleNumber) => tentacleNumber switch
  {
    1 => Brushes.SteelBlue,
    2 => Brushes.DarkOrange,
    3 => Brushes.MediumPurple,
    _ => Brushes.DarkSlateGray
  };
}