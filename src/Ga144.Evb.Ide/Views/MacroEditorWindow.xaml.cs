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

  /// <summary>
  /// Explicit Click handler, not "IsCancel=True": WPF's IsCancel mechanism closes the window via
  /// DialogResult, which only works for a window shown with ShowDialog. This editor is opened
  /// non-modally (see MainWindow.xaml.cs), so an IsCancel button silently does nothing when clicked --
  /// that was the reported bug. Saved is left false, so MainWindow's Closed handler skips persisting
  /// any edits, exactly like a real cancel.
  /// </summary>
  private void OnCancelClick(object sender, RoutedEventArgs e)
  {
    Close();
  }
}