namespace Ga144.C.Toolchain;

/// <summary>
/// The C preprocessor: consumes one C source file (plus, transitively, whatever it "#include"s
/// through <see cref="ICIncludeResolver"/>) and produces a fully macro-expanded token stream with
/// every directive already handled -- ready to be written back out as text (<see cref="CTokenWriter"/>)
/// or handed directly to a later compiler phase. One instance is good for exactly one
/// <see cref="Preprocess"/> call; create a new instance per file preprocessed.
///
/// Supported: object-like and function-like macros (stringize "#" and paste "##", including chained
/// "##" left-to-right), nested "#if"/"#ifdef"/"#ifndef"/"#elif"/"#else"/"#endif" with the standard's
/// short-circuit rule (a dead branch's controlling expression is never evaluated, and once one branch
/// of a group has been taken, later "#elif"s in the same group are not evaluated either), "#include"
/// (both quoted and angle-bracketed, plus one level of macro-expanded "#include SOME_HEADER"),
/// "#pragma once", "#define"/"#undef", "#error", and the predefined macros "__CVM__", "__DATE__",
/// "__TIME__", "__LINE__", and "__FILE__".
///
/// Deliberately not supported: variadic macros ("..."/"__VA_ARGS__"), trigraphs, "#line" (recognized
/// and silently ignored), any "#pragma" other than "once" (silently ignored), and a function-like
/// macro invocation whose name comes from another macro's own expansion needing to look past that
/// expansion's boundary to find its "(" (its "(" must appear within that same expansion). All "#if"/
/// "#elif" arithmetic is done as a 64-bit signed <see cref="long"/> regardless of any integer-literal
/// suffix -- see <see cref="CConstantExpressionEvaluator"/>. An empty macro argument used directly as
/// a "##" paste operand is a further known rough edge: it may not vanish as cleanly as strict C
/// requires.
/// </summary>
public sealed class CPreprocessor
{
  private const int MaxIncludeDepth = 200;

  private readonly ICIncludeResolver _includeResolver;
  private readonly Dictionary<string, CMacroDefinition> _macros = new(StringComparer.Ordinal);
  private readonly HashSet<string> _dynamicMacros = new(StringComparer.Ordinal);
  private readonly HashSet<string> _expandingMacros = new(StringComparer.Ordinal);
  private readonly HashSet<string> _pragmaOnceFiles = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _includeStack = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> _includedFiles = [];
  private readonly List<string> _diagnostics = [];
  private bool _hasError;
  private bool _used;

  public CPreprocessor(ICIncludeResolver includeResolver)
  {
    _includeResolver = includeResolver;
    DefinePredefinedMacros();
  }

  /// <summary>
  /// Defines a simple object-like macro before preprocessing starts, exactly as "-D NAME" or
  /// "-D NAME=VALUE" on a compiler command line would -- there is no way to spell a function-like
  /// macro this way, matching how every mainstream C compiler's "-D" flag works. Must be called before
  /// <see cref="Preprocess"/>.
  /// </summary>
  public void DefineFromCommandLine(string name, string value)
  {
    if (_used)
    {
      throw new InvalidOperationException("Command-line macros must be defined before Preprocess() is called.");
    }

    DefineSimpleMacro(name, value.Length == 0 ? "1" : value, "<command line>");
  }

  public CPreprocessResult Preprocess(string fileName, string sourceText)
  {
    if (_used)
    {
      throw new InvalidOperationException($"{nameof(CPreprocessor)} instances are single-use -- create a new one for each file preprocessed.");
    }

    _used = true;

    var output = new List<CToken>();
    _includeStack.Add(fileName);
    try
    {
      ProcessFile(fileName, sourceText, output, new Stack<ConditionalFrame>(), includeDepth: 0);
    }
    finally
    {
      _includeStack.Remove(fileName);
    }

    output.Add(new CToken(CTokenKind.EndOfFile, string.Empty, new CSourceLocation(fileName, 1, 1), IsAtLineStart: true, HasLeadingWhitespace: false));
    return new CPreprocessResult
    {
      Success = !_hasError,
      Tokens = output,
      Diagnostics = [.. _diagnostics],
      IncludedFiles = [.. _includedFiles],
    };
  }

  // ---- Per-file driver -----------------------------------------------------------------------

