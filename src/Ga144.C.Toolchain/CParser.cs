namespace Ga144.C.Toolchain;

/// <summary>
/// A recursive-descent parser for the C subset this compiler supports (see the design doc for the
/// full scope list). Input is an already-preprocessed token stream (macros expanded, directives gone)
/// -- NewLine tokens carry no meaning in C's grammar and are stripped before parsing starts.
///
/// Not supported, with a clear diagnostic rather than silent misbehavior: struct/union/enum,
/// float/double/long/short, multi-dimensional arrays, function pointers, bit-fields, variadic
/// functions, and the "." / "->" member-access operators (all of which need structs). "goto"/labels,
/// "switch"/"case"/"default", and every operator standard C defines over this compiler's supported
/// types ARE supported.
///
/// <b><c>typedef</c> -- added 2026-09-11, per Stefan's own build failure trying to compile a `libc`
/// `heap.c` against `stddef.h`/`stdlib.h` (both need `size_t`).</b> Supported for a scalar/pointer/array
/// alias over this compiler's own existing type system ONLY -- `typedef unsigned int size_t;`,
/// `typedef int *IntPtr;`, `typedef char Buf[16];`, and one typedef re-using another
/// (`typedef size_t my_size_t;`) all work, and the aliased name can be used anywhere a built-in type
/// keyword could (a declaration's base type, a parameter, a cast, `sizeof(...)`). NOT supported, for the
/// same reason function pointers and structs generally aren't: a typedef naming a function type
/// (`typedef int Fn(int);`) or a function-pointer type (`typedef int (*Fn)(int);`) -- both are rejected
/// with a clear diagnostic by <see cref="ParseTypedefDeclaratorList"/> rather than silently misparsed. A
/// typedef introduces no AST node and needs nothing from <see cref="CCodeGenerator"/> -- it is purely a
/// compile-time name for an already-existing <see cref="CType"/>, resolved once and substituted
/// immediately; see <see cref="_typedefs"/>'s own remarks for how that name table works and its one
/// deliberate scope simplification.
/// </summary>
public sealed class CParser
{
  private static readonly HashSet<string> ReservedWords =
  [
    "void", "char", "int", "unsigned", "signed", "static", "extern", "const", "volatile",
    "if", "else", "while", "do", "for", "return", "break", "continue", "goto", "switch", "case", "default",
    "sizeof", "struct", "union", "enum", "typedef", "float", "double", "long", "short", "auto", "register",
    "__fastcall", "__lower",
  ];

  // "typedef" REMOVED 2026-09-11 -- it used to sit here alongside struct/union/enum/float/double/long/
  // short as a recognized-but-rejected keyword (ParseDeclarationSpecifiers's own "not yet supported"
  // check below iterates this set); it is still a reserved word (ReservedWords above, unchanged) but is
  // now a real, supported specifier -- see this class's own remarks above and ParseDeclarationSpecifiers'
  // own isTypedef handling.
  private static readonly HashSet<string> UnsupportedTypeKeywords = ["struct", "union", "enum", "float", "double", "long", "short"];

  /// <summary>
  /// Every typedef name seen so far in this translation unit, mapped to the already-resolved
  /// <see cref="CType"/> it aliases. Consulted by <see cref="LooksLikeTypeStart"/>/
  /// <see cref="LooksLikeTypeAt"/> (so e.g. "size_t x;" is recognized as a declaration, and
  /// "(size_t)x"/"sizeof(size_t)" is recognized as a type name rather than an expression) and by
  /// <see cref="ParseDeclarationSpecifiers"/> itself (so "size_t" can be used anywhere a built-in type
  /// keyword could be). A typedef's own alias is resolved to a concrete <see cref="CType"/> the moment
  /// it is declared (there is no separate "named alias" <see cref="CTypeKind"/> -- "size_t" and
  /// "unsigned int" become the exact same <see cref="CType"/> instance/value), so every later lookup is
  /// a plain dictionary hit with no chained resolution needed.
  ///
  /// <b>One flat, unscoped table for the whole translation unit, not a stack of per-block scopes.</b> A
  /// typedef declared inside a function body (<see cref="ParseLocalDeclaration"/>'s own isTypedef
  /// branch) is never removed once that function's own block ends -- it remains visible for the rest of
  /// the file too, which is technically looser than real C's block scoping but harmless for every
  /// program this compiler is actually meant to compile (typedefs overwhelmingly come from headers
  /// included at file scope, used by ordinary code below them). This mirrors <see cref="CCompiler"/>'s
  /// own one-CParser-per-translation-unit model exactly: a fresh CParser (and so a fresh, empty table)
  /// is created per file compiled, matching real C's own per-translation-unit typedef scope -- only
  /// BLOCK scoping within a single file is simplified away.
  /// </summary>
  private readonly Dictionary<string, CType> _typedefs = new();

