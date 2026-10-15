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

      if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
      {
        var blockStartLine = line;
        var blockStartColumn = column;

        // ADDED 2026-10-02, per Stefan directly asking for C-style "/* ... */" block
        // comments -- ADDITIONAL to (not replacing) this tokenizer's existing Forth
        // "( ... )" comment just above and its "\"/"//" line comments just below. Unlike
        // either of those, a "/* ... */" comment can span multiple lines: Advance() still
        // runs on every character consumed here (including '\n'), so line/column tracking
        // for whatever follows the comment -- a token, or an "unterminated" diagnostic for
        // some LATER construct -- stays correct even across a multi-line comment. Like the
        // "( ... )" comment above, a "/*"..."*/" pair does not nest -- only the next "*/"
        // closes it, regardless of any "/*" encountered while scanning its own body.
        Advance(current, ref index, ref line, ref column); // '/'
        Advance(source[index], ref index, ref line, ref column); // '*'

        var blockClosed = false;
        while (index < source.Length)
        {
          current = source[index];
          if (current == '*' && index + 1 < source.Length && source[index + 1] == '/')
          {
            Advance(current, ref index, ref line, ref column); // '*'
            Advance(source[index], ref index, ref line, ref column); // '/'
            blockClosed = true;
            break;
          }

          Advance(current, ref index, ref line, ref column);
        }

        if (!blockClosed)
        {
          diagnostics.Add(new F18Diagnostic(
              F18DiagnosticSeverity.Error,
              "F18T003",
              "Unterminated \"/*\" block comment.",
              new F18SourceLocation(blockStartLine, blockStartColumn)));
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

      // REMOVED 2026-10-16, per Stefan directly: "the tokenizer can be much simpler. every
      // word needs to be separated by at least 1 whitespace. so no ']]' lookahead is needed.
      // ']]' is simply a word that finishes the 'A[' sequence. then ']],' is not allowed,
      // because ']],' is not a word, but ']]' and ',' are words." This used to special-case
      // "A[" and "]]" as their own two-character tokens via explicit lookahead, independent
      // of whether whitespace followed them -- which is exactly why a glued "]]," used to
      // tokenize as two separate tokens ("]]" then ",") and silently compile, instead of
      // being rejected as the single unrecognized word "]]," it actually is. Both mnemonics
      // are ordinary reserved words to F18Compiler's own dispatch (its switch matches on
      // token.Text alone, case "a[" / "]]"), so they need no tokenizer-level special
      // treatment at all: like every other word, ':', ';', ',', '=', '[', ']' included (see
      // the rule just below), they are only ever recognized when whitespace delimits them on
      // BOTH sides -- a glued "]]," is now the single, undefined word "]]," and is reported
      // as an unresolved word, not silently split.
      //
      // ':', ';', ',', '=', '[', ']' are NOT self-delimiting -- matching strict
      // Forth semantics, where every token is just a maximal run of non-whitespace
      // characters. A properly spaced "main ;" still tokenizes as two tokens
      // ("main" and ";") because whitespace already separates them below; a
      // glued "main;" tokenizes as the single word "main;", which then fails to
      // resolve as any known word -- exactly like a real Forth system would
      // reject it, rather than silently splitting it the way a C-like tokenizer
      // would. "A[" and "]]" now follow this exact same rule, with no exception.
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

        // ADDED 2026-10-02, alongside the new "/* ... */" block-comment handling above --
        // without this, a token glued directly to a block comment with no separating
        // whitespace (e.g. "foo/*note*/") would swallow the "/*" into the word itself
        // instead of ending the word there, the same reason the "//" check just above
        // already exists for the line-comment case.
        if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
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