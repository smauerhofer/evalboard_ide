namespace Ga144.C.Toolchain;

/// <summary>
/// Evaluates the controlling expression of an "#if"/"#elif" directive: a recursive-descent parser
/// over an already-macro-expanded token sequence, implementing exactly the subset of C's grammar a
/// constant-expression there can use (no assignment, no comma operator), with every value computed as
/// a 64-bit signed <see cref="long"/> regardless of any integer-literal suffix ("u"/"l"/"ul"/...) --
/// suffixes are recognized only so they can be stripped, never used to pick a different width or
/// signedness. That is a deliberate simplification: GA144 C's own integer types are far narrower than
/// 64 bits, and #if arithmetic overflowing a signed 64-bit long is not a case worth the extra
/// complexity of tracking C's full set of integer ranks and signedness-promotion rules.
///
/// The "defined" operator is not handled here -- <see cref="CPreprocessor"/> recognizes and replaces
/// every "defined X" / "defined(X)" with a literal "0" or "1" token before this evaluator ever runs,
/// since answering it requires the macro table, which this class deliberately knows nothing about.
/// By the time this class sees the token sequence, every remaining identifier is just an ordinary name
/// that was not a macro (a real macro would already have been expanded) -- per the C standard, each of
/// those evaluates to 0.
/// </summary>
public sealed class CConstantExpressionEvaluator
{
  private readonly IReadOnlyList<CToken> _tokens;
  private readonly IList<string> _diagnostics;
  private readonly CSourceLocation _fallbackLocation;
  private int _pos;
  private bool _failed;

  /// <summary>
  /// Greater than zero while parsing an operand of "&amp;&amp;", "||", or "?:" whose value cannot
  /// possibly affect the result -- exactly the operands real C compilers do not evaluate either, per
  /// the standard's short-circuit rule. The operand still has to be *parsed* (its tokens still have to
  /// be consumed so the parser ends up in the right place afterwards), just not evaluated in a way
  /// that can fail: while this is positive, a division or modulus by zero silently yields 0 rather
  /// than reporting an error, so e.g. "#if defined(X) &amp;&amp; (10 / X > 1)" does not spuriously
  /// fail to compile when X is not defined (so X, and thus 10 / X, would otherwise be a division by
  /// zero). Genuine syntax errors (an incomplete or malformed expression) are unaffected -- those are
  /// invalid regardless of which way a condition goes, and are still reported inside a suppressed
  /// operand.
  /// </summary>
  private int _suppressDepth;

  public CConstantExpressionEvaluator(IReadOnlyList<CToken> tokens, CSourceLocation fallbackLocation, IList<string> diagnostics)
  {
    _tokens = tokens;
    _fallbackLocation = fallbackLocation;
    _diagnostics = diagnostics;
  }

  /// <summary>Evaluates the whole expression. Returns false (after recording a diagnostic) if the
  /// expression is empty, malformed, or has trailing tokens left over after a complete expression was
  /// parsed.</summary>
  public bool TryEvaluate(out long value)
  {
    if (_tokens.Count == 0)
    {
      Fail(_fallbackLocation, "expected an expression after #if/#elif");
      value = 0;
      return false;
    }

    value = ParseConditional();
    if (!_failed && _pos < _tokens.Count)
    {
      Fail(Current().Location, $"unexpected token \"{Current().Text}\" in #if expression");
    }

    return !_failed;
  }

  private long ParseConditional()
  {
    long condition = ParseLogicalOr();
    if (!_failed && TryConsumePunctuator("?"))
    {
      long whenTrue = EvaluateOperand(ParseConditional, suppress: condition == 0);
      if (!_failed)
      {
        Expect(":");
      }

      long whenFalse = _failed ? 0 : EvaluateOperand(ParseConditional, suppress: condition != 0);
      return condition != 0 ? whenTrue : whenFalse;
    }

    return condition;
  }

  private long ParseLogicalOr()
  {
    long left = ParseLogicalAnd();
    while (!_failed && TryConsumePunctuator("||"))
    {
      long right = EvaluateOperand(ParseLogicalAnd, suppress: left != 0);
      left = (left != 0 || right != 0) ? 1 : 0;
    }

    return left;
  }

  private long ParseLogicalAnd()
  {
    long left = ParseInclusiveOr();
    while (!_failed && TryConsumePunctuator("&&"))
    {
      long right = EvaluateOperand(ParseInclusiveOr, suppress: left == 0);
      left = (left != 0 && right != 0) ? 1 : 0;
    }

    return left;
  }

  /// <summary>Parses one operand of "&amp;&amp;"/"||"/"?:", marking it as unevaluated (see
  /// <see cref="_suppressDepth"/>) when <paramref name="suppress"/> is true.</summary>
  private long EvaluateOperand(Func<long> parse, bool suppress)
  {
    if (!suppress)
    {
      return parse();
    }

    _suppressDepth++;
    try
    {
      return parse();
    }
    finally
    {
      _suppressDepth--;
    }
  }

