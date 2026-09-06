namespace Ga144.C.Toolchain;

/// <summary>
/// Turns source text into a flat stream of <see cref="CToken"/>s: comments are discarded (each one
/// collapses into <see cref="CToken.HasLeadingWhitespace"/> on the next real token, exactly like a run
/// of spaces/tabs would), line-splicing has already happened (via <see cref="CSourceReader"/>), and
/// each source line ends with exactly one <see cref="CTokenKind.NewLine"/> token. This is the one and
/// only tokenizer in the C toolchain -- both the preprocessor and, later, the compiler's parser consume
/// the token stream it produces (the parser after macro expansion has already run).
///
/// Not supported, deliberately: trigraphs, universal-character-names ("\uXXXX" in an identifier), and
/// raw/wide string literal contents beyond recognizing the "L"/"u"/"U"/"u8" prefixes so such literals
/// at least tokenize as a single StringLiteral/CharLiteral rather than tripping over the prefix letter.
/// </summary>
public static class CLexer
{
  // Internal (rather than private) so CPreprocessor's "##" paste operator can recognize a pasted
  // result as a valid punctuator using the exact same set the lexer itself scans for.
  internal static readonly string[] MultiCharPunctuators =
  [
    "...", "<<=", ">>=",
    "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||",
    "*=", "/=", "%=", "+=", "-=", "&=", "^=", "|=", "##",
  ];

  internal const string SingleCharPunctuators = "[](){}.,;:?~!*/%+-<>=^|&#";

  public static IReadOnlyList<CToken> Tokenize(string fileName, string sourceText, IList<string> diagnostics)
  {
    var reader = new CSourceReader(fileName, sourceText);
    var tokens = new List<CToken>();
    int pos = 0;
    bool atLineStart = true;

    while (pos < reader.Length)
    {
      bool hasLeadingWhitespace = SkipWhitespaceAndComments(reader, ref pos, diagnostics);
      if (pos >= reader.Length)
      {
        break;
      }

      CSourceLocation location = reader.GetLocation(pos);
      char c = reader[pos];

      if (c == '\n')
      {
        tokens.Add(new CToken(CTokenKind.NewLine, "\n", location, atLineStart, hasLeadingWhitespace));
        pos++;
        atLineStart = true;
        continue;
      }

      CToken token = ScanToken(reader, ref pos, location, atLineStart, hasLeadingWhitespace, diagnostics);
      tokens.Add(token);
      atLineStart = false;
    }

    tokens.Add(new CToken(CTokenKind.EndOfFile, string.Empty, reader.EndOfFileLocation, atLineStart, HasLeadingWhitespace: false));
    return tokens;
  }

  /// <summary>Skips any run of spaces, tabs, and comments starting at <paramref name="pos"/>, leaving
  /// it at the next non-whitespace character (or at a newline, which is not considered whitespace
  /// here since it is significant to directive/line handling). Returns whether anything was
  /// skipped.</summary>
  private static bool SkipWhitespaceAndComments(CSourceReader reader, ref int pos, IList<string> diagnostics)
  {
    bool skippedAny = false;
    while (pos < reader.Length)
    {
      char c = reader[pos];
      if (c == ' ' || c == '\t' || c == '\v' || c == '\f')
      {
        pos++;
        skippedAny = true;
        continue;
      }

      if (c == '/' && pos + 1 < reader.Length && reader[pos + 1] == '/')
      {
        pos += 2;
        while (pos < reader.Length && reader[pos] != '\n')
        {
          pos++;
        }

        skippedAny = true;
        continue;
      }

      if (c == '/' && pos + 1 < reader.Length && reader[pos + 1] == '*')
      {
        CSourceLocation start = reader.GetLocation(pos);
        pos += 2;
        bool terminated = false;
        while (pos < reader.Length)
        {
          if (reader[pos] == '*' && pos + 1 < reader.Length && reader[pos + 1] == '/')
          {
            pos += 2;
            terminated = true;
            break;
          }

          pos++;
        }

        if (!terminated)
        {
          diagnostics.Add(start.FormatDiagnostic("error", "unterminated comment"));
        }

        skippedAny = true;
        continue;
      }

      break;
    }

    return skippedAny;
  }

