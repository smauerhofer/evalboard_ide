namespace Ga144.C.Toolchain;

/// <summary>
/// A recursive-descent parser for the C subset this compiler supports (see the design doc for the
/// full scope list). Input is an already-preprocessed token stream (macros expanded, directives gone)
/// -- NewLine tokens carry no meaning in C's grammar and are stripped before parsing starts.
///
/// Not supported, with a clear diagnostic rather than silent misbehavior: struct/union/enum/typedef,
/// float/double/long/short, multi-dimensional arrays, function pointers, bit-fields, variadic
/// functions, and the "." / "->" member-access operators (all of which need structs). "goto"/labels,
/// "switch"/"case"/"default", and every operator standard C defines over this compiler's supported
/// types ARE supported.
/// </summary>
public sealed class CParser
{
  private static readonly HashSet<string> ReservedWords =
  [
    "void", "char", "int", "unsigned", "signed", "static", "extern", "const", "volatile",
    "if", "else", "while", "do", "for", "return", "break", "continue", "goto", "switch", "case", "default",
    "sizeof", "struct", "union", "enum", "typedef", "float", "double", "long", "short", "auto", "register",
  ];

  private static readonly HashSet<string> UnsupportedTypeKeywords = ["struct", "union", "enum", "typedef", "float", "double", "long", "short"];

  private readonly List<CToken> _tokens;
  private readonly List<string> _diagnostics = [];
  private int _pos;

  public CParser(IReadOnlyList<CToken> tokens)
  {
    _tokens = tokens.Where(t => t.Kind != CTokenKind.NewLine).ToList();
  }

  /// <summary>A local, statement-level parse failure -- caught and recovered from by skipping to the
  /// next likely-safe boundary (a ";" or a matching "}"), so one mistake doesn't stop every other
  /// diagnostic in the file from being reported too.</summary>
  private sealed class CParseException : Exception;

  public (CTranslationUnit? Unit, IReadOnlyList<string> Diagnostics) Parse()
  {
    var functions = new List<CFunctionDecl>();
    var globals = new List<CGlobalVarDecl>();

    while (!AtEnd)
    {
      try
      {
        ParseExternalDeclaration(functions, globals);
      }
      catch (CParseException)
      {
        SynchronizeAfterError(atTopLevel: true);
      }
    }

    bool hasError = _diagnostics.Any(d => d.Contains(": error:"));
    return (hasError ? null : new CTranslationUnit(functions, globals), _diagnostics);
  }

  // ---- Token cursor ------------------------------------------------------------------------------

  private CToken Current => _tokens[_pos];

  private bool AtEnd => Current.Kind == CTokenKind.EndOfFile;

  private CToken Advance()
  {
    CToken token = Current;
    if (!AtEnd)
    {
      _pos++;
    }

    return token;
  }

  private bool CheckWord(string text) => (Current.IsIdentifier || Current.Kind == CTokenKind.Punctuator) && Current.Text == text;

  private bool Match(string text)
  {
    if (CheckWord(text))
    {
      Advance();
      return true;
    }

    return false;
  }

  private void Expect(string text, string context)
  {
    if (!Match(text))
    {
      throw Error(Current.Location, $"expected '{text}' {context}, found \"{Current.Text}\"");
    }
  }

  private string ExpectIdentifier(string context)
  {
    if (!Current.IsIdentifier)
    {
      throw Error(Current.Location, $"expected an identifier {context}, found \"{Current.Text}\"");
    }

    string text = Current.Text;
    if (ReservedWords.Contains(text))
    {
      throw Error(Current.Location, $"\"{text}\" is a reserved word and cannot be used as an identifier");
    }

    Advance();
    return text;
  }

  private CParseException Error(CSourceLocation location, string message)
  {
    _diagnostics.Add(location.FormatDiagnostic("error", message));
    return new CParseException();
  }

  private void SynchronizeAfterError(bool atTopLevel)
  {
    int braceDepth = 0;
    while (!AtEnd)
    {
      if (CheckWord("{"))
      {
        braceDepth++;
        Advance();
        continue;
      }

      if (CheckWord("}"))
      {
        if (braceDepth == 0)
        {
          if (!atTopLevel)
          {
            Advance();
          }

          return;
        }

        braceDepth--;
        Advance();
        continue;
      }

      if (CheckWord(";") && braceDepth == 0)
      {
        Advance();
        return;
      }

      Advance();
    }
  }

