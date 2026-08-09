using System.Windows;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Modal shown for one node whose generated ROM does not match the chip. Lists
/// every differing address and offers Continue (skip to the next node) or Abort
/// (stop the whole sweep). For a single-node verify only Continue is meaningful;
/// the caller can treat either result as "dismiss".
/// </summary>
public partial class RomMismatchDialog : Window
{
  /// <summary>True when the user chose Abort (stop the sweep).</summary>
  public bool Aborted { get; private set; }

  public RomMismatchDialog(RomComparison comparison, bool showAbort = true)
  {
    InitializeComponent();

    HeaderText.Text =
        $"Node {comparison.Coordinate:000}: {comparison.Mismatches.Count} ROM word(s) differ from the chip.";

    if (comparison.Coverage is not null)
    {
      CoverageText.Text = comparison.Coverage;
      CoverageText.Visibility = Visibility.Visible;
    }

    MismatchGrid.ItemsSource = comparison.Mismatches;

    if (!showAbort)
    {
      AbortButton.Visibility = Visibility.Collapsed;
    }
  }

  private void OnContinueClick(object sender, RoutedEventArgs e)
  {
    Aborted = false;
    DialogResult = true;
  }

  private void OnAbortClick(object sender, RoutedEventArgs e)
  {
    Aborted = true;
    DialogResult = false;
  }
}
