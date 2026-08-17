namespace Ga144.Evb.Ide.Compiler;

internal sealed record F18Token(string Text, int Line, int Column, int StartIndex, int Length)
{
  public F18SourceLocation Location => new(Line, Column);
}

internal static class F18Tokenizer
{
  public static IReadOnlyList<F18Token> Tokenize(string source, List<F18Diagnostic> diagnostics)
  {
    var tokens = new List<F18Token>();
    var index = 0;
    var line = 1;
    var column = 1;

    while (index < source.Length)
    {
      var current = source[index];

      if (char.IsWhiteSpace(current))
      {
        Advance(current, ref index, ref line, ref column);
        continue;
      }

      if (current == '(')
      {
        var startLine = line;
        var startColumn = column;

        // Standard FORTH comment: '(' opens a comment that the NEXT ')' closes.
        // Comments do not nest -- any '(' encountered while scanning is just
        // ordinary comment text with no effect on when the comment ends. Only
        // the first ')' matters.
        var closed = false;

        while (index < source.Length)
        {
          current = source[index];
          Advance(current, ref index, ref line, ref column);
          if (current == ')')
          {
            closed = true;
            break;
          }
        }

        if (!closed)
        {
          diagnostics.Add(new F18Diagnostic(
              F18DiagnosticSeverity.Error,
              "F18T001",
              "Unterminated parenthesized comment.",
              new F18SourceLocation(startLine, startColumn)));
        }

        continue;
      }

      if (current == '\\' ||
          (current == '/' && index + 1 < source.Length && source[index + 1] == '/'))
      {
        while (index < source.Length && source[index] != '\n')
        {
          Advance(source[index], ref index, ref line, ref column);
        }

        continue;
      }

      var tokenLine = line;
      var tokenColumn = column;

      if ((current == 'A' || current == 'a') && index + 1 < source.Length && source[index + 1] == '[')
      {
        tokens.Add(new F18Token("A[", tokenLine, tokenColumn, index, 2));
        Advance(current, ref index, ref line, ref column);
        Advance('[', ref index, ref line, ref column);
        continue;
      }

      if (current == ']' && index + 1 < source.Length && source[index + 1] == ']')
      {
        tokens.Add(new F18Token("]]", tokenLine, tokenColumn, index, 2));
        Advance(current, ref index, ref line, ref column);
        Advance(']', ref index, ref line, ref column);
        continue;
      }

      if (current is ':' or ';' or ',' or '=' or '[' or ']')
      {
        tokens.Add(new F18Token(current.ToString(), tokenLine, tokenColumn, index, 1));
        Advance(current, ref index, ref line, ref column);
        continue;
      }

      var start = index;
      while (index < source.Length)
      {
        current = source[index];
        if (char.IsWhiteSpace(current) || current is '(' or ')' or ':' or ';' or ',' or '[' or ']')
        {
          break;
        }

        if (current == '\\')
        {
          break;
        }

        if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
        {
          break;
        }

        Advance(current, ref index, ref line, ref column);
      }

      if (index == start)
      {
        diagnostics.Add(new F18Diagnostic(
            F18DiagnosticSeverity.Error,
            "F18T002",
            $"Unexpected character '{source[index]}'.",
            new F18SourceLocation(line, column)));
        Advance(source[index], ref index, ref line, ref column);
        continue;
      }

      tokens.Add(new F18Token(source[start..index], tokenLine, tokenColumn, start, index - start));
    }

    return tokens;
  }

  private static void Advance(char value, ref int index, ref int line, ref int column)
  {
    index++;
    if (value == '\n')
    {
      line++;
      column = 1;
    }
    else
    {
      column++;
    }
  }
}