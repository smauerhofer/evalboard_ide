using System.Text;

namespace Ga144.C.Toolchain;

/// <summary>
/// Turns a token stream back into text. Used for the preprocessor CLI's "write the preprocessed
/// source back out" mode, and internally by the stringize ("#") operator to build the text of a
/// stringized macro argument. Each token is written with a single leading space wherever
/// <see cref="CToken.HasLeadingWhitespace"/> says one was there -- never more than one, since the
/// exact amount of original whitespace is not preserved (nor does anything downstream care) -- which
/// is just enough to guarantee two tokens that were never meant to touch (e.g. "-" and "-", or an
/// identifier and a following number) don't get lexed back as one token merged together.
/// </summary>
public static class CTokenWriter
{
  public static string Write(IEnumerable<CToken> tokens)
  {
    var builder = new StringBuilder();
    bool atLineStart = true;
    foreach (CToken token in tokens)
    {
      if (token.Kind == CTokenKind.EndOfFile)
      {
        break;
      }

      if (token.Kind == CTokenKind.NewLine)
      {
        builder.Append('\n');
        atLineStart = true;
        continue;
      }

      if (!atLineStart && token.HasLeadingWhitespace)
      {
        builder.Append(' ');
      }

      builder.Append(token.Text);
      atLineStart = false;
    }

    return builder.ToString();
  }

  /// <summary>The text a stringize ("#") operator produces for one macro argument's tokens: internal
  /// whitespace collapsed to single spaces, none at the very start or end, and every '"' and '\\'
  /// inside a string or character literal token escaped, per the C standard's rules for turning an
  /// argument into a string literal.</summary>
  public static string Stringize(IEnumerable<CToken> tokens)
  {
    var builder = new StringBuilder();
    bool first = true;
    foreach (CToken token in tokens)
    {
      if (!first && token.HasLeadingWhitespace)
      {
        builder.Append(' ');
      }

      if (token.Kind is CTokenKind.StringLiteral or CTokenKind.CharLiteral)
      {
        foreach (char c in token.Text)
        {
          if (c is '"' or '\\')
          {
            builder.Append('\\');
          }

          builder.Append(c);
        }
      }
      else
      {
        builder.Append(token.Text);
      }

      first = false;
    }

    return builder.ToString();
  }
}
