using Ga144.Evb.Ide.ViewModels;
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

  private void OnCreateClick(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(_viewModel.Name))
    {
      MessageBox.Show(this, "Enter a project name.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    DialogResult = true;
  }
}