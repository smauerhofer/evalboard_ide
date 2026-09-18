using System.Globalization;
using System.Windows.Data;

namespace Ga144.Evb.Ide.Views;

/// <summary>
/// True when two hex color strings name the same color (case-insensitive) -- used by the
/// project "Default node color" picker (MainWindow.xaml) and the per-node "Node color" picker
/// (NodeEditorWindow.xaml) to put a highlighted border on whichever palette swatch is currently
/// selected, via a MultiBinding DataTrigger comparing a swatch's own hex to the picker's current
/// value. Registered once as a shared resource in App.xaml.
/// </summary>
public sealed class ColorEqualsConverter : IMultiValueConverter
{
  public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
  {
    return values is [string left, string right] && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
  }

  public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
      throw new NotSupportedException();
}
