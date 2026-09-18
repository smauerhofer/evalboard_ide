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
  public PostMortemNodeWindow(PostMortemNodeDetailViewModel viewModel)
  {
    InitializeComponent();
    DataContext = viewModel;
  }
}
