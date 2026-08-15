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

  /// <summary>
  /// True when the user saved (applied) the macro edits. Used instead of
  /// DialogResult because this window is shown non-modally, and setting
  /// DialogResult is only valid for a window opened with ShowDialog.
  /// </summary>
  public bool Saved { get; private set; }

  private void OnSaveClick(object sender, RoutedEventArgs e)
  {
    if (_viewModel.TryApply())
    {
      Saved = true;
      Close();
    }
  }
}