  private long ParseInclusiveOr() => ParseBinary(ParseExclusiveOr, "|", (a, b) => a | b);

  private long ParseExclusiveOr() => ParseBinary(ParseAnd, "^", (a, b) => a ^ b);

  private long ParseAnd() => ParseBinary(ParseEquality, "&", (a, b) => a & b);

  private long ParseEquality()
  {
    long left = ParseRelational();
    while (!_failed)
    {
      if (TryConsumePunctuator("=="))
      {
        left = left == ParseRelational() ? 1 : 0;
      }
      else if (TryConsumePunctuator("!="))
      {
        left = left != ParseRelational() ? 1 : 0;
      }
      else
      {
        break;
      }
    }

    return left;
  }

  private long ParseRelational()
  {
    long left = ParseShift();
    while (!_failed)
    {
      if (TryConsumePunctuator("<"))
      {
        left = left < ParseShift() ? 1 : 0;
      }
      else if (TryConsumePunctuator(">"))
      {
        left = left > ParseShift() ? 1 : 0;
      }
      else if (TryConsumePunctuator("<="))
      {
        left = left <= ParseShift() ? 1 : 0;
      }
      else if (TryConsumePunctuator(">="))
      {
        left = left >= ParseShift() ? 1 : 0;
      }
      else
      {
        break;
      }
    }

    return left;
  }

  private long ParseShift()
  {
    long left = ParseAdditive();
    while (!_failed)
    {
      if (TryConsumePunctuator("<<"))
      {
        left <<= (int)ParseAdditive();
      }
      else if (TryConsumePunctuator(">>"))
      {
        left >>= (int)ParseAdditive();
      }
      else
      {
        break;
      }
    }

    return left;
  }

  private long ParseAdditive() => ParseBinary(ParseMultiplicative, "+", "-", (a, op, b) => op == "+" ? a + b : a - b);

  private long ParseMultiplicative()
  {
    long left = ParseUnary();
    while (!_failed)
    {
      if (TryConsumePunctuator("*"))
      {
        left *= ParseUnary();
      }
      else if (TryConsumePunctuator("/"))
      {
        CSourceLocation location = Previous().Location;
        long right = ParseUnary();
        if (!_failed)
        {
          left = right == 0 ? DivideByZero(location) : left / right;
        }
      }
      else if (TryConsumePunctuator("%"))
      {
        CSourceLocation location = Previous().Location;
        long right = ParseUnary();
        if (!_failed)
        {
          left = right == 0 ? DivideByZero(location) : left % right;
        }
      }
      else
      {
        break;
      }
    }

    return left;
  }

  private long ParseUnary()
  {
    if (TryConsumePunctuator("+"))
    {
      return ParseUnary();
    }

    if (TryConsumePunctuator("-"))
    {
      return -ParseUnary();
    }

    if (TryConsumePunctuator("!"))
    {
      return ParseUnary() == 0 ? 1 : 0;
    }

    if (TryConsumePunctuator("~"))
    {
      return ~ParseUnary();
    }

    return ParsePrimary();
  }

  private long ParsePrimary()
  {
    if (AtEnd())
    {
      Fail(_fallbackLocation, "expected an expression after #if/#elif");
      return 0;
    }

    CToken token = Current();

    if (TryConsumePunctuator("("))
    {
      long value = ParseConditional();
      if (!_failed)
      {
        Expect(")");
      }

      return value;
    }

    if (token.Kind == CTokenKind.Number)
    {
      _pos++;
      return ParseIntegerLiteral(token);
    }

    if (token.Kind == CTokenKind.CharLiteral)
    {
      _pos++;
      return ParseCharLiteral(token);
    }

    if (token.Kind == CTokenKind.Identifier)
    {
      // Any identifier reaching here was not a macro (macro expansion already ran), and is not
      // "defined" (CPreprocessor already resolved every "defined" operator) -- per the standard, it
      // simply evaluates to 0.
      _pos++;
      return 0;
    }

    Fail(token.Location, $"unexpected token \"{token.Text}\" in #if expression");
    return 0;
  }