  private void ProcessFile(string fileName, string sourceText, List<CToken> output, Stack<ConditionalFrame> conditionalStack, int includeDepth)
  {
    int diagnosticsBeforeLexing = _diagnostics.Count;
    IReadOnlyList<CToken> tokens = CLexer.Tokenize(fileName, sourceText, _diagnostics);
    if (_diagnostics.Count > diagnosticsBeforeLexing)
    {
      // The lexer only ever reports genuine errors (an unterminated comment or string/char literal),
      // never warnings.
      _hasError = true;
    }

    int startDepth = conditionalStack.Count;
    int i = 0;
    while (i < tokens.Count && tokens[i].Kind != CTokenKind.EndOfFile)
    {
      CToken token = tokens[i];
      if (token.IsAtLineStart && token.IsPunctuator("#"))
      {
        ProcessDirectiveLine(tokens, ref i, fileName, output, conditionalStack, includeDepth);
        continue;
      }

      if (!IsActive(conditionalStack))
      {
        i++;
        continue;
      }

      ExpandNextUnit(tokens, ref i, output);
    }

    if (conditionalStack.Count != startDepth)
    {
      CSourceLocation location = tokens.Count > 0 ? tokens[^1].Location : new CSourceLocation(fileName, 1, 1);
      AddError(location, "unterminated conditional directive: missing #endif");
      while (conditionalStack.Count > startDepth)
      {
        conditionalStack.Pop();
      }
    }
  }

  private void ProcessDirectiveLine(IReadOnlyList<CToken> tokens, ref int i, string fileName, List<CToken> output, Stack<ConditionalFrame> conditionalStack, int includeDepth)
  {
    CSourceLocation hashLocation = tokens[i].Location;
    i++;

    int lineEnd = i;
    while (lineEnd < tokens.Count && tokens[lineEnd].Kind is not (CTokenKind.NewLine or CTokenKind.EndOfFile))
    {
      lineEnd++;
    }

    bool active = IsActive(conditionalStack);

    if (i < lineEnd)
    {
      CToken keywordToken = tokens[i];
      string keyword = keywordToken.IsIdentifier ? keywordToken.Text : string.Empty;
      List<CToken> body = tokens.Skip(i + 1).Take(lineEnd - (i + 1)).ToList();

      switch (keyword)
      {
        case "if":
          HandleIf(body, hashLocation, conditionalStack, active);
          break;
        case "ifdef":
          HandleIfdef(body, hashLocation, conditionalStack, active, negate: false);
          break;
        case "ifndef":
          HandleIfdef(body, hashLocation, conditionalStack, active, negate: true);
          break;
        case "elif":
          HandleElif(body, hashLocation, conditionalStack);
          break;
        case "else":
          HandleElse(hashLocation, conditionalStack);
          break;
        case "endif":
          HandleEndif(hashLocation, conditionalStack);
          break;
        default:
          if (active)
          {
            switch (keyword)
            {
              case "define":
                HandleDefine(body, hashLocation);
                break;
              case "undef":
                HandleUndef(body, hashLocation);
                break;
              case "include":
                HandleInclude(body, hashLocation, fileName, output, conditionalStack, includeDepth);
                break;
              case "error":
                HandleError(body, hashLocation);
                break;
              case "pragma":
                HandlePragma(body, fileName);
                break;
              case "line":
                // Recognized, deliberately ignored -- nothing downstream of this preprocessor consumes
                // #line's line/file renumbering.
                break;
              case "":
                AddError(hashLocation, "expected a preprocessing directive name after '#'");
                break;
              default:
                AddError(hashLocation, $"unknown preprocessing directive \"#{keyword}\"");
                break;
            }
          }

          break;
      }
    }

    i = lineEnd < tokens.Count && tokens[lineEnd].Kind == CTokenKind.NewLine ? lineEnd + 1 : lineEnd;
  }

  // ---- Conditional compilation ("#if" family) -------------------------------------------------

  private sealed class ConditionalFrame
  {
    public required bool ParentActive;
    public bool AnyBranchTrue;
    public bool CurrentBranchActive;
    public bool HasElse;
  }

  private static bool IsActive(Stack<ConditionalFrame> stack) =>
      stack.Count == 0 || (stack.Peek().ParentActive && stack.Peek().CurrentBranchActive);