  // ---- Types --------------------------------------------------------------------------------------

  private bool LooksLikeTypeStart() =>
      CheckWord("void") || CheckWord("char") || CheckWord("int") || CheckWord("unsigned") || CheckWord("signed") ||
      CheckWord("static") || CheckWord("extern") || CheckWord("const") || CheckWord("volatile") ||
      UnsupportedTypeKeywords.Any(CheckWord);

  /// <summary>Consumes storage-class/qualifier keywords (in any order/quantity) and exactly one base
  /// type, reporting a specific diagnostic for a recognized-but-unsupported type keyword rather than a
  /// generic syntax error.</summary>
  private CType ParseDeclarationSpecifiers(out bool isStatic, out bool isExtern)
  {
    isStatic = false;
    isExtern = false;
    while (true)
    {
      if (Match("static"))
      {
        isStatic = true;
      }
      else if (Match("extern"))
      {
        isExtern = true;
      }
      else if (Match("const") || Match("volatile"))
      {
        // Accepted and ignored: this compiler does not yet enforce const-correctness.
      }
      else
      {
        break;
      }
    }

    foreach (string keyword in UnsupportedTypeKeywords)
    {
      if (CheckWord(keyword))
      {
        throw Error(Current.Location, $"'{keyword}' is not yet supported by this compiler");
      }
    }

    if (Match("void"))
    {
      return CType.Void;
    }

    if (Match("signed"))
    {
      if (Match("char"))
      {
        return CType.Char;
      }

      Match("int");
      return CType.Int;
    }

    if (Match("unsigned"))
    {
      if (Match("char"))
      {
        return CType.UnsignedChar;
      }

      Match("int");
      return CType.UnsignedInt;
    }

    if (Match("char"))
    {
      return CType.Char;
    }

    if (Match("int"))
    {
      return CType.Int;
    }

    throw Error(Current.Location, $"expected a type, found \"{Current.Text}\"");
  }

  /// <summary>Parses "*... name (\"[\" length \"]\")?" given the already-parsed base type -- a plain
  /// (non-function, non-abstract) declarator. Rejects a second array dimension explicitly rather than
  /// silently misinterpreting one (see the design doc's "no multi-dimensional arrays" scope note).
  /// </summary>
  private CType ParseDeclaratorType(CType baseType, string nameContext, out string name, out CSourceLocation nameLocation)
  {
    CType type = baseType;
    while (Match("*"))
    {
      type = CType.PointerTo(type);
    }

    nameLocation = Current.Location;
    name = ExpectIdentifier(nameContext);

    if (Match("["))
    {
      int length = -1;
      if (!CheckWord("]"))
      {
        length = checked((int)ParseConstantIntExpression());
      }

      Expect("]", "to close the array declarator");

      if (CheckWord("["))
      {
        throw Error(Current.Location, "multi-dimensional arrays are not yet supported");
      }

      type = CType.ArrayOf(type, length);
    }

    return type;
  }

  /// <summary>An "abstract declarator" -- a type with no name, for "sizeof(T)" and casts: the base
  /// type plus any number of "*". No array/function shape is supported here (e.g. "sizeof(int[5])" or
  /// a function-pointer cast) -- a small, deliberate scope limit.</summary>
  private CType ParseAbstractType()
  {
    CType type = ParseDeclarationSpecifiers(out _, out _);
    while (Match("*"))
    {
      type = CType.PointerTo(type);
    }

    return type;
  }

  // ---- A tiny constant-expression evaluator, for array bounds, "case" labels, and scalar global
  // initializers. Preprocessing has already resolved every macro by the time the parser sees these
  // tokens, so in practice this almost always sees a single literal; the small operator set below
  // exists mainly for the occasional "N + 1" or "1 << 4" a person writes directly. ------------------

  private long ParseConstantIntExpression() => ParseConstantBitwiseOr();

  private long ParseConstantBitwiseOr() => ParseConstantChain(ParseConstantBitwiseXor, ("|", (a, b) => a | b));

  private long ParseConstantBitwiseXor() => ParseConstantChain(ParseConstantBitwiseAnd, ("^", (a, b) => a ^ b));

  private long ParseConstantBitwiseAnd() => ParseConstantChain(ParseConstantShift, ("&", (a, b) => a & b));

  private long ParseConstantShift() => ParseConstantChain(ParseConstantAdditive, ("<<", (a, b) => a << (int)b), (">>", (a, b) => a >> (int)b));

