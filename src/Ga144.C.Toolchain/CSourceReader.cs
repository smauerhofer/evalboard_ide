using System.Text;

namespace Ga144.C.Toolchain;

/// <summary>
/// Turns raw source text into the character sequence the lexer actually scans, by resolving
/// line-splicing (a backslash immediately followed by a newline -- optionally with trailing spaces or
/// tabs before that newline, which strict C does not allow but real-world code sometimes has --
/// deletes both the backslash and the newline, joining the two physical lines into one logical line).
/// Every surviving character keeps its own <see cref="CSourceLocation"/> so diagnostics still point at
/// the original physical line/column even after splicing, and "\r\n" is normalized to a single '\n'
/// so the rest of the toolchain never has to think about line-ending style.
///
/// This is an internal building block of <see cref="CLexer"/>; nothing outside this project needs it.
///
/// Deliberately not supported: trigraphs (e.g. "??="). Nobody writes them in new code, and GA144 C
/// sources are not expected to.
/// </summary>
internal sealed class CSourceReader
{
  private readonly char[] _text;
  private readonly CSourceLocation[] _locations;

  public CSourceReader(string fileName, string sourceText)
  {
    var text = new StringBuilder(sourceText.Length);
    var locations = new List<CSourceLocation>(sourceText.Length);

    int line = 1;
    int column = 1;
    int i = 0;
    while (i < sourceText.Length)
    {
      char c = sourceText[i];

      if (c == '\\' && TrySpliceLine(sourceText, i, out int spliceLength, out int newlineLength))
      {
        // Skip the backslash, any trailing horizontal whitespace, and the newline itself -- none of
        // it appears in the spliced output -- but still advance to the start of the next physical line.
        i += spliceLength;
        line++;
        column = 1;
        continue;
      }

      if (c == '\r' && i + 1 < sourceText.Length && sourceText[i + 1] == '\n')
      {
        // Normalize "\r\n" to a single '\n', attributed to the position of the '\r'.
        text.Append('\n');
        locations.Add(new CSourceLocation(fileName, line, column));
        i += 2;
        line++;
        column = 1;
        continue;
      }

      if (c == '\r')
      {
        // A lone '\r' (old Mac line ending) -- also treated as a newline.
        text.Append('\n');
        locations.Add(new CSourceLocation(fileName, line, column));
        i++;
        line++;
        column = 1;
        continue;
      }

      text.Append(c);
      locations.Add(new CSourceLocation(fileName, line, column));
      i++;
      if (c == '\n')
      {
        line++;
        column = 1;
      }
      else
      {
        column++;
      }
    }

    _text = new char[text.Length];
    text.CopyTo(0, _text, 0, text.Length);
    _locations = [.. locations];
    EndOfFileLocation = new CSourceLocation(fileName, line, column);
  }

  public int Length => _text.Length;

  public CSourceLocation EndOfFileLocation { get; }

  public char this[int index] => _text[index];

  public CSourceLocation GetLocation(int index) => index < _locations.Length ? _locations[index] : EndOfFileLocation;

  /// <summary>The characters from <paramref name="start"/> (inclusive) to <paramref name="end"/>
  /// (exclusive) -- e.g. the exact text an identifier, number, or string/char literal scanned to.</summary>
  public ReadOnlySpan<char> Slice(int start, int end) => _text.AsSpan(start, end - start);

  /// <summary>
  /// If a backslash at <paramref name="index"/> is immediately followed -- possibly after some
  /// spaces/tabs -- by a newline ("\n", "\r\n", or "\r"), reports how many characters make up that
  /// whole splice (the backslash, any in-between whitespace, and the newline) so the caller can skip
  /// over all of it at once.
  /// </summary>
  private static bool TrySpliceLine(string sourceText, int index, out int spliceLength, out int newlineLength)
  {
    int j = index + 1;
    while (j < sourceText.Length && (sourceText[j] == ' ' || sourceText[j] == '\t'))
    {
      j++;
    }

    if (j < sourceText.Length && sourceText[j] == '\r' && j + 1 < sourceText.Length && sourceText[j + 1] == '\n')
    {
      newlineLength = 2;
    }
    else if (j < sourceText.Length && (sourceText[j] == '\n' || sourceText[j] == '\r'))
    {
      newlineLength = 1;
    }
    else
    {
      spliceLength = 0;
      newlineLength = 0;
      return false;
    }

    spliceLength = j + newlineLength - index;
    return true;
  }
}
