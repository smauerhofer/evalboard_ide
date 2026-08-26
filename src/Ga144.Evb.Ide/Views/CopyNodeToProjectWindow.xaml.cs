using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class CopyNodeToProjectWindow : Window
{
  private readonly CopyNodeToProjectViewModel _viewModel;

  public CopyNodeToProjectWindow(CopyNodeToProjectViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
  }

  private void OnCopyClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.SelectedProject is null)
    {
      MessageBox.Show(
          this,
          "Select a project to copy into.",
          "Copy to project",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      return;
    }

    DialogResult = true;
  }
}