  private void HandleIf(List<CToken> body, CSourceLocation hashLocation, Stack<ConditionalFrame> stack, bool parentActive)
  {
    bool value = parentActive && EvaluateConstantExpression(ExpandDefinedThenMacros(body), hashLocation);
    stack.Push(new ConditionalFrame { ParentActive = parentActive, CurrentBranchActive = value, AnyBranchTrue = value });
  }

  private void HandleIfdef(List<CToken> body, CSourceLocation hashLocation, Stack<ConditionalFrame> stack, bool parentActive, bool negate)
  {
    bool value = false;
    if (parentActive)
    {
      if (body.Count == 0 || !body[0].IsIdentifier)
      {
        AddError(hashLocation, $"expected a macro name after #if{(negate ? "ndef" : "def")}");
      }
      else
      {
        bool defined = _macros.ContainsKey(body[0].Text);
        value = negate ? !defined : defined;
      }
    }

    stack.Push(new ConditionalFrame { ParentActive = parentActive, CurrentBranchActive = value, AnyBranchTrue = value });
  }

  private void HandleElif(List<CToken> body, CSourceLocation hashLocation, Stack<ConditionalFrame> stack)
  {
    if (stack.Count == 0)
    {
      AddError(hashLocation, "#elif without a matching #if");
      return;
    }

    ConditionalFrame frame = stack.Peek();
    if (!frame.ParentActive || frame.AnyBranchTrue)
    {
      // Either the whole group is inside an already-inactive context, or an earlier branch in this
      // same group already matched -- either way, per the standard's short-circuit rule, this
      // controlling expression must not be evaluated at all.
      frame.CurrentBranchActive = false;
      if (frame.HasElse && frame.ParentActive)
      {
        AddError(hashLocation, "#elif after #else");
      }

      return;
    }

    bool value = EvaluateConstantExpression(ExpandDefinedThenMacros(body), hashLocation);
    frame.CurrentBranchActive = value;
    frame.AnyBranchTrue = value;
  }

  private void HandleElse(CSourceLocation hashLocation, Stack<ConditionalFrame> stack)
  {
    if (stack.Count == 0)
    {
      AddError(hashLocation, "#else without a matching #if");
      return;
    }

    ConditionalFrame frame = stack.Peek();
    if (frame.HasElse)
    {
      AddError(hashLocation, "#else after #else");
    }

    frame.HasElse = true;
    frame.CurrentBranchActive = frame.ParentActive && !frame.AnyBranchTrue;
    frame.AnyBranchTrue = true;
  }

  private void HandleEndif(CSourceLocation hashLocation, Stack<ConditionalFrame> stack)
  {
    if (stack.Count == 0)
    {
      AddError(hashLocation, "#endif without a matching #if");
      return;
    }

    stack.Pop();
  }

  private bool EvaluateConstantExpression(List<CToken> expandedBody, CSourceLocation hashLocation)
  {
    var evaluator = new CConstantExpressionEvaluator(expandedBody, hashLocation, _diagnostics);
    bool ok = evaluator.TryEvaluate(out long value);
    if (!ok)
    {
      _hasError = true;
      return false;
    }

    return value != 0;
  }

  /// <summary>Resolves every "defined X" / "defined(X)" in an "#if"/"#elif" expression to a literal
  /// 0/1 token first (the operand must not itself be macro-expanded), then macro-expands whatever is
  /// left, exactly as the standard requires.</summary>
  private List<CToken> ExpandDefinedThenMacros(List<CToken> body)
  {
    var afterDefined = new List<CToken>();
    int j = 0;
    while (j < body.Count)
    {
      CToken token = body[j];
      if (token.IsIdentifier && token.Text == "defined")
      {
        CSourceLocation location = token.Location;
        j++;
        bool parenthesized = j < body.Count && body[j].IsPunctuator("(");
        if (parenthesized)
        {
          j++;
        }

        if (j >= body.Count || !body[j].IsIdentifier)
        {
          AddError(location, "expected a macro name after \"defined\"");
          afterDefined.Add(new CToken(CTokenKind.Number, "0", location, false, true));
          continue;
        }

        string name = body[j].Text;
        j++;

        if (parenthesized)
        {
          if (j < body.Count && body[j].IsPunctuator(")"))
          {
            j++;
          }
          else
          {
            AddError(location, $"missing ')' after \"defined({name}\"");
          }
        }

        afterDefined.Add(new CToken(CTokenKind.Number, _macros.ContainsKey(name) ? "1" : "0", location, false, true));
        continue;
      }

      afterDefined.Add(token);
      j++;
    }

    return ExpandTokenList(afterDefined);
  }