  private long ParseConstantAdditive() => ParseConstantChain(ParseConstantMultiplicative, ("+", (a, b) => a + b), ("-", (a, b) => a - b));

  private long ParseConstantMultiplicative() =>
      ParseConstantChain(ParseConstantUnary, ("*", (a, b) => a * b), ("/", (a, b) => b == 0 ? throw Error(Current.Location, "division by zero in constant expression") : a / b),
          ("%", (a, b) => b == 0 ? throw Error(Current.Location, "division by zero in constant expression") : a % b));

  private long ParseConstantChain(Func<long> operand, params (string Op, Func<long, long, long> Combine)[] operators)
  {
    long left = operand();
    while (true)
    {
      bool matched = false;
      foreach ((string op, Func<long, long, long> combine) in operators)
      {
        if (CheckWord(op))
        {
          Advance();
          left = combine(left, operand());
          matched = true;
          break;
        }
      }

      if (!matched)
      {
        return left;
      }
    }
  }

  private long ParseConstantUnary()
  {
    if (Match("-"))
    {
      return -ParseConstantUnary();
    }

    if (Match("+"))
    {
      return ParseConstantUnary();
    }

    if (Match("~"))
    {
      return ~ParseConstantUnary();
    }

    if (Match("!"))
    {
      return ParseConstantUnary() == 0 ? 1 : 0;
    }

    if (CheckWord("sizeof"))
    {
      Advance();
      Expect("(", "after 'sizeof'");
      CType type = ParseAbstractType();
      Expect(")", "to close 'sizeof(...)'");
      return type.SizeInWords;
    }

    if (Match("("))
    {
      long value = ParseConstantIntExpression();
      Expect(")", "to close '(...)'");
      return value;
    }

    if (Current.Kind == CTokenKind.Number)
    {
      return ParseNumberLiteralValue(Advance());
    }

    if (Current.Kind == CTokenKind.CharLiteral)
    {
      return ParseCharLiteralValue(Advance().Text);
    }

    throw Error(Current.Location, $"expected a constant expression, found \"{Current.Text}\"");
  }

  // ---- External (top-level) declarations -----------------------------------------------------------

  private void ParseExternalDeclaration(List<CFunctionDecl> functions, List<CGlobalVarDecl> globals)
  {
    CSourceLocation location = Current.Location;
    CType baseType = ParseDeclarationSpecifiers(out bool isStatic, out bool isExtern);

    // The base type carries no pointer stars of its own -- in "int *a, b;", only "a" is a pointer, not
    // "b", so each declarator (the first one included) consumes its own stars starting fresh from
    // baseType, never from a previous declarator's own type.
    CType type = baseType;
    while (Match("*"))
    {
      type = CType.PointerTo(type);
    }

    string name = ExpectIdentifier("in a top-level declaration");

    if (Match("("))
    {
      List<CParameter> parameters = ParseParameterList();
      Expect(")", "to close the parameter list");

      CCompoundStmt? body = null;
      if (!Match(";"))
      {
        body = ParseCompoundStatement();
      }

      functions.Add(new CFunctionDecl(location, name, type, parameters, body, isStatic));
      return;
    }

    // One or more comma-separated global variable declarators sharing the same base type/specifiers.
    while (true)
    {
      if (Match("["))
      {
        int length = -1;
        if (!CheckWord("]"))
        {
          length = checked((int)ParseConstantIntExpression());
        }

        Expect("]", "to close the array declarator");
        type = CType.ArrayOf(type, length);
      }

      CExpr? initializer = null;
      if (Match("="))
      {
        initializer = ParseAssignmentExpression();
      }

      globals.Add(new CGlobalVarDecl(location, name, type, initializer, isStatic, isExtern));

      if (!Match(","))
      {
        break;
      }

      type = baseType;
      while (Match("*"))
      {
        type = CType.PointerTo(type);
      }

      location = Current.Location;
      name = ExpectIdentifier("in a global variable declaration");
    }

    Expect(";", "after a global variable declaration");
  }

