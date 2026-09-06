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

  public CProjectWindow(CProjectViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
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

  private void OnAddLibraryReferenceClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        var dialog = new OpenFolderDialog { Title = "Choose the library project's own folder" };
        if (dialog.ShowDialog(this) == true)
        {
          _viewModel.AddLibraryReference(dialog.FolderName);
        }
      });

  private void OnRemoveLibraryReferenceClick(object sender, RoutedEventArgs e) =>
      RunGuarded(() =>
      {
        if (_viewModel.SelectedLibraryReference is { } reference)
        {
          _viewModel.RemoveLibraryReference(reference);
        }
      });

  private void OnBuildClick(object sender, RoutedEventArgs e) =>
      MessageBox.Show(
          this,
          _viewModel.BuildDescription,
          "Build " + _viewModel.Name,
          MessageBoxButton.OK,
          MessageBoxImage.Information);

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