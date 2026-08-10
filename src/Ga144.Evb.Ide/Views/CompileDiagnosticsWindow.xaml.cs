using System.Windows;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Non-modal window that shows compilation diagnostics (errors/warnings). A single
/// instance is reused per node editor: <see cref="ShowDiagnostics"/> updates the
/// text and brings it forward, so repeated failed compiles refresh the same window
/// instead of stacking new ones. Because it is shown with Show() (not ShowDialog())
/// the editor stays usable while it is open.
/// </summary>
public partial class CompileDiagnosticsWindow : Window
{
  public CompileDiagnosticsWindow()
  {
    InitializeComponent();
  }

  public void ShowDiagnostics(string header, string diagnostics)
  {
    HeaderText.Text = header;
    DiagnosticsText.Text = diagnostics;
    if (!IsVisible)
    {
      Show();
    }

    Activate();
  }

  private void OnCopyClick(object sender, RoutedEventArgs e)
  {
    try
    {
      Clipboard.SetText(DiagnosticsText.Text ?? string.Empty);
    }
    catch
    {
      // Clipboard can transiently fail if another process holds it; ignore.
    }
  }

  private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
