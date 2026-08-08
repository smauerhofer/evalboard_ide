using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide.Views;

public partial class MacroEditorWindow : Window
{
  private readonly MacroEditorViewModel _viewModel;

  public MacroEditorWindow(MacroEditorViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
  }

  private void OnSaveClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.TryApply())
    {
      DialogResult = true;
    }
  }
}
