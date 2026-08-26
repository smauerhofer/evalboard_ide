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

      // ':', ';', ',', '=', '[', ']' are NOT self-delimiting -- matching strict
      // Forth semantics, where every token (including these) is just a maximal
      // run of non-whitespace characters. A properly spaced "main ;" still
      // tokenizes as two tokens ("main" and ";") because whitespace already
      // separates them below; a glued "main;" tokenizes as the single word
      // "main;", which then fails to resolve as any known word -- exactly like
      // a real Forth system would reject it, rather than silently splitting it
      // the way a C-like tokenizer would. (The "A[" and "]]" two-character
      // sequences above are this project's own packed-literal notation, not
      // standard Forth syntax, and are unaffected by this rule -- they are
      // recognized by their own explicit lookahead before this point is ever
      // reached, and are always written adjacent to whitespace on the other
      // side by convention, not by requirement.)
      var start = index;
      while (index < source.Length)
      {
        current = source[index];
        if (char.IsWhiteSpace(current) || current is '(' or ')')
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