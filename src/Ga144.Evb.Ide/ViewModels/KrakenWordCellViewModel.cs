using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class KrakenWordCellViewModel : ObservableObject
{
  private string _valueText = "0x00000";

  public KrakenWordCellViewModel(string label, int address = -1, bool isReadOnly = false)
  {
    Label = label;
    Address = address;
    IsReadOnly = isReadOnly;
  }

  public string Label { get; }
  public int Address { get; }
  public string AddressText => Address < 0 ? string.Empty : $"0x{Address:X3}";
  public bool IsReadOnly { get; }

  public string ValueText
  {
    get => _valueText;
    set => SetProperty(ref _valueText, value ?? string.Empty);
  }

  public void SetValue(int value) => ValueText = $"0x{value & F18InstructionSet.WordMask:X5}";

  public bool TryGetValue(out int value) => KrakenWordFormatting.TryParse(ValueText, out value);
}

internal static class KrakenWordFormatting
{
  public static bool TryParse(string? text, out int value)
  {
    value = 0;
    string token = (text ?? string.Empty).Trim().Replace("_", string.Empty, StringComparison.Ordinal);
    if (token.Length == 0)
    {
      return false;
    }

    bool negative = token.StartsWith("-", StringComparison.Ordinal);
    string body = negative ? token[1..] : token;
    int radix = 10;
    if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      radix = 16;
      body = body[2..];
    }
    else if (body.StartsWith('x') || body.StartsWith('X'))
    {
      radix = 16;
      body = body[1..];
    }
    else if (body.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
    {
      radix = 2;
      body = body[2..];
    }

    try
    {
      int parsed = Convert.ToInt32(body, radix);
      if (negative)
      {
        parsed = -parsed;
      }

      if (parsed is < -0x20000 or > 0x3FFFF)
      {
        return false;
      }

      value = parsed & F18InstructionSet.WordMask;
      return true;
    }
    catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
    {
      return false;
    }
  }
}
