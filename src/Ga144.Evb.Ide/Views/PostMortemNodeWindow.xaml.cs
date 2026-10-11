using System.Windows;
using Ga144.Evb.Ide.ViewModels;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// Non-modal post-mortem detail window for one node -- opened by <see cref="PostMortemChipWindow"/>,
/// which sets <see cref="Window.Owner"/> to itself so every one of these closes automatically when
/// the chip window closes (WPF's own owned-window mechanism; no extra code needed for that here).
/// </summary>
public partial class PostMortemNodeWindow : Window
{
  private readonly PostMortemNodeDetailViewModel _viewModel;

  public PostMortemNodeWindow(PostMortemNodeDetailViewModel viewModel)
  {
    InitializeComponent();
    _viewModel = viewModel;
    DataContext = viewModel;
  }

  // Same clipboard convention as KrakenNodeControlWindow's own Copy RAM/ROM buttons: the view model
  // builds the text (BuildClipboardText -- exactly what the registers/stacks panel above shows),
  // this just puts it on the clipboard and swallows the rare "another process holds it" failure.
  private void OnCopyRegistersAndStacksClick(object sender, RoutedEventArgs e) =>
      CopyToClipboard(_viewModel.BuildClipboardText());

  // "Copy RAM" (2026-10-11, Stefan: "I want to copy the complete RAM disassembly array into
  // clipboard"): same idea, this time for the RAM tab's own six-column table
  // (BuildRamClipboardText -- exactly what the ListView below shows, tab-separated with a header row).
  private void OnCopyRamClick(object sender, RoutedEventArgs e) =>
      CopyToClipboard(_viewModel.BuildRamClipboardText());

  // "Copy ROM" (2026-10-11, same ask as Copy RAM, for the ROM tab): BuildRomClipboardText -- exactly
  // what the ListView on the ROM tab shows, tab-separated with a header row.
  private void OnCopyRomClick(object sender, RoutedEventArgs e) =>
      CopyToClipboard(_viewModel.BuildRomClipboardText());

  private static void CopyToClipboard(string text)
  {
    try
    {
      Clipboard.SetText(text);
    }
    catch (System.Runtime.InteropServices.ExternalException)
    {
      // The clipboard can transiently fail if another process holds it; ignore.
    }
  }
}