  private long ParseIntegerLiteral(CToken token)
  {
    string text = token.Text;
    int end = text.Length;
    while (end > 0 && "uUlL".IndexOf(text[end - 1]) >= 0)
    {
      end--;
    }

    string digits = text[..end];
    try
    {
      if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
      {
        return (long)Convert.ToUInt64(digits[2..], 16);
      }

      if (digits.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
      {
        return (long)Convert.ToUInt64(digits[2..], 2);
      }

      if (digits.Length > 1 && digits[0] == '0' && digits[1..].All(c => c is >= '0' and <= '7'))
      {
        return (long)Convert.ToUInt64(digits, 8);
      }

      if (digits.Contains('.') || digits.Contains('e') || digits.Contains('E'))
      {
        Fail(token.Location, $"floating-point constant \"{token.Text}\" is not valid in a #if expression");
        return 0;
      }

      return (long)Convert.ToUInt64(digits, 10);
    }
    catch (Exception exception) when (exception is FormatException or OverflowException)
    {
      Fail(token.Location, $"invalid integer constant \"{token.Text}\" in #if expression");
      return 0;
    }
  }

  private long ParseCharLiteral(CToken token)
  {
    string text = token.Text;
    int start = text.IndexOf('\'') + 1;
    int end = text.LastIndexOf('\'');
    if (start <= 0 || end <= start)
    {
      Fail(token.Location, $"malformed character constant {token.Text}");
      return 0;
    }

    string body = text[start..end];
    long result = 0;
    int i = 0;
    while (i < body.Length)
    {
      long charValue;
      if (body[i] == '\\' && i + 1 < body.Length)
      {
        (charValue, i) = ReadEscape(body, i + 1);
      }
      else
      {
        charValue = body[i];
        i++;
      }

      // A multi-character constant is implementation-defined; this matches common practice of
      // packing each successive character into the low bytes of the result.
      result = (result << 8) | (charValue & 0xFF);
    }

    return result;
  }

  private static (long Value, int NextIndex) ReadEscape(string body, int index)
  {
    char c = body[index];
    switch (c)
    {
      case 'n': return ('\n', index + 1);
      case 't': return ('\t', index + 1);
      case 'r': return ('\r', index + 1);
      case '0': return (0, index + 1);
      case 'a': return (7, index + 1);
      case 'b': return (8, index + 1);
      case 'f': return (12, index + 1);
      case 'v': return (11, index + 1);
      case '\\': return ('\\', index + 1);
      case '\'': return ('\'', index + 1);
      case '"': return ('"', index + 1);
      case 'x':
        {
          int j = index + 1;
          int value = 0;
          while (j < body.Length && Uri.IsHexDigit(body[j]))
          {
            value = (value * 16) + Convert.ToInt32(body[j].ToString(), 16);
            j++;
          }

          return (value, j);
        }

      default:
        if (c is >= '0' and <= '7')
        {
          int j = index;
          int value = 0;
          int digits = 0;
          while (j < body.Length && body[j] is >= '0' and <= '7' && digits < 3)
          {
            value = (value * 8) + (body[j] - '0');
            j++;
            digits++;
          }

          return (value, j);
        }

        return (c, index + 1);
    }
  }

  private long ParseBinary(Func<long> operand, string op, Func<long, long, long> combine)
  {
    long left = operand();
    while (!_failed && TryConsumePunctuator(op))
    {
      left = combine(left, operand());
    }

    return left;
  }

  private long ParseBinary(Func<long> operand, string opA, string opB, Func<long, string, long, long> combine)
  {
    long left = operand();
    while (!_failed)
    {
      if (TryConsumePunctuator(opA))
      {
        left = combine(left, opA, operand());
      }
      else if (TryConsumePunctuator(opB))
      {
        left = combine(left, opB, operand());
      }
      else
      {
        break;
      }
    }

    return left;
  }

  private bool AtEnd() => _failed || _pos >= _tokens.Count;

  private CToken Current() => _tokens[_pos];

  private CToken Previous() => _tokens[_pos - 1];

  private bool TryConsumePunctuator(string text)
  {
    if (!AtEnd() && Current().IsPunctuator(text))
    {
      _pos++;
      return true;
    }

    return false;
  }

  private void Expect(string punctuator)
  {
    if (!TryConsumePunctuator(punctuator))
    {
      CSourceLocation location = AtEnd() ? _fallbackLocation : Current().Location;
      Fail(location, AtEnd() ? $"expected \"{punctuator}\"" : $"expected \"{punctuator}\" before \"{Current().Text}\"");
    }
  }

  /// <summary>A division or modulus by zero: an error, unless it occurs inside an operand that
  /// "&amp;&amp;"/"||"/"?:" short-circuiting means was never actually going to be used (see
  /// <see cref="_suppressDepth"/>), in which case it silently evaluates to 0 instead -- exactly as a
  /// real C compiler would never evaluate it at all.</summary>
  private long DivideByZero(CSourceLocation location) =>
      _suppressDepth > 0 ? 0 : Fail<long>(location, "division by zero in #if expression");

  private void Fail(CSourceLocation location, string message)
  {
    if (!_failed)
    {
      _diagnostics.Add(location.FormatDiagnostic("error", message));
      _failed = true;
    }
  }

  private T Fail<T>(CSourceLocation location, string message)
  {
    Fail(location, message);
    return default!;
  }
}