  private List<CParameter> ParseParameterList()
  {
    var parameters = new List<CParameter>();
    if (CheckWord("void") && _tokens[_pos + 1].IsPunctuator(")"))
    {
      Advance();
      return parameters;
    }

    if (CheckWord(")"))
    {
      return parameters;
    }

    do
    {
      CType baseType = ParseDeclarationSpecifiers(out _, out _);
      CType type = ParseDeclaratorType(baseType, "in a parameter declaration", out string name, out _);
      parameters.Add(new CParameter(name, type.Decay()));
    } while (Match(","));

    return parameters;
  }

  // ---- Statements -------------------------------------------------------------------------------

  private CCompoundStmt ParseCompoundStatement()
  {
    CSourceLocation location = Current.Location;
    Expect("{", "to start a block");
    var statements = new List<CStmt>();
    while (!CheckWord("}") && !AtEnd)
    {
      try
      {
        ParseBlockItem(statements);
      }
      catch (CParseException)
      {
        SynchronizeAfterError(atTopLevel: false);
      }
    }

    Expect("}", "to close a block");
    return new CCompoundStmt(location, statements);
  }

  private void ParseBlockItem(List<CStmt> statements)
  {
    if (LooksLikeTypeStart())
    {
      ParseLocalDeclaration(statements);
      return;
    }

    statements.Add(ParseStatement());
  }

  private void ParseLocalDeclaration(List<CStmt> statements)
  {
    CType baseType = ParseDeclarationSpecifiers(out bool isStatic, out _);

    do
    {
      CType type = ParseDeclaratorType(baseType, "in a local declaration", out string name, out CSourceLocation nameLocation);
      CExpr? initializer = null;
      if (Match("="))
      {
        initializer = ParseAssignmentExpression();
      }

      statements.Add(new CLocalVarDecl(nameLocation, name, type, initializer, isStatic));
    } while (Match(","));

    Expect(";", "after a local declaration");
  }

  private CStmt ParseStatement()
  {
    CSourceLocation location = Current.Location;

    if (CheckWord("{"))
    {
      return ParseCompoundStatement();
    }

    if (Match(";"))
    {
      return new CEmptyStmt(location);
    }

    if (Match("if"))
    {
      Expect("(", "after 'if'");
      CExpr condition = ParseExpression();
      Expect(")", "after the 'if' condition");
      CStmt then = ParseStatement();
      CStmt? elseStmt = Match("else") ? ParseStatement() : null;
      return new CIfStmt(location, condition, then, elseStmt);
    }

    if (Match("while"))
    {
      Expect("(", "after 'while'");
      CExpr condition = ParseExpression();
      Expect(")", "after the 'while' condition");
      return new CWhileStmt(location, condition, ParseStatement());
    }

    if (Match("do"))
    {
      CStmt body = ParseStatement();
      Expect("while", "to close a 'do' loop");
      Expect("(", "after 'while'");
      CExpr condition = ParseExpression();
      Expect(")", "after the 'do/while' condition");
      Expect(";", "after a 'do/while' loop");
      return new CDoWhileStmt(location, body, condition);
    }

    if (Match("for"))
    {
      Expect("(", "after 'for'");
      CStmt? init = null;
      if (!CheckWord(";"))
      {
        if (LooksLikeTypeStart())
        {
          var declStatements = new List<CStmt>();
          ParseLocalDeclaration(declStatements);
          init = declStatements.Count == 1 ? declStatements[0] : new CCompoundStmt(location, declStatements);
        }
        else
        {
          init = new CExprStmt(Current.Location, ParseExpression());
          Expect(";", "after a 'for' loop's initializer");
        }
      }
      else
      {
        Advance();
      }

      CExpr? condition = CheckWord(";") ? null : ParseExpression();
      Expect(";", "after a 'for' loop's condition");
      CExpr? update = CheckWord(")") ? null : ParseExpression();
      Expect(")", "after a 'for' loop's update");
      return new CForStmt(location, init, condition, update, ParseStatement());
    }

    if (Match("return"))
    {
      CExpr? value = CheckWord(";") ? null : ParseExpression();
      Expect(";", "after 'return'");
      return new CReturnStmt(location, value);
    }

    if (Match("break"))
    {
      Expect(";", "after 'break'");
      return new CBreakStmt(location);
    }

    if (Match("continue"))
    {
      Expect(";", "after 'continue'");
      return new CContinueStmt(location);
    }

    if (Match("goto"))
    {
      string label = ExpectIdentifier("after 'goto'");
      Expect(";", "after 'goto <label>'");
      return new CGotoStmt(location, label);
    }

    if (Match("switch"))
    {
      Expect("(", "after 'switch'");
      CExpr selector = ParseExpression();
      Expect(")", "after the 'switch' selector");
      return new CSwitchStmt(location, selector, ParseStatement());
    }

    if (Match("case"))
    {
      CExpr value = ParseConditionalExpression();
      Expect(":", "after a 'case' value");
      return new CCaseLabelStmt(location, value, ParseStatement());
    }

    if (Match("default"))
    {
      Expect(":", "after 'default'");
      return new CDefaultLabelStmt(location, ParseStatement());
    }

    // A plain "identifier:" label -- distinguished from an expression-statement by lookahead, since a
    // bare identifier followed by ':' is never itself a valid expression-statement in this grammar
    // (there is no ternary-less "?:"-free way to end an expression on a lone ':').
    if (Current.IsIdentifier && !ReservedWords.Contains(Current.Text) && _tokens[_pos + 1].IsPunctuator(":"))
    {
      string label = Advance().Text;
      Advance();
      return new CLabelStmt(location, label, ParseStatement());
    }

    CExpr expression = ParseExpression();
    Expect(";", "after an expression statement");
    return new CExprStmt(location, expression);
  }

