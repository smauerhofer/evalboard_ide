using Ga144.Evb.Ide.ViewModels;
using Microsoft.Win32;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class NewCProjectWindow : Window
{
  private readonly NewCProjectViewModel _viewModel;

  public NewCProjectWindow(NewCProjectViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
  }

  private void OnBrowseClick(object sender, RoutedEventArgs e)
  {
    var dialog = new OpenFolderDialog
    {
      Title = "Choose where the new project's own folder is created",
    };

    if (Directory.Exists(_viewModel.Location))
    {
      dialog.InitialDirectory = _viewModel.Location;
    }

    if (dialog.ShowDialog(this) == true)
    {
      _viewModel.Location = dialog.FolderName;
    }
  }

  private void OnCreateClick(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(_viewModel.Name))
    {
      MessageBox.Show(this, "Enter a project name.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    if (string.IsNullOrWhiteSpace(_viewModel.Location) || !Directory.Exists(_viewModel.Location))
    {
      MessageBox.Show(this, "Choose an existing location for the new project.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    DialogResult = true;
  }
}
