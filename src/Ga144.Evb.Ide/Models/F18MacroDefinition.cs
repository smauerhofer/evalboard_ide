namespace Ga144.Evb.Ide.Models;

public sealed class F18MacroDefinition
{
  public string Name { get; set; } = "macro";
  public string Description { get; set; } = string.Empty;
  public string SourceCode { get; set; } = string.Empty;

  public F18MacroDefinition Clone() => new()
  {
    Name = Name,
    Description = Description,
    SourceCode = SourceCode
  };

  public void Normalize()
  {
    Name = (Name ?? string.Empty).Trim();
    Description ??= string.Empty;
    SourceCode ??= string.Empty;
  }

  public static bool IsValidName(string? name)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return false;
    }

    string value = name.Trim();
    if (value.Any(char.IsWhiteSpace) ||
        value.Any(character => character is '(' or ')' or ':' or ';' or ',' or '=' or '[' or ']'))
    {
      return false;
    }

    // Decimal tokens are reserved for node imports such as "607 import".
    return value.Any(character => !char.IsDigit(character));
  }

  public static void NormalizeList(List<F18MacroDefinition> macros)
  {
    for (int index = macros.Count - 1; index >= 0; index--)
    {
      F18MacroDefinition? macro = macros[index];
      if (macro is null)
      {
        macros.RemoveAt(index);
        continue;
      }

      macro.Normalize();
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    macros.RemoveAll(macro => string.IsNullOrWhiteSpace(macro.Name) || !seen.Add(macro.Name));
  }
}