  // ---- "#define" / "#undef" / "#error" / "#pragma" ---------------------------------------------

  private void HandleDefine(List<CToken> body, CSourceLocation hashLocation)
  {
    if (body.Count == 0 || !body[0].IsIdentifier)
    {
      AddError(hashLocation, "macro name missing after #define");
      return;
    }

    CToken nameToken = body[0];
    string name = nameToken.Text;
    if (name == "defined")
    {
      AddError(nameToken.Location, "\"defined\" cannot be used as a macro name");
      return;
    }

    bool isFunctionLike = body.Count > 1 && body[1].IsPunctuator("(") && !body[1].HasLeadingWhitespace;
    var parameters = new List<string>();
    int bodyStart = 1;

    if (isFunctionLike)
    {
      int k = 2;
      if (k < body.Count && body[k].IsPunctuator(")"))
      {
        k++;
      }
      else
      {
        while (true)
        {
          if (k >= body.Count || !body[k].IsIdentifier)
          {
            AddError(hashLocation, $"expected a parameter name in the definition of macro \"{name}\"");
            return;
          }

          if (parameters.Contains(body[k].Text))
          {
            AddError(body[k].Location, $"duplicate macro parameter \"{body[k].Text}\"");
            return;
          }

          parameters.Add(body[k].Text);
          k++;

          if (k < body.Count && body[k].IsPunctuator(","))
          {
            k++;
            continue;
          }

          if (k < body.Count && body[k].IsPunctuator(")"))
          {
            k++;
            break;
          }

          AddError(hashLocation, $"expected ',' or ')' in the parameter list of macro \"{name}\"");
          return;
        }
      }

      bodyStart = k;
    }

    List<CToken> replacement = body.Skip(bodyStart).ToList();
    if (replacement.Count > 0)
    {
      replacement[0] = replacement[0] with { HasLeadingWhitespace = false };

      if (replacement[0].IsPunctuator("##"))
      {
        AddError(replacement[0].Location, $"'##' cannot appear at the start of macro \"{name}\"'s replacement list");
        return;
      }

      if (replacement[^1].IsPunctuator("##"))
      {
        AddError(replacement[^1].Location, $"'##' cannot appear at the end of macro \"{name}\"'s replacement list");
        return;
      }
    }

    if (isFunctionLike)
    {
      for (int k = 0; k < replacement.Count; k++)
      {
        if (replacement[k].IsPunctuator("#") &&
            (k + 1 >= replacement.Count || !replacement[k + 1].IsIdentifier || !parameters.Contains(replacement[k + 1].Text)))
        {
          AddError(replacement[k].Location, "'#' is not followed by a macro parameter");
          return;
        }
      }
    }

    var macro = new CMacroDefinition
    {
      Name = name,
      IsFunctionLike = isFunctionLike,
      Parameters = parameters,
      ReplacementList = replacement,
      DefinedAt = nameToken.Location,
    };

    if (_macros.TryGetValue(name, out CMacroDefinition? existing) && !existing.IsIdenticalTo(macro))
    {
      AddWarning(nameToken.Location, $"macro \"{name}\" redefined");
    }

    _macros[name] = macro;
    _dynamicMacros.Remove(name);
  }

  private void HandleUndef(List<CToken> body, CSourceLocation hashLocation)
  {
    if (body.Count == 0 || !body[0].IsIdentifier)
    {
      AddError(hashLocation, "macro name missing after #undef");
      return;
    }

    _macros.Remove(body[0].Text);
    _dynamicMacros.Remove(body[0].Text);
  }

  private void HandleError(List<CToken> body, CSourceLocation hashLocation)
  {
    string message = CTokenWriter.Write(body).Trim();
    AddError(hashLocation, message.Length == 0 ? "#error" : $"#error {message}");
  }

  private void HandlePragma(List<CToken> body, string fileName)
  {
    if (body.Count > 0 && body[0].IsIdentifier && body[0].Text == "once")
    {
      _pragmaOnceFiles.Add(fileName);
    }

    // Every other #pragma is silently ignored: none apply to preprocessing, and no later compiler
    // phase defines any of its own yet.
  }

