using System;
using System.Text;
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
  private readonly RomComparison _comparison;
  private readonly string? _expandedRomSource;

  /// <summary>True when the user chose Abort (stop the sweep).</summary>
  public bool Aborted { get; private set; }

  public RomMismatchDialog(
      RomComparison comparison,
      bool showAbort = true,
      string? expandedRomSource = null)
  {
    InitializeComponent();

    _comparison = comparison;
    _expandedRomSource = expandedRomSource;

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

    // The diagnostics copy is only useful when the expanded ROM source is
    // available; hide the button otherwise rather than copy an empty section.
    if (string.IsNullOrEmpty(_expandedRomSource))
    {
      CopyDiagnosticsButton.Visibility = Visibility.Collapsed;
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

  private void OnCopyClick(object sender, RoutedEventArgs e)
  {
    CopyToClipboard(BuildClipboardText());
  }

  private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
  {
    CopyToClipboard(BuildDiagnosticsText());
  }

  private void CopyToClipboard(string text)
  {
    try
    {
      Clipboard.SetText(text);
    }
    catch (Exception exception)
    {
      // The clipboard can be transiently locked by another process; surface it
      // rather than failing silently, and leave the dialog open so the user can
      // retry.
      MessageBox.Show(
          this,
          "Could not copy to the clipboard:\n\n" + exception.Message,
          "Copy failed",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }
  }

  // Tab-separated so it pastes cleanly into a spreadsheet or a diff: a header
  // line, the coverage note when present, a column row, then one row per
  // differing address with the generated and on-chip words.
  private string BuildClipboardText()
  {
    var builder = new StringBuilder();
    AppendMismatchTable(builder);
    return builder.ToString();
  }

  // The full diagnostics bundle: the mismatch table plus the macro-expanded ROM
  // source, so the whole picture can be pasted somewhere for analysis.
  private string BuildDiagnosticsText()
  {
    var builder = new StringBuilder();
    AppendMismatchTable(builder);
    builder.AppendLine();
    builder.AppendLine("=== Expanded ROM source (macros expanded) ===");
    builder.AppendLine(_expandedRomSource ?? string.Empty);
    return builder.ToString();
  }

  private void AppendMismatchTable(StringBuilder builder)
  {
    builder.AppendLine(
        $"Node {_comparison.Coordinate:000}: {_comparison.Mismatches.Count} ROM word(s) differ from the chip.");

    if (_comparison.Coverage is not null)
    {
      builder.AppendLine(_comparison.Coverage);
    }

    builder.AppendLine("Address\tGenerated\tOn chip");
    foreach (RomWordMismatch mismatch in _comparison.Mismatches)
    {
      builder.AppendLine($"{mismatch.AddressHex}\t{mismatch.GeneratedHex}\t{mismatch.OnChipHex}");
    }
  }
}