  // ---- Expressions (precedence climbing, weakest-binding first) -------------------------------------

  private CExpr ParseExpression()
  {
    CExpr left = ParseAssignmentExpression();
    while (Match(","))
    {
      left = new CCommaExpr(left.Location, left, ParseAssignmentExpression());
    }

    return left;
  }

  private static readonly Dictionary<string, CBinaryOp> CompoundAssignOperators = new()
  {
    ["+="] = CBinaryOp.Add,
    ["-="] = CBinaryOp.Subtract,
    ["*="] = CBinaryOp.Multiply,
    ["/="] = CBinaryOp.Divide,
    ["%="] = CBinaryOp.Modulo,
    ["&="] = CBinaryOp.BitwiseAnd,
    ["|="] = CBinaryOp.BitwiseOr,
    ["^="] = CBinaryOp.BitwiseXor,
    ["<<="] = CBinaryOp.ShiftLeft,
    [">>="] = CBinaryOp.ShiftRight,
  };

  private CExpr ParseAssignmentExpression()
  {
    CExpr left = ParseConditionalExpression();

    if (CheckWord("="))
    {
      CSourceLocation location = Current.Location;
      Advance();
      return new CAssignExpr(location, left, ParseAssignmentExpression());
    }

    foreach ((string op, CBinaryOp binaryOp) in CompoundAssignOperators)
    {
      if (CheckWord(op))
      {
        CSourceLocation location = Current.Location;
        Advance();
        return new CCompoundAssignExpr(location, binaryOp, left, ParseAssignmentExpression());
      }
    }

    return left;
  }

  private CExpr ParseConditionalExpression()
  {
    CExpr condition = ParseLogicalOrExpression();
    if (Match("?"))
    {
      CExpr whenTrue = ParseExpression();
      Expect(":", "in a '?:' expression");
      CExpr whenFalse = ParseConditionalExpression();
      return new CConditionalExpr(condition.Location, condition, whenTrue, whenFalse);
    }

    return condition;
  }

  private CExpr ParseLogicalOrExpression() => ParseLeftAssociativeBinary(ParseLogicalAndExpression, ("||", CBinaryOp.LogicalOr));

  private CExpr ParseLogicalAndExpression() => ParseLeftAssociativeBinary(ParseBitwiseOrExpression, ("&&", CBinaryOp.LogicalAnd));

  private CExpr ParseBitwiseOrExpression() => ParseLeftAssociativeBinary(ParseBitwiseXorExpression, ("|", CBinaryOp.BitwiseOr));

  private CExpr ParseBitwiseXorExpression() => ParseLeftAssociativeBinary(ParseBitwiseAndExpression, ("^", CBinaryOp.BitwiseXor));

  private CExpr ParseBitwiseAndExpression() => ParseLeftAssociativeBinary(ParseEqualityExpression, ("&", CBinaryOp.BitwiseAnd));

  private CExpr ParseEqualityExpression() => ParseLeftAssociativeBinary(ParseRelationalExpression, ("==", CBinaryOp.Equal), ("!=", CBinaryOp.NotEqual));

  private CExpr ParseRelationalExpression() => ParseLeftAssociativeBinary(ParseShiftExpression,
      ("<=", CBinaryOp.LessOrEqual), (">=", CBinaryOp.GreaterOrEqual), ("<", CBinaryOp.Less), (">", CBinaryOp.Greater));

