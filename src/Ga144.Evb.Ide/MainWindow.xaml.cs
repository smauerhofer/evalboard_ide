using Ga144.Evb.Ide.Controls;
using Ga144.Evb.Ide.Services;
using Ga144.Evb.Ide.ViewModels;
using Ga144.Evb.Ide.Views;
using System.ComponentModel;
using System.Windows;

namespace Ga144.Evb.Ide;

public partial class MainWindow : Window
{
  private readonly MainWindowViewModel _viewModel;
  private SerialDeviceChangeWatcher? _deviceWatcher;
  private bool _closeCompleted;
  private MacroEditorWindow? _macroEditor;

  public MainWindow(MainWindowViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Loaded += OnLoaded;
    Closing += OnClosing;
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    // Replace the former ~1.5 s polling scan with event-driven discovery:
    // enumerate only when Windows reports a real USB arrival/removal. This
    // removes the continuous USB device-tree activity that stalled a mouse
    // sharing a single xHCI controller through the KVM.
    _deviceWatcher ??= new SerialDeviceChangeWatcher(this, _viewModel.RequestDeviceChangeScan);
    _deviceWatcher.Start();

    await _viewModel.InitializeAsync();
  }

  private async void OnChipRequested(object? sender, ChipRequestedEventArgs e)
  {
    if (_viewModel.SelectedProject is null)
    {
      return;
    }

    try
    {
      KrakenLiveController krakenController = _viewModel.GetKrakenController(_viewModel.SelectedProject, e.Role);
      var window = new ChipWindow(new ChipViewModel(
          _viewModel.SelectedProject,
          e.Role,
          _viewModel.RomLibrary,
          _viewModel.RomLibraryPath,
          _viewModel.SaveRomLibraryAsync,
          () => _viewModel.ResolveKrakenEndpoint(e.Role),
          krakenController))
      {
        Owner = this
      };

      // Stop new probes and, critically, wait for any timer-started
      // probe that was already in progress to finish before the chip
      // window can erect Kraken. Once Kraken is live, resumed scans are
      // metadata-only and never open a serial port for active probing.
      await _viewModel.SuspendSerialScanningAsync();
      try
      {
        window.ShowDialog();
      }
      finally
      {
        _viewModel.ResumeSerialScanning();
      }
    }
    catch (Exception exception)
    {
      MessageBox.Show(
          this,
          "Unable to open the GA144 chip window.\n\n" + exception.Message,
          "GA144 chip error",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
  }


  private async void OnBoardPortRequested(object? sender, BoardPortRequestedEventArgs e)
  {
    await _viewModel.AssignSelectedPortToBoardPortAsync(e.Role);
  }


  private void OnMacrosClick(object sender, RoutedEventArgs e)
  {
    // Only one macro editor at a time: if one is already open, bring it to the
    // front instead of opening a second, non-modal window.
    if (_macroEditor is not null)
    {
      if (_macroEditor.WindowState == WindowState.Minimized)
      {
        _macroEditor.WindowState = WindowState.Normal;
      }

      _macroEditor.Activate();
      return;
    }

    if (_viewModel.SelectedProject is null)
    {
      return;
    }

    try
    {
      var editorViewModel = new MacroEditorViewModel(
          _viewModel.SelectedProject.Model,
          _viewModel.RomLibrary,
          _viewModel.RomLibraryPath);
      var editor = new MacroEditorWindow(editorViewModel)
      {
        Owner = this
      };
      _macroEditor = editor;

      // Non-modal: show the editor without blocking, and run the save logic when it
      // closes after the user saved (the editor sets Saved = true on Save; it is
      // shown non-modally so DialogResult cannot be used). Clearing _macroEditor on
      // close lets the button open a fresh one next time while preventing a second
      // window from being opened while this one is active.
      editor.Closed += async (_, _) =>
      {
        _macroEditor = null;
        if (!editor.Saved)
        {
          return;
        }

        await PersistMacroChangesAsync(editorViewModel);
      };

      editor.Show();
    }
    catch (Exception exception)
    {
      // Opening failed; drop any partially-created editor so the button works again.
      _macroEditor = null;
      MessageBox.Show(
          this,
          "The F18 macro editor could not be opened.\n\n" + exception,
          "F18 macro editor error",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
  }

  // Persist macro edits made in the (non-modal) macro editor: the system ROM
  // library and/or the project workspace, depending on what changed. Surfaces any
  // file-write failures without discarding the in-memory updates.
  private async Task PersistMacroChangesAsync(MacroEditorViewModel editorViewModel)
  {
    var saveErrors = new List<string>();
    if (editorViewModel.SystemChanged)
    {
      try
      {
        await _viewModel.SaveRomLibraryAsync();
      }
      catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
      {
        saveErrors.Add($"System macro library: {exception.Message}");
      }
    }

    if (editorViewModel.UserChanged)
    {
      _viewModel.SelectedProject?.NotifyProjectChanged();
      try
      {
        await _viewModel.SaveWorkspaceImmediatelyAsync();
      }
      catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
      {
        saveErrors.Add($"Project workspace: {exception.Message}");
      }
    }

    if (saveErrors.Count > 0)
    {
      MessageBox.Show(
          this,
          "The macro definitions were updated in memory, but one or more files could not be persisted.\n\n" +
          string.Join(Environment.NewLine, saveErrors),
          "Macro save error",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
    }
  }

  private async void OnClosing(object? sender, CancelEventArgs e)
  {
    if (_closeCompleted)
    {
      return;
    }

    e.Cancel = true;
    IsEnabled = false;
    try
    {
      _deviceWatcher?.Dispose();
      _deviceWatcher = null;
      await _viewModel.DisposeAsync();
    }
    finally
    {
      _closeCompleted = true;
      Close();
    }
  }
}