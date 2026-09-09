using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;
using Ga144.Evb.Ide.ViewModels;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// The IDE window for a single C project. Non-modal, and any number can be open at once -- one per
/// C project the user has opened or created, each editing a different folder on disk. MainWindow
/// keeps track of which root paths already have a window open so opening the same project twice
/// activates the existing window instead of a second one.
/// </summary>
public partial class CProjectWindow : Window
{
  private readonly CProjectViewModel _viewModel;

  // Resolves this project's own chosen chip project/role into a ready-to-open CvmDebuggerViewModel --
  // supplied by MainWindow (the only place that constructs this window), since only MainWindowViewModel
  // holds the live per-(GA144 project, board, role) KrakenLiveController cache a real chip session
  // needs (GetKrakenController's own remarks explain why a second, independent controller for the same
  // physical chip would be unsafe). Keeping this as an injected delegate rather than a direct
  // MainWindowViewModel reference keeps CProjectViewModel itself hardware-independent -- a C project's
  // Build already works whether or not the GA144 side of the IDE is open at all (see
  // ChipProjectResolver's own remarks), and Debug's own extra hardware dependency lives here in the
  // window, not leaked into that view model.
  private readonly Func<Guid, Ga144ChipRole, (bool Success, string? ErrorMessage, CvmDebuggerViewModel? ViewModel)> _resolveCvmDebugger;

  // Same reusable, non-modal window pattern ChipWindow's own "CVM Debugger" button uses -- one window
  // per click of Debug, reactivated (and reloaded with the freshly built image) rather than duplicated
  // on a second click.
  private CvmDebuggerWindow? _cvmDebuggerWindow;

  public CProjectWindow(
      CProjectViewModel viewModel,
      Func<Guid, Ga144ChipRole, (bool Success, string? ErrorMessage, CvmDebuggerViewModel? ViewModel)> resolveCvmDebugger)
  {
    InitializeComponent();
    _viewModel = viewModel;
    _resolveCvmDebugger = resolveCvmDebugger;
    DataContext = viewModel;
  }

  private void OnOpenProjectFolderClick(object sender, RoutedEventArgs e) => OpenInExplorer(_viewModel.RootPath);

  private void OnRefreshClick(object sender, RoutedEventArgs e) => _viewModel.Refresh();