  private CExpr ParseShiftExpression() => ParseLeftAssociativeBinary(ParseAdditiveExpression, ("<<", CBinaryOp.ShiftLeft), (">>", CBinaryOp.ShiftRight));

  private CExpr ParseAdditiveExpression() => ParseLeftAssociativeBinary(ParseMultiplicativeExpression, ("+", CBinaryOp.Add), ("-", CBinaryOp.Subtract));

  private CExpr ParseMultiplicativeExpression() => ParseLeftAssociativeBinary(ParseCastExpression, ("*", CBinaryOp.Multiply), ("/", CBinaryOp.Divide), ("%", CBinaryOp.Modulo));

  /// <summary>Shared machinery for every left-associative binary precedence level: parse one operand,
  /// then greedily consume "(op operand)*", checking longer operator spellings first so e.g. "&&" is
  /// never mistaken for "&" followed by a stray "&".</summary>
  private CExpr ParseLeftAssociativeBinary(Func<CExpr> operand, params (string Op, CBinaryOp Kind)[] operators)
  {
    CExpr left = operand();
    while (true)
    {
      bool matched = false;
      foreach ((string op, CBinaryOp kind) in operators)
      {
        if (CheckWord(op))
        {
          CSourceLocation location = Current.Location;
          Advance();
          left = new CBinaryExpr(location, kind, left, operand());
          matched = true;
          break;
        }
      }

      if (!matched)
      {
        return left;
      }
    }
  }

  private CExpr ParseCastExpression()
  {
    if (CheckWord("(") && LooksLikeTypeAt(_pos + 1))
    {
      CSourceLocation location = Current.Location;
      Advance();
      CType targetType = ParseAbstractType();
      Expect(")", "to close a cast");
      return new CCastExpr(location, targetType, ParseCastExpression());
    }

    return ParseUnaryExpression();
  }

  private bool LooksLikeTypeAt(int index)
  {
    CToken token = _tokens[index];
    return token.IsIdentifier && (token.Text is "void" or "char" or "int" or "unsigned" or "signed" or "const" or "volatile" || UnsupportedTypeKeywords.Contains(token.Text));
  }

  private CExpr ParseUnaryExpression()
  {
    CSourceLocation location = Current.Location;

    if (Match("++"))
    {
      return new CUnaryExpr(location, CUnaryOp.PreIncrement, ParseUnaryExpression());
    }

    if (Match("--"))
    {
      return new CUnaryExpr(location, CUnaryOp.PreDecrement, ParseUnaryExpression());
    }

    if (Match("+"))
    {
      return new CUnaryExpr(location, CUnaryOp.Plus, ParseCastExpression());
    }

    if (Match("-"))
    {
      return new CUnaryExpr(location, CUnaryOp.Minus, ParseCastExpression());
    }

    if (Match("!"))
    {
      return new CUnaryExpr(location, CUnaryOp.LogicalNot, ParseCastExpression());
    }

    if (Match("~"))
    {
      return new CUnaryExpr(location, CUnaryOp.BitwiseNot, ParseCastExpression());
    }

    if (Match("&"))
    {
      return new CUnaryExpr(location, CUnaryOp.AddressOf, ParseCastExpression());
    }

    if (Match("*"))
    {
      return new CUnaryExpr(location, CUnaryOp.Dereference, ParseCastExpression());
    }

    if (Match("sizeof"))
    {
      if (CheckWord("(") && LooksLikeTypeAt(_pos + 1))
      {
        Advance();
        CType type = ParseAbstractType();
        Expect(")", "to close 'sizeof(...)'");
        return new CSizeOfTypeExpr(location, type);
      }

      return new CSizeOfExprExpr(location, ParseUnaryExpression());
    }

    return ParsePostfixExpression();
  }