  // ---- "#include" ------------------------------------------------------------------------------

  private void HandleInclude(List<CToken> body, CSourceLocation hashLocation, string includingFileName, List<CToken> output, Stack<ConditionalFrame> conditionalStack, int includeDepth)
  {
    if (!TryExtractIncludeTarget(body, out string includeName, out bool isAngled, out bool malformed))
    {
      if (malformed)
      {
        AddError(hashLocation, "missing closing '>' in #include");
        return;
      }

      // Not a literal filename -- try macro-expanding the line once (e.g. "#include SOME_HEADER")
      // and retry against the result; deliberately only ever one expansion attempt (never recursive),
      // so a line that still isn't a literal after this is reported rather than expanded forever.
      List<CToken> expanded = ExpandTokenList(body);
      if (!TryExtractIncludeTarget(expanded, out includeName, out isAngled, out malformed))
      {
        AddError(hashLocation, malformed ? "missing closing '>' in #include" : "expected \"FILENAME\" or <FILENAME> after #include");
        return;
      }
    }

    if (string.IsNullOrEmpty(includeName))
    {
      AddError(hashLocation, "empty filename in #include");
      return;
    }

    if (includeDepth + 1 > MaxIncludeDepth)
    {
      AddError(hashLocation, $"#include nested too deeply (possible #include cycle) resolving \"{includeName}\"");
      return;
    }

    if (!_includeResolver.TryResolve(includeName, isAngled, includingFileName, out CResolvedInclude? resolved) || resolved is null)
    {
      AddError(hashLocation, $"cannot open include file: \"{includeName}\"");
      return;
    }

    if (_pragmaOnceFiles.Contains(resolved.FileName))
    {
      return;
    }

    if (!_includeStack.Add(resolved.FileName))
    {
      AddError(hashLocation, $"\"{resolved.FileName}\" includes itself, directly or indirectly");
      return;
    }

    _includedFiles.Add(resolved.FileName);
    try
    {
      ProcessFile(resolved.FileName, resolved.Content, output, conditionalStack, includeDepth + 1);
    }
    finally
    {
      _includeStack.Remove(resolved.FileName);
    }
  }

  /// <summary>
  /// Reads the filename out of an "#include" line's tokens, if the very first token already makes it
  /// unambiguous: a string literal ("#include \"name.h\" ..."), or a "&lt;...&gt;" pair. Anything after
  /// the filename itself is ignored (most compilers are similarly lenient about stray trailing tokens
  /// on an #include line). Returns false with <paramref name="malformed"/> set only for an opened-but-
  /// never-closed "&lt;"; returns false with it clear when the line does not start with either shape at
  /// all (the caller's cue to try macro-expanding the line once before giving up).
  /// </summary>
  private static bool TryExtractIncludeTarget(List<CToken> body, out string includeName, out bool isAngled, out bool malformed)
  {
    malformed = false;

    if (body.Count > 0 && body[0].Kind == CTokenKind.StringLiteral && body[0].Text.Length >= 2)
    {
      includeName = body[0].Text[1..^1];
      isAngled = false;
      return true;
    }

    if (body.Count > 0 && body[0].IsPunctuator("<"))
    {
      int closeIndex = body.FindIndex(t => t.IsPunctuator(">"));
      if (closeIndex < 0)
      {
        includeName = string.Empty;
        isAngled = true;
        malformed = true;
        return false;
      }

      includeName = CTokenWriter.Write(body.GetRange(1, closeIndex - 1));
      isAngled = true;
      return true;
    }

    includeName = string.Empty;
    isAngled = false;
    return false;
  }

  // ---- Macro expansion ---------------------------------------------------------------------------

  /// <summary>Consumes one token at <paramref name="tokens"/>[<paramref name="i"/>], appending its
  /// (possibly multi-token) expansion to <paramref name="output"/> and advancing <paramref name="i"/>
  /// past whatever it consumed. This is the single core expansion primitive: the top-level file driver
  /// calls it directly against the file's own token list (which can include real newlines, so a
  /// function-like macro's argument list may span several physical lines), and macro-body/argument
  /// rescanning (<see cref="ExpandTokenList"/>) calls it against a small in-memory list instead --
  /// both cases are otherwise identical from this method's point of view.</summary>
  private void ExpandNextUnit(IReadOnlyList<CToken> tokens, ref int i, List<CToken> output)
  {
    CToken token = tokens[i];
    if (token.Kind == CTokenKind.NewLine)
    {
      output.Add(token);
      i++;
      return;
    }

    if (TryExpandIdentifierAt(tokens, ref i, output))
    {
      return;
    }

    output.Add(token);
    i++;
  }