  /// <summary>A `__fastcall` function's pointer parameters are passed in node 306's four address
  /// registers, `ar[0]` through `ar[3]` -- see `claude/cvm-abi.md` section 2.2. A 5th pointer parameter
  /// has nowhere to go under this convention (pointers never fall back to the stack, or to node 511's
  /// register file, for a `__fastcall` function), so it is a compiler error -- Stefan, verbatim: "a 5th
  /// __fastcall pointer parameter also rises a compiler error."</summary>
  private const int MaxFastcallPointerParameters = 4;

  /// <summary>A `__fastcall` function's non-pointer parameters are passed in node 511's own register
  /// file, one register per 16-bit word, beginning at `reg[0]` -- see `claude/cvm-abi.md` section 2.2
  /// and `claude/cvm-node510-511-register-file.md`. That register file holds exactly 32 registers, so a
  /// `__fastcall` function whose non-pointer parameters need more than 32 words of register space cannot
  /// be represented -- Stefan, verbatim: "if a __fastcall function uses more than 32 register, the
  /// compiler should generate an error." Measured in WORDS, not parameter COUNT, via
  /// <see cref="CType.SizeInWords"/> -- today every non-pointer type this compiler supports (int,
  /// unsigned int, char, unsigned char) is exactly 1 word, so this is currently equivalent to counting
  /// non-pointer parameters, but the word-based accounting matches the ABI doc's own "32-bit values...
  /// occupies two consecutive registers" rule and stays correct if a wider non-pointer type is ever
  /// added.</summary>
  private const int MaxFastcallRegisterWords = 32;

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
      CheckWord("__fastcall") || CheckWord("__lower") || CheckWord("typedef") ||
      (Current.IsIdentifier && _typedefs.ContainsKey(Current.Text)) ||
      UnsupportedTypeKeywords.Any(CheckWord);

