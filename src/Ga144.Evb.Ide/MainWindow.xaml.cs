using Ga144.Evb.Ide.Controls;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using Ga144.Evb.Ide.ViewModels;
using Ga144.Evb.Ide.Views;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ga144.Evb.Ide;

/// <summary>One row in MainWindow's own "libs"/"prgs" project lists (see <see
/// cref="MainWindow.RefreshCWorkspaceProjectLists"/>) -- just enough to display a name and open the
/// project on double-click, without pulling <c>Models.CProject</c> itself into any binding.</summary>
public sealed record CWorkspaceProjectEntry(string Name, string RootPath);

public partial class MainWindow : Window
{
  private readonly MainWindowViewModel _viewModel;
  private readonly CProjectStore _cProjectStore = new();
  // One CProjectWindow per open root folder (case-insensitive on Windows), so opening a C project
  // that's already open activates the existing window instead of a second one -- "Multiple C
  // projects can be open at any time" means multiple *different* projects, not multiple windows
  // onto the same one.
  private readonly Dictionary<string, CProjectWindow> _openCProjectWindows = new(StringComparer.OrdinalIgnoreCase);
  private SerialDeviceChangeWatcher? _deviceWatcher;
  private bool _closeCompleted;
  private MacroEditorWindow? _macroEditor;

  // The two grouped project lists shown in MainWindow's own "Libraries"/"Programs" panel (see
  // RefreshCWorkspaceProjectLists), rebuilt from disk whenever the C workspace root changes.
  public ObservableCollection<CWorkspaceProjectEntry> CLibraryProjects { get; } = [];
  public ObservableCollection<CWorkspaceProjectEntry> CProgramProjects { get; } = [];