  private List<CToken> ExpandTokenList(List<CToken> input)
  {
    var result = new List<CToken>();
    int j = 0;
    while (j < input.Count)
    {
      ExpandNextUnit(input, ref j, result);
    }

    return result;
  }

  private bool TryExpandIdentifierAt(IReadOnlyList<CToken> tokens, ref int i, List<CToken> output)
  {
    CToken token = tokens[i];
    if (!token.IsIdentifier || !_macros.TryGetValue(token.Text, out CMacroDefinition? macro))
    {
      return false;
    }

    if (_dynamicMacros.Contains(macro.Name))
    {
      output.Add(MakeDynamicMacroToken(token));
      i++;
      return true;
    }

    if (_expandingMacros.Contains(macro.Name))
    {
      // Self-reference (direct or indirect) -- leave this occurrence unexpanded rather than recursing
      // forever, exactly as the standard requires.
      return false;
    }

    ExpandMacroCall(tokens, ref i, macro, output);
    return true;
  }

  private void ExpandMacroCall(IReadOnlyList<CToken> tokens, ref int i, CMacroDefinition macro, List<CToken> output)
  {
    CToken nameToken = tokens[i];

    if (!macro.IsFunctionLike)
    {
      List<CToken> expansion = ExpandReplacementList(macro, rawArgs: null, expandedArgs: null);
      AppendWithLeadingWhitespace(output, expansion, nameToken);
      i++;
      return;
    }

    int startIndex = i;
    if (!TryCollectArguments(tokens, ref i, macro, out List<List<CToken>>? rawArgs))
    {
      if (i == startIndex)
      {
        // No "(" followed the name -- it was never actually invoked.
        output.Add(nameToken);
        i++;
      }

      // Otherwise TryCollectArguments already reported an error and left `i` past the damage.
      return;
    }

    List<List<CToken>> expandedArgs = rawArgs!.Select(ExpandTokenList).ToList();
    List<CToken> functionExpansion = ExpandReplacementList(macro, rawArgs, expandedArgs);
    AppendWithLeadingWhitespace(output, functionExpansion, nameToken);
  }

  private static void AppendWithLeadingWhitespace(List<CToken> output, List<CToken> expansion, CToken originalFirstToken)
  {
    for (int k = 0; k < expansion.Count; k++)
    {
      output.Add(k == 0 ? expansion[k] with { HasLeadingWhitespace = originalFirstToken.HasLeadingWhitespace } : expansion[k]);
    }
  }

  private List<CToken> ExpandReplacementList(CMacroDefinition macro, List<List<CToken>>? rawArgs, List<List<CToken>>? expandedArgs)
  {
    List<CToken> substituted = macro.IsFunctionLike
        ? BuildFunctionLikeReplacement(macro, rawArgs!, expandedArgs!)
        : ResolvePastes(macro.ReplacementList.ToList(), macro.Name);

    _expandingMacros.Add(macro.Name);
    try
    {
      return ExpandTokenList(substituted);
    }
    finally
    {
      _expandingMacros.Remove(macro.Name);
    }
  }

  private List<CToken> BuildFunctionLikeReplacement(CMacroDefinition macro, List<List<CToken>> rawArgs, List<List<CToken>> expandedArgs)
  {
    var intermediate = new List<CToken>();
    IReadOnlyList<CToken> replacement = macro.ReplacementList;

    for (int k = 0; k < replacement.Count; k++)
    {
      CToken token = replacement[k];

      if (token.IsPunctuator("#") && k + 1 < replacement.Count && replacement[k + 1].IsIdentifier &&
          TryFindParameterIndex(macro, replacement[k + 1].Text, out int stringizeParamIndex))
      {
        string stringized = CTokenWriter.Stringize(rawArgs[stringizeParamIndex]);
        intermediate.Add(new CToken(CTokenKind.StringLiteral, "\"" + stringized + "\"", token.Location, false, token.HasLeadingWhitespace));
        k++;
        continue;
      }

      if (token.IsIdentifier && TryFindParameterIndex(macro, token.Text, out int paramIndex))
      {
        bool adjacentToPaste = (k + 1 < replacement.Count && replacement[k + 1].IsPunctuator("##")) ||
                                (k > 0 && replacement[k - 1].IsPunctuator("##"));
        List<CToken> tokensToInsert = adjacentToPaste ? rawArgs[paramIndex] : expandedArgs[paramIndex];
        for (int m = 0; m < tokensToInsert.Count; m++)
        {
          intermediate.Add(m == 0 ? tokensToInsert[m] with { HasLeadingWhitespace = token.HasLeadingWhitespace } : tokensToInsert[m]);
        }

        continue;
      }

      intermediate.Add(token);
    }

    return ResolvePastes(intermediate, macro.Name);
  }