  private void OnNewHeaderFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        string? name = TextInputDialog.Ask(this, "New header file", "File name (a \".h\" extension is added if you leave it off):");
        if (name is not null)
        {
          _viewModel.SelectedHeaderFile = RelativeTo(_viewModel.Model.IncludeDirectoryPath, _viewModel.CreateNewHeaderFile(name));
        }
      });

  private void OnNewSourceFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        string? name = TextInputDialog.Ask(this, "New source file", "File name (a \".c\" extension is added if you leave it off):");
        if (name is not null)
        {
          _viewModel.SelectedSourceFile = RelativeTo(_viewModel.Model.SourceDirectoryPath, _viewModel.CreateNewSourceFile(name));
        }
      });

  private void OnAddExistingHeaderFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        var dialog = new OpenFileDialog { Title = "Add existing header file", Filter = "C headers (*.h)|*.h|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
          _viewModel.SelectedHeaderFile = RelativeTo(_viewModel.Model.IncludeDirectoryPath, _viewModel.AddExistingHeaderFile(dialog.FileName));
        }
      });

  private void OnAddExistingSourceFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        var dialog = new OpenFileDialog { Title = "Add existing source file", Filter = "C sources (*.c)|*.c|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
          _viewModel.SelectedSourceFile = RelativeTo(_viewModel.Model.SourceDirectoryPath, _viewModel.AddExistingSourceFile(dialog.FileName));
        }
      });

  private void OnOpenHeaderFileClick(object sender, MouseButtonEventArgs e) => OnOpenHeaderFileClick(sender, (RoutedEventArgs)e);

  private void OnOpenHeaderFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() => OpenFile(_viewModel.SelectedHeaderFile, _viewModel.ResolveHeaderFilePath));

  private void OnOpenSourceFileClick(object sender, MouseButtonEventArgs e) => OnOpenSourceFileClick(sender, (RoutedEventArgs)e);

  private void OnOpenSourceFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() => OpenFile(_viewModel.SelectedSourceFile, _viewModel.ResolveSourceFilePath));

  private void OnRemoveHeaderFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        if (_viewModel.SelectedHeaderFile is { } file && ConfirmRemove(file))
        {
          _viewModel.RemoveHeaderFile(file);
        }
      });

  private void OnRemoveSourceFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        if (_viewModel.SelectedSourceFile is { } file && ConfirmRemove(file))
        {
          _viewModel.RemoveSourceFile(file);
        }
      });

  private void OnNewAssemblyFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        string? name = TextInputDialog.Ask(this, "New assembly file", "File name (a \".asm\" extension is added if you leave it off):");
        if (name is not null)
        {
          _viewModel.SelectedAssemblyFile = RelativeTo(_viewModel.Model.AssemblyDirectoryPath, _viewModel.CreateNewAssemblyFile(name));
        }
      });

  private void OnAddExistingAssemblyFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        var dialog = new OpenFileDialog { Title = "Add existing assembly file", Filter = "CVM assembly (*.asm)|*.asm|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
          _viewModel.SelectedAssemblyFile = RelativeTo(_viewModel.Model.AssemblyDirectoryPath, _viewModel.AddExistingAssemblyFile(dialog.FileName));
        }
      });

  private void OnOpenAssemblyFileClick(object sender, MouseButtonEventArgs e) => OnOpenAssemblyFileClick(sender, (RoutedEventArgs)e);

  private void OnOpenAssemblyFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() => OpenFile(_viewModel.SelectedAssemblyFile, _viewModel.ResolveAssemblyFilePath));

  private void OnRemoveAssemblyFileClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        if (_viewModel.SelectedAssemblyFile is { } file && ConfirmRemove(file))
        {
          _viewModel.RemoveAssemblyFile(file);
        }
      });

  private void OnChooseChipProjectClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        IReadOnlyList<ChipProjectResolver.ProjectSummary> projects = _viewModel.ListAvailableChipProjects();
        (Guid? ProjectId, Ga144ChipRole Role)? choice = SelectChipProjectDialog.Ask(
            this, projects, _viewModel.Model.ChipProjectId, _viewModel.Model.ChipProjectRole);

        if (choice is { } selected)
        {
          _viewModel.SetChipProject(selected.ProjectId, selected.Role);
        }
      });

  private void OnBuildClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        CBuildResult result = _viewModel.Build();
        MessageBox.Show(
            this,
            result.Messages.Count > 0 ? string.Join(Environment.NewLine, result.Messages) : "Nothing to build.",
            (result.Success ? "Build succeeded -- " : "Build failed -- ") + _viewModel.Name,
            MessageBoxButton.OK,
            result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
      });

  /// <summary>
  /// Builds this project, then opens (or reactivates) the CVM Debugger against its own chosen chip
  /// project with the resulting .gaimg already loaded -- Stefan's own ask: "I am missing a debugger
  /// button in the cprojectwindow, that opens directly the debugger with the program already loaded
  /// into the debugger." Always rebuilds first (same reasoning an IDE's own "Start Debugging" always
  /// does) so Debug never quietly runs a stale image left over from an earlier Build; a build failure
  /// stops here with the same message Build's own button would show, and never touches an
  /// already-open debugger window. Reuses an already-open CVM Debugger window for this C project
  /// rather than opening a second one -- clicking Debug again after an edit reloads the freshly built
  /// image into that SAME session (LoadImageFile is a live reprogram, not a reset, so this never
  /// disturbs a Step/Continue/breakpoint session already in progress on unrelated addresses).
  /// </summary>
  private void OnDebugClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        if (_viewModel.Model.ChipProjectId is not { } chipProjectId)
        {
          MessageBox.Show(this, "Choose a chip project first (\"Chip project...\") -- Debug needs to know which GA144 chip to run this program on.",
              "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        CBuildResult buildResult = _viewModel.Build();
        if (!buildResult.Success)
        {
          MessageBox.Show(
              this,
              buildResult.Messages.Count > 0 ? string.Join(Environment.NewLine, buildResult.Messages) : "Build failed.",
              "Debug -- build failed -- " + _viewModel.Name,
              MessageBoxButton.OK,
              MessageBoxImage.Warning);
          return;
        }

        if (!File.Exists(_viewModel.Model.ImageOutputPath))
        {
          MessageBox.Show(this,
              "Build succeeded but produced no .gaimg to debug -- check that a chip project is chosen and that this project actually links (see the Build dialog's own messages).",
              "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        CvmDebuggerViewModel debuggerViewModel;
        if (_cvmDebuggerWindow is not null)
        {
          debuggerViewModel = (CvmDebuggerViewModel)_cvmDebuggerWindow.DataContext;
          if (_cvmDebuggerWindow.WindowState == WindowState.Minimized)
          {
            _cvmDebuggerWindow.WindowState = WindowState.Normal;
          }

          _cvmDebuggerWindow.Activate();
        }
        else
        {
          (bool success, string? errorMessage, CvmDebuggerViewModel? resolvedViewModel) = _resolveCvmDebugger(chipProjectId, _viewModel.Model.ChipProjectRole);
          if (!success || resolvedViewModel is null)
          {
            MessageBox.Show(this, errorMessage ?? "Could not open the CVM Debugger.", "Debug", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
          }

          debuggerViewModel = resolvedViewModel;
          var window = new CvmDebuggerWindow(debuggerViewModel) { Owner = this };
          _cvmDebuggerWindow = window;
          window.Closed += OnCvmDebuggerWindowClosed;
          window.Show();
        }

        debuggerViewModel.LoadImageFile(_viewModel.Model.ImageOutputPath);
      });

  private void OnCvmDebuggerWindowClosed(object? sender, EventArgs e)
  {
    if (sender is CvmDebuggerWindow window)
    {
      window.Closed -= OnCvmDebuggerWindowClosed;
    }

    _cvmDebuggerWindow = null;
  }

  private bool ConfirmRemove(string displayName) =>
      MessageBox.Show(
          this,
          $"Remove \"{displayName}\" from the project? The file is deleted from disk.",
          "Remove file",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning) == MessageBoxResult.Yes;

  private static void OpenFile(string? selectedDisplayPath, Func<string, string> resolve)
  {
    if (selectedDisplayPath is null)
    {
      return;
    }

    Process.Start(new ProcessStartInfo
    {
      FileName = resolve(selectedDisplayPath),
      UseShellExecute = true,
    });
  }

  private static void OpenInExplorer(string path)
  {
    Directory.CreateDirectory(path);
    Process.Start(new ProcessStartInfo
    {
      FileName = path,
      UseShellExecute = true,
    });
  }

  private static string RelativeTo(string baseDirectory, string fullPath) => Path.GetRelativePath(baseDirectory, fullPath);

  private void RunGuarded(Action action)
  {
    try
    {
      action();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
    {
      MessageBox.Show(this, exception.Message, "C project error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}