  public MainWindow(MainWindowViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
    Loaded += OnLoaded;
    Closing += OnClosing;
    _viewModel.CWorkspaceProjectsChanged += (_, _) => RefreshCWorkspaceProjectLists();
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    // Replace the former ~1.5 s polling scan with event-driven discovery:
    // enumerate only when Windows reports a real USB arrival/removal. This
    // removes the continuous USB device-tree activity that stalled a mouse
    // sharing a single xHCI controller through the KVM.
    _deviceWatcher ??= new SerialDeviceChangeWatcher(this, _viewModel.RequestDeviceChangeScan);
    _deviceWatcher.Start();

    LibraryProjectsListBox.ItemsSource = CLibraryProjects;
    ProgramProjectsListBox.ItemsSource = CProgramProjects;

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
          krakenController,
          _viewModel.Projects))
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

  private void OnNewCProjectClick(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(_viewModel.CWorkspaceRootPath) && !ChooseCWorkspaceRoot())
    {
      return;
    }

    var dialogViewModel = new NewCProjectViewModel(_viewModel.CLibsDirectoryPath!, _viewModel.CPrgsDirectoryPath!);
    var dialog = new NewCProjectWindow(dialogViewModel) { Owner = this };
    if (dialog.ShowDialog() != true)
    {
      return;
    }

    try
    {
      CProject project = _cProjectStore.Create(dialogViewModel.ResolvedRootPath, dialogViewModel.Name, dialogViewModel.Kind);
      OpenOrActivateCProjectWindow(project);
      RefreshCWorkspaceProjectLists();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException)
    {
      MessageBox.Show(this, exception.Message, "New C project", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private void OnChooseCWorkspaceClick(object sender, RoutedEventArgs e) => ChooseCWorkspaceRoot();

  // Prompts for a folder to use as the shared C workspace root (containing "libs"/"prgs"), creating
  // those two subfolders if they don't already exist. Returns true if a root ended up configured
  // (either just chosen, or already set from before) -- callers that need a root before proceeding
  // (New C Project) check this rather than assuming the prompt was accepted.
  private bool ChooseCWorkspaceRoot()
  {
    var dialog = new OpenFolderDialog { Title = "Choose the C workspace folder (will contain \"libs\" and \"prgs\")" };
    if (Directory.Exists(_viewModel.CWorkspaceRootPath))
    {
      dialog.InitialDirectory = _viewModel.CWorkspaceRootPath;
    }

    if (dialog.ShowDialog(this) != true)
    {
      return !string.IsNullOrWhiteSpace(_viewModel.CWorkspaceRootPath);
    }

    try
    {
      Directory.CreateDirectory(Path.Combine(dialog.FolderName, "libs"));
      Directory.CreateDirectory(Path.Combine(dialog.FolderName, "prgs"));
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      MessageBox.Show(this, exception.Message, "C workspace", MessageBoxButton.OK, MessageBoxImage.Error);
      return !string.IsNullOrWhiteSpace(_viewModel.CWorkspaceRootPath);
    }

    _viewModel.CWorkspaceRootPath = dialog.FolderName;
    return true;
  }

  private void OnCWorkspaceProjectDoubleClick(object sender, MouseButtonEventArgs e)
  {
    if (sender is ListBox { SelectedItem: CWorkspaceProjectEntry entry })
    {
      OpenCProjectFolder(entry.RootPath);
    }
  }

  private void RefreshCWorkspaceProjectLists()
  {
    PopulateCWorkspaceProjectList(CLibraryProjects, _viewModel.CLibsDirectoryPath);
    PopulateCWorkspaceProjectList(CProgramProjects, _viewModel.CPrgsDirectoryPath);
  }

  private void PopulateCWorkspaceProjectList(ObservableCollection<CWorkspaceProjectEntry> target, string? directoryPath)
  {
    target.Clear();
    if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
    {
      return;
    }

    var entries = new List<CWorkspaceProjectEntry>();
    foreach (string folder in Directory.EnumerateDirectories(directoryPath))
    {
      if (!_cProjectStore.IsProjectFolder(folder))
      {
        continue;
      }

      string name;
      try
      {
        name = _cProjectStore.Load(folder).Name;
      }
      catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
      {
        name = Path.GetFileName(folder);
      }

      entries.Add(new CWorkspaceProjectEntry(name, Path.GetFullPath(folder)));
    }

    foreach (CWorkspaceProjectEntry entry in entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
    {
      target.Add(entry);
    }
  }

  private void OnOpenCProjectClick(object sender, RoutedEventArgs e)
  {
    var dialog = new OpenFolderDialog { Title = "Open C project (choose its own folder)" };
    string defaultLocation = DefaultCProjectLocation();
    if (Directory.Exists(defaultLocation))
    {
      dialog.InitialDirectory = defaultLocation;
    }

    if (dialog.ShowDialog(this) != true)
    {
      return;
    }

    OpenCProjectFolder(dialog.FolderName);
  }

  private void OnRecentCProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (sender is not ComboBox comboBox || comboBox.SelectedItem is not string path)
    {
      return;
    }

    comboBox.SelectedIndex = -1;
    OpenCProjectFolder(path);
  }

  private void OpenCProjectFolder(string rootPath)
  {
    try
    {
      CProject project = _cProjectStore.Load(rootPath);
      OpenOrActivateCProjectWindow(project);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
        or InvalidDataException or ArgumentException)
    {
      MessageBox.Show(this, exception.Message, "Open C project", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  private void OpenOrActivateCProjectWindow(CProject project)
  {
    if (_openCProjectWindows.TryGetValue(project.RootPath, out CProjectWindow? existing))
    {
      if (existing.WindowState == WindowState.Minimized)
      {
        existing.WindowState = WindowState.Normal;
      }

      existing.Activate();
      return;
    }

    var window = new CProjectWindow(
        new CProjectViewModel(project, _cProjectStore, _viewModel.CLibsDirectoryPath),
        _viewModel.TryResolveCvmDebuggerViewModel)
    { Owner = this };
    _openCProjectWindows[project.RootPath] = window;
    window.Closed += (_, _) => _openCProjectWindows.Remove(project.RootPath);
    window.Show();

    _viewModel.RecordRecentCProjectPath(project.RootPath);
  }

  // The parent of the most recently opened/created C project, if any, so the next "New"/"Open"
  // dialog starts somewhere useful instead of always the same default folder.
  private string DefaultCProjectLocation()
  {
    string? mostRecent = _viewModel.RecentCProjectPaths.FirstOrDefault();
    if (mostRecent is not null)
    {
      string? parent = Path.GetDirectoryName(mostRecent);
      if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
      {
        return parent;
      }
    }

    return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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