  private static CToken ScanToken(CSourceReader reader, ref int pos, CSourceLocation location, bool atLineStart, bool hasLeadingWhitespace, IList<string> diagnostics)
  {
    char c = reader[pos];

    if (IsIdentifierStart(c))
    {
      int start = pos;
      pos++;
      while (pos < reader.Length && IsIdentifierPart(reader[pos]))
      {
        pos++;
      }

      string text = new(reader.Slice(start, pos));

      // A string/char literal prefix ("L", "u", "U", "u8") immediately followed by a quote, with no
      // whitespace in between, is part of the literal rather than a standalone identifier.
      if (pos < reader.Length && (reader[pos] == '"' || reader[pos] == '\'') && IsStringPrefix(text))
      {
        return ScanQuoted(reader, ref pos, location, atLineStart, hasLeadingWhitespace, prefix: text, diagnostics);
      }

      return new CToken(CTokenKind.Identifier, text, location, atLineStart, hasLeadingWhitespace);
    }

    if (char.IsAsciiDigit(c) || (c == '.' && pos + 1 < reader.Length && char.IsAsciiDigit(reader[pos + 1])))
    {
      int start = pos;
      pos++;
      while (pos < reader.Length && IsPpNumberContinuation(reader, pos))
      {
        if ((reader[pos] is 'e' or 'E' or 'p' or 'P') && pos + 1 < reader.Length && (reader[pos + 1] is '+' or '-'))
        {
          pos += 2;
        }
        else
        {
          pos++;
        }
      }

      return new CToken(CTokenKind.Number, new string(reader.Slice(start, pos)), location, atLineStart, hasLeadingWhitespace);
    }

    if (c is '"' or '\'')
    {
      return ScanQuoted(reader, ref pos, location, atLineStart, hasLeadingWhitespace, prefix: "", diagnostics);
    }

    foreach (string punctuator in MultiCharPunctuators)
    {
      if (Matches(reader, pos, punctuator))
      {
        pos += punctuator.Length;
        return new CToken(CTokenKind.Punctuator, punctuator, location, atLineStart, hasLeadingWhitespace);
      }
    }

    if (SingleCharPunctuators.IndexOf(c) >= 0)
    {
      pos++;
      return new CToken(CTokenKind.Punctuator, c.ToString(), location, atLineStart, hasLeadingWhitespace);
    }

    pos++;
    return new CToken(CTokenKind.Other, c.ToString(), location, atLineStart, hasLeadingWhitespace);
  }

  private static CToken ScanQuoted(CSourceReader reader, ref int pos, CSourceLocation location, bool atLineStart, bool hasLeadingWhitespace, string prefix, IList<string> diagnostics)
  {
    int start = pos - prefix.Length;
    char quote = reader[pos];
    CTokenKind kind = quote == '"' ? CTokenKind.StringLiteral : CTokenKind.CharLiteral;
    pos++;
    bool terminated = false;
    while (pos < reader.Length)
    {
      char c = reader[pos];
      if (c == '\n')
      {
        break;
      }

      if (c == '\\' && pos + 1 < reader.Length && reader[pos + 1] != '\n')
      {
        pos += 2;
        continue;
      }

      pos++;
      if (c == quote)
      {
        terminated = true;
        break;
      }
    }

    if (!terminated)
    {
      diagnostics.Add(location.FormatDiagnostic("error", $"missing terminating {quote} character"));
    }

    return new CToken(kind, new string(reader.Slice(start, pos)), location, atLineStart, hasLeadingWhitespace);
  }

  private static bool Matches(CSourceReader reader, int pos, string text)
  {
    if (pos + text.Length > reader.Length)
    {
      return false;
    }

    for (int i = 0; i < text.Length; i++)
    {
      if (reader[pos + i] != text[i])
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsPpNumberContinuation(CSourceReader reader, int pos)
  {
    char c = reader[pos];
    return char.IsAsciiLetterOrDigit(c) || c == '_' || c == '.';
  }

  private static bool IsStringPrefix(string text) => text is "L" or "u" or "U" or "u8";

  private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_';

  private static bool IsIdentifierPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}