  private static bool TryFindParameterIndex(CMacroDefinition macro, string name, out int index)
  {
    if (macro.IsFunctionLike)
    {
      for (int p = 0; p < macro.Parameters.Count; p++)
      {
        if (macro.Parameters[p] == name)
        {
          index = p;
          return true;
        }
      }
    }

    index = -1;
    return false;
  }

  private List<CToken> ResolvePastes(List<CToken> tokens, string macroName)
  {
    var result = new List<CToken>();
    int k = 0;
    while (k < tokens.Count)
    {
      if (tokens[k].IsPunctuator("##"))
      {
        if (result.Count == 0 || k + 1 >= tokens.Count)
        {
          AddError(tokens[k].Location, $"'##' cannot appear at either end of macro \"{macroName}\"'s replacement list");
          k++;
          continue;
        }

        CToken left = result[^1];
        CToken right = tokens[k + 1];
        result.RemoveAt(result.Count - 1);
        result.Add(PasteTokens(left, right, macroName));
        k += 2;
        continue;
      }

      result.Add(tokens[k]);
      k++;
    }

    return result;
  }

  private CToken PasteTokens(CToken left, CToken right, string macroName)
  {
    string combinedText = left.Text + right.Text;

    if (IsValidIdentifierText(combinedText))
    {
      return new CToken(CTokenKind.Identifier, combinedText, left.Location, false, left.HasLeadingWhitespace);
    }

    if (IsValidNumberText(combinedText))
    {
      return new CToken(CTokenKind.Number, combinedText, left.Location, false, left.HasLeadingWhitespace);
    }

    if (Array.IndexOf(CLexer.MultiCharPunctuators, combinedText) >= 0 ||
        (combinedText.Length == 1 && CLexer.SingleCharPunctuators.IndexOf(combinedText[0]) >= 0))
    {
      return new CToken(CTokenKind.Punctuator, combinedText, left.Location, false, left.HasLeadingWhitespace);
    }

    AddError(left.Location, $"pasting \"{left.Text}\" and \"{right.Text}\" does not give a valid preprocessing token, in macro \"{macroName}\"");
    return new CToken(CTokenKind.Other, combinedText, left.Location, false, left.HasLeadingWhitespace);
  }

  private static bool IsValidIdentifierText(string text) =>
      text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.Skip(1).All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

  private static bool IsValidNumberText(string text) =>
      text.Length > 0 && (char.IsAsciiDigit(text[0]) || (text[0] == '.' && text.Length > 1 && char.IsAsciiDigit(text[1]))) &&
      text.Skip(1).All(c => char.IsAsciiLetterOrDigit(c) || c == '.');