  /// <summary>Consumes storage-class/qualifier keywords (in any order/quantity) and exactly one base
  /// type, reporting a specific diagnostic for a recognized-but-unsupported type keyword rather than a
  /// generic syntax error.
  ///
  /// <b><c>__fastcall</c>/<c>__lower</c> -- added 2026-09-07, per <c>claude/cvm-abi.md</c>.</b> Both are
  /// recognized here, in the same order-independent specifier bag as <c>static</c>/<c>extern</c>/
  /// <c>const</c>/<c>volatile</c>, rather than in the (more MSVC-literal) position between the return
  /// type and the function name -- a deliberate placement choice, flagged here since Stefan did not
  /// dictate a specific syntax: this keeps both keywords usable in any order alongside the existing
  /// specifiers (e.g. <c>static __fastcall int add(...)</c> or <c>__fastcall static int add(...)</c>),
  /// consistent with how this method already treats every other specifier. Both are semantically
  /// meaningless outside a FUNCTION declaration (a parameter, a local variable, a global variable, or a
  /// bare type name for <c>sizeof</c>/a cast can never be <c>__fastcall</c> or <c>__lower</c>) -- every
  /// call site below that is not <see cref="ParseExternalDeclaration"/>'s own function-declarator branch
  /// rejects a true <paramref name="isFastcall"/>/<paramref name="isLower"/> immediately via
  /// <see cref="RejectCallingConventionKeywords"/>, with a specific diagnostic, rather than silently
  /// dropping them or falling through to a confusing generic parse error.</summary>
  private CType ParseDeclarationSpecifiers(out bool isStatic, out bool isExtern, out bool isFastcall, out bool isLower, out bool isTypedef)
  {
    isStatic = false;
    isExtern = false;
    isFastcall = false;
    isLower = false;
    isTypedef = false;
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
      else if (Match("__fastcall"))
      {
        isFastcall = true;
      }
      else if (Match("__lower"))
      {
        isLower = true;
      }
      else if (Match("typedef"))
      {
        // Order-independent, same as every other specifier here -- "typedef unsigned int size_t;" and
        // "unsigned typedef int size_t;" both parse (real C allows this too; typedef is just another
        // storage-class specifier grammatically). See this class's own remarks on typedef support.
        isTypedef = true;
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

    // A previously-declared typedef name, used here as this declaration's own base type -- see
    // _typedefs' own remarks. Checked last, after every built-in keyword, so a typedef can never shadow
    // one of them (not that a real program would try: ExpectIdentifier already refuses to declare a
    // typedef named "int"/"void"/etc., since those are reserved words).
    if (Current.IsIdentifier && _typedefs.TryGetValue(Current.Text, out CType? aliasedType))
    {
      Advance();
      return aliasedType;
    }

    throw Error(Current.Location, $"expected a type, found \"{Current.Text}\"");
  }

  /// <summary>Rejects <c>__fastcall</c>/<c>__lower</c> wherever <see cref="ParseDeclarationSpecifiers"/>
  /// was called for something other than a function declaration (a parameter, a local/global variable,
  /// or a bare type name for <c>sizeof</c>/a cast) -- both keywords describe a function's own calling
  /// convention and code placement, and are meaningless anywhere else.</summary>
  private void RejectCallingConventionKeywords(bool isFastcall, bool isLower, CSourceLocation location, string context)
  {
    if (isFastcall)
    {
      throw Error(location, $"'__fastcall' can only be used on a function declaration, not {context}");
    }

    if (isLower)
    {
      throw Error(location, $"'__lower' can only be used on a function declaration, not {context}");
    }
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
    CSourceLocation location = Current.Location;
    CType type = ParseDeclarationSpecifiers(out _, out _, out bool isFastcall, out bool isLower, out bool isTypedef);
    RejectCallingConventionKeywords(isFastcall, isLower, location, "a type name");
    if (isTypedef)
    {
      throw Error(location, "'typedef' cannot be used in a type name");
    }

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
    CType baseType = ParseDeclarationSpecifiers(out bool isStatic, out bool isExtern, out bool isFastcall, out bool isLowerSpecified, out bool isTypedef);

    if (isTypedef)
    {
      RejectCallingConventionKeywords(isFastcall, isLowerSpecified, location, "a typedef declaration");
      if (isStatic || isExtern)
      {
        throw Error(location, "'typedef' cannot be combined with 'static' or 'extern'");
      }

      ParseTypedefDeclaratorList(baseType);
      return;
    }

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

      // "__fastcall implies also __lower, so __fastcall includes __lower" (Stefan, 2026-09-07) -- a
      // __fastcall function is ALWAYS required to be placed in the lower half of memory too, whether or
      // not "__lower" was itself also written on the declaration. See CFunctionDecl's own remarks: a
      // caller only ever needs to check IsLower to decide the call/lcall encoding, never IsFastcall.
      bool isLower = isLowerSpecified || isFastcall;

      // "pointers must be allocated in ar[0..3] for fastcall functions" plus "a 5th __fastcall pointer
      // parameter also rises a compiler error" (Stefan, 2026-09-07, 3rd/4th rounds of claude/cvm-abi.md's
      // dictation): a __fastcall function may declare at most MaxFastcallPointerParameters pointer
      // parameters (node 306 has exactly that many address registers, ar[0..3]) -- a 5th has nowhere
      // left to go under this convention (pointers are never passed via node 511's register file, and
      // never fall back to the stack for a __fastcall function per that same dictation).
      //
      // "if a __fastcall function uses more than 32 register, the compiler should generate an error"
      // (same 3rd round): every NON-pointer parameter is instead passed in node 511's own register file,
      // one register per CType.SizeInWords word, and that file holds only MaxFastcallRegisterWords
      // registers total -- exceeding it is likewise a compiler error, not a silent fallback to the stack.
      // Both checks only need the already-parsed parameter list (no code generation involved), so both
      // are enforced here rather than deferred.
      if (isFastcall)
      {
        int pointerParameterCount = parameters.Count(p => p.Type.IsPointer);
        if (pointerParameterCount > MaxFastcallPointerParameters)
        {
          throw Error(location, $"\"{name}\" is '__fastcall' but declares {pointerParameterCount} pointer parameters -- a '__fastcall' function may have at most {MaxFastcallPointerParameters} (node 306's ar[0..3])");
        }

        int registerWordCount = parameters.Where(p => !p.Type.IsPointer).Sum(p => p.Type.SizeInWords);
        if (registerWordCount > MaxFastcallRegisterWords)
        {
          throw Error(location, $"\"{name}\" is '__fastcall' but its non-pointer parameters need {registerWordCount} node-511 registers -- a '__fastcall' function may use at most {MaxFastcallRegisterWords} (reg[0..{MaxFastcallRegisterWords - 1}])");
        }
      }

      CCompoundStmt? body = null;
      if (!Match(";"))
      {
        body = ParseCompoundStatement();
      }

      functions.Add(new CFunctionDecl(location, name, type, parameters, body, isStatic, isFastcall, isLower));
      return;
    }

    RejectCallingConventionKeywords(isFastcall, isLowerSpecified, location, "a variable declaration");

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
      CSourceLocation specifierLocation = Current.Location;
      CType baseType = ParseDeclarationSpecifiers(out _, out _, out bool isFastcall, out bool isLower, out bool isTypedef);
      RejectCallingConventionKeywords(isFastcall, isLower, specifierLocation, "a parameter declaration");
      if (isTypedef)
      {
        throw Error(specifierLocation, "'typedef' cannot be used in a parameter declaration");
      }

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
    CSourceLocation specifierLocation = Current.Location;
    CType baseType = ParseDeclarationSpecifiers(out bool isStatic, out _, out bool isFastcall, out bool isLower, out bool isTypedef);
    RejectCallingConventionKeywords(isFastcall, isLower, specifierLocation, "a local declaration");

    if (isTypedef)
    {
      if (isStatic)
      {
        throw Error(specifierLocation, "'typedef' cannot be combined with 'static'");
      }

      // See _typedefs' own remarks: a local typedef is recorded in the same flat, unscoped table a
      // file-scope one is, so it (harmlessly, for every program this compiler is actually meant to
      // compile) remains visible for the rest of the file, not just the rest of this block.
      ParseTypedefDeclaratorList(baseType);
      return;
    }

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

  /// <summary>Parses one or more comma-separated typedef declarators sharing the same base type
  /// ("typedef int A, *B, C[4];") and records each name in <see cref="_typedefs"/> rather than emitting
  /// any AST node -- a typedef is purely a compile-time name for an already-existing <see cref="CType"/>,
  /// with nothing for <see cref="CCodeGenerator"/> to generate. Shared by both call sites that can start
  /// a typedef (<see cref="ParseExternalDeclaration"/> at file scope, <see cref="ParseLocalDeclaration"/>
  /// inside a block -- see that field's own remarks on why both write into the same flat table).
  ///
  /// A name already in <see cref="_typedefs"/> is accepted again, unchanged, only if it names an EQUAL
  /// type -- re-including the same header twice (this compiler enforces no "#pragma once"/include-guard
  /// behavior of its own; it trusts the source's own guards, exactly like a real C preprocessor would if
  /// the guards were missing) must not be a hard error, but redefining a name to a genuinely different
  /// type is a real bug in the source and is rejected with a specific diagnostic.</summary>
  private void ParseTypedefDeclaratorList(CType baseType)
  {
    do
    {
      CType type = ParseDeclaratorType(baseType, "in a typedef declaration", out string name, out CSourceLocation nameLocation);

      if (CheckWord("("))
      {
        throw Error(Current.Location, "typedef of a function type is not yet supported");
      }

      if (CheckWord("="))
      {
        throw Error(Current.Location, "a typedef declaration cannot have an initializer");
      }

      if (_typedefs.TryGetValue(name, out CType? existing) && !existing.Equals(type))
      {
        throw Error(nameLocation, $"\"{name}\" is already declared as a different type (\"{existing}\" vs \"{type}\")");
      }

      _typedefs[name] = type;
    } while (Match(","));

    Expect(";", "after a typedef declaration");
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
    return token.IsIdentifier && (token.Text is "void" or "char" or "int" or "unsigned" or "signed" or "const" or "volatile"
        || UnsupportedTypeKeywords.Contains(token.Text) || _typedefs.ContainsKey(token.Text));
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