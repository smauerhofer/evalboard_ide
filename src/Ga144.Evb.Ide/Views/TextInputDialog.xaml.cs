using System.Windows;

namespace Ga144.Evb.Ide.Views;

/// <summary>A small reusable "enter a single line of text" prompt (a new project's name, a new file's name, and so on) -- WPF has no built-in equivalent of VB's InputBox.</summary>
public partial class TextInputDialog : Window
{
  public static readonly DependencyProperty PromptProperty = DependencyProperty.Register(
      nameof(Prompt), typeof(string), typeof(TextInputDialog), new PropertyMetadata(string.Empty));

  public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
      nameof(Value), typeof(string), typeof(TextInputDialog), new PropertyMetadata(string.Empty));

  public TextInputDialog()
  {
    InitializeComponent();
    Loaded += (_, _) =>
    {
      ValueTextBox.SelectAll();
      ValueTextBox.Focus();
    };
  }

  public string Prompt
  {
    get => (string)GetValue(PromptProperty);
    set => SetValue(PromptProperty, value);
  }

  public string Value
  {
    get => (string)GetValue(ValueProperty);
    set => SetValue(ValueProperty, value);
  }

  private void OnOkClick(object sender, RoutedEventArgs e)
  {
    if (string.IsNullOrWhiteSpace(Value))
    {
      MessageBox.Show(this, "Enter a value.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    DialogResult = true;
  }

  /// <summary>Shows the prompt modally and returns the trimmed text, or null if the user cancelled.</summary>
  public static string? Ask(Window owner, string title, string prompt, string defaultValue = "")
  {
    var dialog = new TextInputDialog
    {
      Owner = owner,
      Title = title,
      Prompt = prompt,
      Value = defaultValue,
    };

    return dialog.ShowDialog() == true ? dialog.Value.Trim() : null;
  }
}