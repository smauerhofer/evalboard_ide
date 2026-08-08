using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.ViewModels;

namespace Ga144.Evb.Ide.Controls;

public partial class BoardViewControl : UserControl
{
    public static readonly DependencyProperty BoardProperty = DependencyProperty.Register(
        nameof(Board),
        typeof(BoardViewModel),
        typeof(BoardViewControl),
        new PropertyMetadata(null, OnBoardChanged));

    public BoardViewControl()
    {
        InitializeComponent();
    }

    public BoardViewModel? Board
    {
        get => (BoardViewModel?)GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    public event EventHandler<ChipRequestedEventArgs>? ChipRequested;
    public event EventHandler<BoardPortRequestedEventArgs>? PortRequested;

    private static void OnBoardChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var control = (BoardViewControl)dependencyObject;
        if (eventArgs.OldValue is BoardViewModel oldBoard)
        {
            oldBoard.PropertyChanged -= control.OnBoardPropertyChanged;
        }

        if (eventArgs.NewValue is BoardViewModel newBoard)
        {
            newBoard.PropertyChanged += control.OnBoardPropertyChanged;
        }

        control.Rebuild();
    }

    private void OnBoardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BoardViewModel.BoardModel) or nameof(BoardViewModel.BoardVisualRevision))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        OverlayCanvas.Children.Clear();
        if (Board is null)
        {
            BoardImage.Source = null;
            NoBoardPanel.Visibility = Visibility.Visible;
            return;
        }

        BoardVisualDefinition definition = BoardVisualCatalog.Get(Board.BoardModel);
        if (string.IsNullOrWhiteSpace(definition.ImageResource))
        {
            BoardImage.Source = null;
            NoBoardPanel.Visibility = Visibility.Visible;
            return;
        }

        BoardImage.Source = new BitmapImage(new Uri(definition.ImageResource, UriKind.Absolute));
        NoBoardPanel.Visibility = Visibility.Collapsed;

        foreach (ChipVisualDefinition chip in definition.Chips)
        {
            Button button = CreateChipButton(chip);
            Canvas.SetLeft(button, chip.X);
            Canvas.SetTop(button, chip.Y);
            OverlayCanvas.Children.Add(button);
        }

        foreach (BoardPortVisualDefinition port in definition.Ports)
        {
            Button button = CreatePortButton(port);
            Canvas.SetLeft(button, port.X);
            Canvas.SetTop(button, port.Y);
            OverlayCanvas.Children.Add(button);
        }

        foreach (JumperVisualDefinition jumper in definition.Jumpers)
        {
            Button button = CreateJumperButton(jumper);
            Canvas.SetLeft(button, jumper.X);
            Canvas.SetTop(button, jumper.Y);
            OverlayCanvas.Children.Add(button);
        }
    }

    private Button CreateChipButton(ChipVisualDefinition chip)
    {
        var button = CreateOverlayButton(chip.Width, chip.Height);
        button.Tag = chip.Role;
        button.Content = chip.ShortLabel;
        button.FontSize = 15;
        button.FontWeight = FontWeights.Bold;
        button.Foreground = Brushes.White;
        button.Background = new SolidColorBrush(Color.FromArgb(52, 15, 54, 28));
        button.BorderBrush = new SolidColorBrush(Color.FromArgb(235, 255, 220, 76));
        button.BorderThickness = new Thickness(2);
        button.ToolTip = $"Open the {chip.Label} node map from the active project";
        button.Click += (_, _) => ChipRequested?.Invoke(this, new ChipRequestedEventArgs(chip.Role));
        return button;
    }

    private Button CreatePortButton(BoardPortVisualDefinition port)
    {
        string assignment = Board?.GetPortSummary(port.Role) ?? "Not assigned";
        bool assigned = !string.Equals(assignment, "Not assigned", StringComparison.Ordinal);

        var button = CreateOverlayButton(port.Width, port.Height);
        button.Tag = port.Role;
        button.Content = port.ShortLabel;
        button.FontSize = 14;
        button.FontWeight = FontWeights.Bold;
        button.Foreground = Brushes.White;
        button.Background = assigned
            ? new SolidColorBrush(Color.FromArgb(185, 20, 126, 166))
            : new SolidColorBrush(Color.FromArgb(60, 18, 40, 48));
        button.BorderBrush = assigned ? Brushes.White : Brushes.DeepSkyBlue;
        button.BorderThickness = new Thickness(assigned ? 2.5 : 1.5);
        button.ToolTip = $"{port.Label}\n{assignment}\nSelect an FTDI serial interface, then click here to assign it to the selected board.";
        button.Click += (_, _) => PortRequested?.Invoke(this, new BoardPortRequestedEventArgs(port.Role));
        return button;
    }

    private Button CreateJumperButton(JumperVisualDefinition jumper)
    {
        bool installed = Board?.IsJumperInstalled(jumper.Id) == true;
        var button = CreateOverlayButton(jumper.Width, jumper.Height);
        button.Tag = jumper.Id;
        button.Content = string.Empty;
        button.Background = installed
            ? new SolidColorBrush(Color.FromArgb(190, 255, 188, 49))
            : new SolidColorBrush(Color.FromArgb(22, 20, 20, 20));
        button.BorderBrush = installed ? Brushes.White : Brushes.Orange;
        button.BorderThickness = new Thickness(installed ? 1.5 : 1.0);
        button.ToolTip = $"{jumper.Id}: {jumper.Label}\nCurrent state: {(installed ? "installed" : "removed")}\nClick to toggle.";

        button.Click += (_, _) =>
        {
            Board?.ToggleJumper(jumper.Id);
            Rebuild();
        };
        return button;
    }

    private static Button CreateOverlayButton(double width, double height) => new()
    {
        Width = width,
        Height = height,
        MinWidth = 0,
        MinHeight = 0,
        Margin = new Thickness(0),
        Padding = new Thickness(0),
        Cursor = System.Windows.Input.Cursors.Hand,
        Focusable = false,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
}

public sealed class ChipRequestedEventArgs(Ga144ChipRole role) : EventArgs
{
    public Ga144ChipRole Role { get; } = role;
}

public sealed class BoardPortRequestedEventArgs(EvalBoardPortRole role) : EventArgs
{
    public EvalBoardPortRole Role { get; } = role;
}