  /// <summary>Collects a function-like macro invocation's argument list. On success, advances
  /// <paramref name="i"/> to just past the closing ')' and returns true. On failure with
  /// <paramref name="i"/> left unchanged, the macro name was not actually followed by '(' at all (not
  /// an invocation this time). On failure with <paramref name="i"/> advanced, an unterminated-argument
  /// error was already reported.</summary>
  private bool TryCollectArguments(IReadOnlyList<CToken> tokens, ref int i, CMacroDefinition macro, out List<List<CToken>>? rawArguments)
  {
    int j = i + 1;
    while (j < tokens.Count && tokens[j].Kind == CTokenKind.NewLine)
    {
      j++;
    }

    if (j >= tokens.Count || !tokens[j].IsPunctuator("("))
    {
      rawArguments = null;
      return false;
    }

    j++;
    var arguments = new List<List<CToken>>();
    var current = new List<CToken>();
    int parenDepth = 1;
    bool pendingWhitespace = false;

    while (true)
    {
      if (j >= tokens.Count || tokens[j].Kind == CTokenKind.EndOfFile)
      {
        AddError(tokens[i].Location, $"unterminated argument list invoking macro \"{macro.Name}\"");
        i = j;
        rawArguments = null;
        return false;
      }

      CToken token = tokens[j];

      if (token.Kind == CTokenKind.NewLine)
      {
        pendingWhitespace = true;
        j++;
        continue;
      }

      if (token.IsPunctuator("("))
      {
        parenDepth++;
      }
      else if (token.IsPunctuator(")"))
      {
        parenDepth--;
        if (parenDepth == 0)
        {
          arguments.Add(current);
          j++;
          break;
        }
      }
      else if (token.IsPunctuator(",") && parenDepth == 1)
      {
        arguments.Add(current);
        current = [];
        j++;
        pendingWhitespace = false;
        continue;
      }

      current.Add(pendingWhitespace ? token with { HasLeadingWhitespace = true } : token);
      pendingWhitespace = false;
      j++;
    }

    i = j;

    if (arguments.Count == 1 && arguments[0].Count == 0 && macro.Parameters.Count == 0)
    {
      // "MACRO()" invoking a zero-parameter macro is one call with no arguments, not one empty argument.
      arguments.Clear();
    }

    if (arguments.Count != macro.Parameters.Count)
    {
      AddError(tokens[i - 1].Location, $"macro \"{macro.Name}\" requires {macro.Parameters.Count} argument(s), but {arguments.Count} given");
      while (arguments.Count < macro.Parameters.Count)
      {
        arguments.Add([]);
      }
    }

    rawArguments = arguments;
    return true;
  }

  // ---- Predefined macros --------------------------------------------------------------------------

  private void DefinePredefinedMacros()
  {
    DateTime now = DateTime.Now;
    DefineSimpleMacro("__CVM__", "1", "<builtin>");
    DefineSimpleMacro("__DATE__", $"\"{now:MMM dd yyyy}\"", "<builtin>");
    DefineSimpleMacro("__TIME__", $"\"{now:HH:mm:ss}\"", "<builtin>");
    DefineDynamicPlaceholder("__LINE__");
    DefineDynamicPlaceholder("__FILE__");
  }

  /// <summary>Defines a plain object-like macro directly from already-known text, bypassing
  /// "#define" parsing entirely -- used both for the fixed predefined macros and for "-D" command-line
  /// macros, neither of which ever needs a parameter list.</summary>
  private void DefineSimpleMacro(string name, string tokenText, string locationFileName)
  {
    var ignoredDiagnostics = new List<string>();
    IReadOnlyList<CToken> tokens = CLexer.Tokenize(locationFileName, tokenText, ignoredDiagnostics);
    List<CToken> replacement = tokens.Where(t => t.Kind is not (CTokenKind.NewLine or CTokenKind.EndOfFile)).ToList();
    _macros[name] = new CMacroDefinition
    {
      Name = name,
      IsFunctionLike = false,
      ReplacementList = replacement,
      DefinedAt = new CSourceLocation(locationFileName, 0, 0),
    };
    _dynamicMacros.Remove(name);
  }

  private void DefineDynamicPlaceholder(string name)
  {
    _macros[name] = new CMacroDefinition { Name = name, IsFunctionLike = false, DefinedAt = new CSourceLocation("<builtin>", 0, 0) };
    _dynamicMacros.Add(name);
  }

  private static CToken MakeDynamicMacroToken(CToken token) =>
      token.Text == "__LINE__"
          ? new CToken(CTokenKind.Number, token.Location.Line.ToString(), token.Location, false, token.HasLeadingWhitespace)
          : new CToken(CTokenKind.StringLiteral, $"\"{EscapeForStringLiteral(token.Location.FileName)}\"", token.Location, false, token.HasLeadingWhitespace);

  private static string EscapeForStringLiteral(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

  // ---- Diagnostics ---------------------------------------------------------------------------------

  private void AddError(CSourceLocation location, string message)
  {
    _diagnostics.Add(location.FormatDiagnostic("error", message));
    _hasError = true;
  }

  private void AddWarning(CSourceLocation location, string message) => _diagnostics.Add(location.FormatDiagnostic("warning", message));
}