  private CExpr ParsePostfixExpression()
  {
    CExpr expr = ParsePrimaryExpression();

    while (true)
    {
      CSourceLocation location = Current.Location;

      if (Match("["))
      {
        CExpr index = ParseExpression();
        Expect("]", "to close an array index");
        expr = new CIndexExpr(location, expr, index);
        continue;
      }

      if (CheckWord("("))
      {
        if (expr is not CNameExpr callee)
        {
          throw Error(location, "only a plain function name can be called (function pointers are not yet supported)");
        }

        Advance();
        var arguments = new List<CExpr>();
        if (!CheckWord(")"))
        {
          do
          {
            arguments.Add(ParseAssignmentExpression());
          } while (Match(","));
        }

        Expect(")", "to close a function call's argument list");
        expr = new CCallExpr(location, callee.Name, arguments);
        continue;
      }

      if (Match("++"))
      {
        expr = new CUnaryExpr(location, CUnaryOp.PostIncrement, expr);
        continue;
      }

      if (Match("--"))
      {
        expr = new CUnaryExpr(location, CUnaryOp.PostDecrement, expr);
        continue;
      }

      if (CheckWord(".") || CheckWord("->"))
      {
        throw Error(location, "struct/union member access is not yet supported");
      }

      return expr;
    }
  }

  private CExpr ParsePrimaryExpression()
  {
    CToken token = Current;

    if (token.Kind == CTokenKind.Number)
    {
      Advance();
      return new CIntLiteralExpr(token.Location, ParseNumberLiteralValue(token), IsUnsignedSuffix(token.Text));
    }

    if (token.Kind == CTokenKind.CharLiteral)
    {
      Advance();
      return new CIntLiteralExpr(token.Location, ParseCharLiteralValue(token.Text), IsUnsigned: false);
    }

    if (token.Kind == CTokenKind.StringLiteral)
    {
      Advance();
      return new CStringLiteralExpr(token.Location, DecodeStringLiteral(token.Text));
    }

    if (Match("("))
    {
      CExpr inner = ParseExpression();
      Expect(")", "to close a parenthesized expression");
      return inner;
    }

    if (token.IsIdentifier && !ReservedWords.Contains(token.Text))
    {
      Advance();
      return new CNameExpr(token.Location, token.Text);
    }

    throw Error(token.Location, $"expected an expression, found \"{token.Text}\"");
  }

  // ---- Literal decoding ---------------------------------------------------------------------------

  private static bool IsUnsignedSuffix(string numberText) => numberText.Any(c => c is 'u' or 'U');

  private static long ParseNumberLiteralValue(CToken token)
  {
    string text = token.Text;
    int end = text.Length;
    while (end > 0 && "uUlL".IndexOf(text[end - 1]) >= 0)
    {
      end--;
    }

    string digits = text[..end];
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

    return (long)Convert.ToUInt64(digits, 10);
  }

  private static long ParseCharLiteralValue(string text)
  {
    int start = text.IndexOf('\'') + 1;
    int end = text.LastIndexOf('\'');
    string body = start > 0 && end > start ? text[start..end] : string.Empty;
    string decoded = DecodeEscapes(body);
    return decoded.Length > 0 ? decoded[0] : 0;
  }

  private static string DecodeStringLiteral(string text)
  {
    int start = text.IndexOf('"') + 1;
    int end = text.LastIndexOf('"');
    string body = start > 0 && end > start ? text[start..end] : string.Empty;
    return DecodeEscapes(body);
  }

  private static string DecodeEscapes(string body)
  {
    var result = new System.Text.StringBuilder(body.Length);
    int i = 0;
    while (i < body.Length)
    {
      if (body[i] == '\\' && i + 1 < body.Length)
      {
        char c = body[i + 1];
        switch (c)
        {
          case 'n': result.Append('\n'); i += 2; break;
          case 't': result.Append('\t'); i += 2; break;
          case 'r': result.Append('\r'); i += 2; break;
          case '0': result.Append('\0'); i += 2; break;
          case 'a': result.Append('\a'); i += 2; break;
          case 'b': result.Append('\b'); i += 2; break;
          case 'f': result.Append('\f'); i += 2; break;
          case 'v': result.Append('\v'); i += 2; break;
          case '\\': result.Append('\\'); i += 2; break;
          case '\'': result.Append('\''); i += 2; break;
          case '"': result.Append('"'); i += 2; break;
          case 'x':
          {
            int j = i + 2;
            int value = 0;
            while (j < body.Length && Uri.IsHexDigit(body[j]))
            {
              value = (value * 16) + Convert.ToInt32(body[j].ToString(), 16);
              j++;
            }

            result.Append((char)value);
            i = j;
            break;
          }

          default:
            result.Append(c);
            i += 2;
            break;
        }
      }
      else
      {
        result.Append(body[i]);
        i++;
      }
    }

    return result.ToString();
  }
}
