namespace Ga144.C.Toolchain;

/// <summary>
/// A recursive-descent parser for the C subset this compiler supports (see the design doc for the
/// full scope list). Input is an already-preprocessed token stream (macros expanded, directives gone)
/// -- NewLine tokens carry no meaning in C's grammar and are stripped before parsing starts.
///
/// Not supported, with a clear diagnostic rather than silent misbehavior: union/enum/double,
/// multi-dimensional arrays, function pointers, bit-fields, and variadic functions. "goto"/labels,
/// "switch"/"case"/"default", and every operator standard C defines over this compiler's supported types
/// ARE supported.
///
/// <b><c>short</c> -- added 2026-09-26, per Stefan verbatim: "support 'short'. make 'short' equal to
/// 'int'."</b> A pure parser-level synonym: "short", "short int", "unsigned short", and "unsigned short
/// int" all resolve to the exact same <see cref="CType"/> instance <c>int</c>/<c>unsigned int</c> already
/// would without the word "short" -- there is no separate <see cref="CTypeKind"/> for it and
/// <see cref="CCodeGenerator"/> needs no changes at all, since by the time codegen ever sees the type,
/// it already IS plain <c>int</c>. (<c>signed long</c>/<c>unsigned long</c> are not specifically
/// recognized -- see <see cref="ParseDeclarationSpecifiers"/>'s own remarks on <c>long</c> below for why
/// that combination is simply not something this first pass handles.)
///
/// <b><c>long</c> -- added 2026-09-26, the same day, per Stefan verbatim: "support 'long'. a 'long' is
/// represented as a 2 word little endian unit. do not generate code for handling 'long' yet, because that
/// has to be defined first in the CVM."</b> See <see cref="CType.Long"/>'s own remarks for the full scope:
/// a <c>long</c> variable (local/global/static/struct-member) is fully declarable, correctly sized (2
/// words) and addressable, and <c>long *</c>/<c>long[]</c>/pointer arithmetic on either all work (pure
/// address math, already generic over element size). What is rejected: a <c>long</c> passed or returned
/// BY VALUE in a function signature (checked here, at the parser level, exactly the way <c>struct</c>'s
/// own by-value restriction already is -- see just below); every actual VALUE-level operation (a read, an
/// assignment, <c>++</c>/<c>--</c>, a cast, arithmetic, an initializer other than "none, so it's zero-
/// filled") is instead rejected in <see cref="CCodeGenerator"/>, since those need codegen context this
/// purely-syntactic class doesn't have.
///
/// <b><c>float</c> -- added 2026-09-26, per Stefan's own float-ABI dictation.</b> See
/// <see cref="CType.Float"/>'s own remarks and <see cref="CCodeGenerator"/>'s own doc comment for the
/// codegen model. A pointer/array of <c>float</c> and pointer arithmetic/indexing on either were
/// rejected outright in this first pass, then generalized the same day (see the next paragraph) once
/// pointer arithmetic/indexing gained element-size scaling; still NOT supported: `float` comparisons,
/// `++`/`--` on a plain `float` variable, and any implicit or explicit int/float conversion.
///
/// <b><c>struct</c> -- added 2026-09-26, per Stefan's own "add 'struct' to the C language" instruction
/// (prompted by a real `libc` `heap.c` build failure -- a free-list heap allocator needing a
/// self-referential struct).</b> See <see cref="CType.StructOf"/>'s own remarks for the full scope: a
/// tagged struct definition/reference, scalar/pointer/`float`/array/nested-struct-by-value members,
/// self-reference via a pointer member, local/parameter*/global/static struct variables (*only
/// "struct Tag *", never a struct itself, as a parameter or return type), and "."/"->" member access
/// including chained access and `&amp;`. NOT supported, rejected with a clear diagnostic rather than
/// silently mishandled: `union`/`enum` (still), a struct passed/returned BY VALUE in a function
/// signature, and reading or assigning an entire struct value in one expression (`s1 = s2;`).
///
/// <b>Multi-word pointer arithmetic/indexing -- added 2026-09-26, the same day, per Stefan's own
/// follow-up request to lift the struct/`float` pointer scope limits above.</b> An array of `float` or of
/// `struct`, a `float`/array/struct member of a `struct`, a pointer to `float` or to `struct`, and
/// pointer arithmetic/indexing/`++`/`--` on any of those (`p+1`, `p[i]`, `p++`) are now all supported --
/// see <see cref="PointerToChecked"/>/<see cref="ArrayOfChecked"/>'s own remarks for why nothing is
/// rejected here anymore, and <see cref="CCodeGenerator"/>'s own doc comment for how codegen scales by
/// element size instead of assuming every element is one word.
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
  //
  // "float" REMOVED 2026-09-26, per Stefan's own float-ABI dictation (see CType.Float's own remarks) --
  // it is now a real, supported base type (see ParseDeclarationSpecifiers' own "float" handling below).
  // Still a reserved word (ReservedWords above, unchanged); still explicitly listed in LooksLikeTypeStart/
  // LooksLikeTypeAt (which used to recognize it only via this set) since removing it from THIS set alone
  // would otherwise silently stop those two methods from recognizing "float" as a type-starting keyword.
  //
  // "struct" REMOVED 2026-09-26, per Stefan's own "add 'struct' to the C language" instruction -- it is
  // now a real, supported specifier (see ParseStructSpecifier below), handled the same way "float" is:
  // still a reserved word, still explicitly listed in LooksLikeTypeStart/LooksLikeTypeAt so removing it
  // from this set alone doesn't stop either method recognizing it as a type-starting keyword. "union" and
  // "enum" stay rejected here -- struct support does not extend to either.
  //
  // "short" and "long" REMOVED 2026-09-26, per Stefan's own "support 'short'... support 'long'..."
  // instruction (see this class's own remarks above) -- both are now real, supported base-type keywords
  // (see ParseDeclarationSpecifiers' own "short"/"long" handling below), handled the same way "float" and
  // "struct" were: still reserved words, still explicitly listed in LooksLikeTypeStart/LooksLikeTypeAt.
  private static readonly HashSet<string> UnsupportedTypeKeywords = ["union", "enum", "double"];

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

  /// <summary>
  /// Every struct tag seen so far in this translation unit, mapped to its own <see cref="CType"/> --
  /// added 2026-09-26 alongside basic <c>struct</c> support (see this class's own remarks and <see
  /// cref="CType.StructOf"/>'s own remarks). Mirrors <see cref="_typedefs"/>'s own "one flat, unscoped
  /// table for the whole file" simplification exactly, for the same reason (harmless for every program
  /// this compiler is actually meant to compile), and exists for the same core purpose: "struct Tag"
  /// used a second time (as a further member's type, a variable's type, a parameter, a cast, a
  /// <c>sizeof</c>) must resolve to the exact SAME <see cref="CType"/> instance the tag's own
  /// definition created, not a new, unrelated one -- this is also what makes a self-referential struct
  /// possible at all (see <see cref="CType.Members"/>'s own remarks): the tag is registered in this
  /// table the moment "struct Tag" is first seen, before its body (if any) is parsed, so a member
  /// declared as "struct Tag *next" inside that very body resolves back to the same, still-filling-in
  /// instance.
  /// </summary>
  private readonly Dictionary<string, CType> _structTags = new();

  /// <summary>A `__fastcall` function's pointer parameters are passed in the address-register node's four
  /// registers, `ar[0]` through `ar[3]` -- node 308 as of 2026-09-15's renumbering, previously called
  /// "node 306" (see `Ga144.Evb.Ide.Cvm.Node308Program`'s own remarks) -- see `claude/cvm-abi.md` section
  /// 2.2. A 5th pointer parameter
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

  /// <summary>A `__fastcall` function's <c>float</c> parameters are passed in node 305's own 8-register
  /// floating-point file, fr[0] for the first, fr[1] for the second, etc. -- see
  /// <c>claude/cvm-abi.md</c> section 2.3 and this class's own remarks above
  /// <see cref="MaxFastcallRegisterWords"/>. Added 2026-09-26, per Stefan verbatim: "we have 8 float
  /// register. so for fastcall functions the parameter are passed by register fr[0] for the first float
  /// parameter, fr[1] for the second parameter, ...".</summary>
  private const int MaxFastcallFloatParameters = 8;

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
          // A "}" at depth 0 here is unmatched by any "{" seen during this recovery scan, so it must be
          // either a stray brace at file scope, or (when !atTopLevel) the enclosing block's own closing
          // brace that ParseCompoundStatement's loop condition / trailing Expect("}") is waiting to see.
          //
          // At top level there is no such enclosing consumer waiting for it -- Parse()'s own
          // "while (!AtEnd)" loop just calls ParseExternalDeclaration() again, which would immediately
          // fail again on the very same "}" token (nothing starts a declaration with "}"), land back in
          // this same recovery routine, and find the exact same unmatched "}" again. Left unconsumed,
          // that is an infinite loop -- confirmed 2026-09-26 from Stefan's own report of a hang at this
          // file's "expected a type" throw after Build on a real library with a stray/mismatched top-level
          // brace. So at top level we must advance past it ourselves to guarantee forward progress.
          //
          // Nested (atTopLevel: false), we deliberately do NOT advance: this "}" is the block's own
          // closing brace, and ParseCompoundStatement (the only caller that passes atTopLevel: false) both
          // tests for it in its "while (!CheckWord("}") && !AtEnd)" loop condition and consumes it itself
          // via the trailing Expect("}", "to close a block") -- advancing past it here would make that
          // loop think the block hadn't ended yet and try to parse further statements past its real end.
          if (atTopLevel)
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
      CheckWord("void") || CheckWord("char") || CheckWord("int") || CheckWord("float") || CheckWord("unsigned") || CheckWord("signed") ||
      CheckWord("short") || CheckWord("long") ||
      CheckWord("static") || CheckWord("extern") || CheckWord("const") || CheckWord("volatile") ||
      CheckWord("__fastcall") || CheckWord("__lower") || CheckWord("typedef") || CheckWord("struct") ||
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

      // "short" is a pure synonym for plain int width here (see this class's own remarks above) --
      // "signed short"/"signed short int" both just fall through to the same "return CType.Int" plain
      // "signed"/"signed int" already would.
      Match("short");
      Match("int");
      return CType.Int;
    }

    if (Match("unsigned"))
    {
      if (Match("char"))
      {
        return CType.UnsignedChar;
      }

      // "unsigned short"/"unsigned short int" -- see the "signed" branch just above.
      Match("short");
      Match("int");
      return CType.UnsignedInt;
    }

    if (Match("short"))
    {
      // Bare "short" or "short int" -- see this class's own remarks above: short is simply int here.
      Match("int");
      return CType.Int;
    }

    if (Match("char"))
    {
      return CType.Char;
    }

    if (Match("int"))
    {
      return CType.Int;
    }

    if (Match("long"))
    {
      // Bare "long" or "long int" -- see this class's own remarks above and CType.Long's own remarks for
      // the full scope. "signed long"/"unsigned long" are deliberately not recognized as a combination
      // (neither was asked for, and this compiler does not implement any long ARITHMETIC yet regardless
      // of signedness) -- either falls through with "long" left unconsumed by the signed/unsigned
      // branches above, producing a plain (if generic) "expected an identifier" parse error rather than a
      // silent misparse.
      Match("int");
      return CType.Long;
    }

    if (Match("float"))
    {
      return CType.Float;
    }

    if (Match("struct"))
    {
      return ParseStructSpecifier();
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

  /// <summary>Constructs "element *". Originally (2026-09-26, alongside basic <c>float</c> support)
  /// rejected "float *" outright because pointer arithmetic had no element-size scaling; as of
  /// 2026-09-26's follow-up (multi-word pointer arithmetic/indexing, per Stefan's own request), <see
  /// cref="CCodeGenerator"/>'s <c>EmitAddressOfIndex</c>/binary-operator codegen scales by the pointee's
  /// own <see cref="CType.SizeInWords"/> for every element type, so no type is rejected here anymore.
  /// Kept as a named helper (every <see cref="CType.PointerTo"/> call site in this class still goes
  /// through it) purely so a future scope limit has one place to land, not because one exists today.
  /// </summary>
  private CType PointerToChecked(CType element, CSourceLocation location) => CType.PointerTo(element);

  /// <summary>Constructs "element[length]". Originally (2026-09-26) rejected an array of <c>float</c> or
  /// of <c>struct</c> for the same reason <see cref="PointerToChecked"/> once rejected "float *" --
  /// see that helper's own remarks for why neither rejection applies anymore. Still rejects an array of an
  /// INCOMPLETE struct (a forward-declared tag with no body yet) -- the same "incomplete type used by
  /// value" rule <see cref="ParseStructSpecifier"/>'s own member-parsing loop enforces for a by-value
  /// struct member, for the identical reason: <see cref="CType.SizeInWords"/> for such a <c>struct</c> is
  /// (correctly) 0 until it's defined, so an array of it would silently reserve zero storage per element
  /// rather than failing loudly.</summary>
  private CType ArrayOfChecked(CType element, int length, CSourceLocation location)
  {
    if (element.IsStruct && element.Members!.Count == 0)
    {
      throw Error(location, $"an array of incomplete type \"{element}\" is not supported (the struct must be defined first)");
    }

    return CType.ArrayOf(element, length);
  }

  /// <summary>
  /// Parses "struct Tag" (a reference to a previously-declared or forward-declared tag, or the implicit
  /// forward declaration of a brand-new one) optionally followed by "{ member-declaration... }" (the
  /// tag's own definition) -- added 2026-09-26, per Stefan's own "add 'struct' to the C language"
  /// instruction. Called with "struct" itself already consumed by <see cref="ParseDeclarationSpecifiers"/>.
  ///
  /// Anonymous structs ("struct { ... }", no tag) are not supported -- this compiler requires a tag on
  /// every struct, a deliberate small scope limit (real C programs overwhelmingly tag every struct they
  /// define anyway) that keeps <see cref="_structTags"/> a simple flat name table with nothing else to
  /// key an anonymous definition by.
  ///
  /// <b>Self-reference, and why it just works here.</b> The tag is registered in <see
  /// cref="_structTags"/> (with an EMPTY, mutable <see cref="CType.Members"/> list -- see <see
  /// cref="CType.StructOf"/>'s own remarks) before its body (if any) is parsed at all, so a member
  /// declared inside that very body as "struct Tag *next" resolves "struct Tag" back to this same,
  /// still-filling-in <see cref="CType"/> instance -- a pointer's own size never depends on whether its
  /// pointee's member list has finished filling in yet. A BY-VALUE self-reference ("struct Tag next;",
  /// with no "*") is the one case that genuinely cannot work (its own size would depend on itself), and
  /// is rejected explicitly below, the same way any other member whose type is still an INCOMPLETE
  /// struct (an empty member list) is rejected -- see the member-parsing loop's own comment.
  /// </summary>
  private CType ParseStructSpecifier()
  {
    CSourceLocation tagLocation = Current.Location;
    string tag = ExpectIdentifier("after 'struct'");

    if (!_structTags.TryGetValue(tag, out CType? structType))
    {
      structType = CType.StructOf(tag);
      _structTags[tag] = structType;
    }

    if (!Match("{"))
    {
      // A bare reference/forward declaration ("struct Tag" used as a type, or "struct Tag;" on its
      // own) -- return the tag's own type, complete or not; a BY-VALUE use of an incomplete one is
      // caught wherever that value would actually need a known size (a member, a variable, sizeof).
      return structType;
    }

    if (structType.Members!.Count > 0)
    {
      throw Error(tagLocation, $"\"struct {tag}\" is already defined");
    }

    while (!CheckWord("}") && !AtEnd)
    {
      CSourceLocation memberSpecifierLocation = Current.Location;
      CType memberBaseType = ParseDeclarationSpecifiers(out bool isStatic, out bool isExtern, out bool isFastcall, out bool isLower, out bool isTypedef);
      RejectCallingConventionKeywords(isFastcall, isLower, memberSpecifierLocation, "a struct member declaration");
      if (isStatic || isExtern || isTypedef)
      {
        throw Error(memberSpecifierLocation, "'static'/'extern'/'typedef' cannot be used on a struct member");
      }

      do
      {
        CType memberType = ParseDeclaratorType(memberBaseType, "in a struct member declaration", out string memberName, out CSourceLocation memberNameLocation);

        // A 'float' or array member was rejected here in the original (2026-09-26) struct pass, the same
        // multi-word/scaling scope limit PointerToChecked/ArrayOfChecked used to apply -- see their own
        // remarks for why neither rejection applies anymore; CType.SizeInWords/CStructMember.Offset were
        // already fully general (an array member's own size already accounts for its element size), so
        // no change was needed there to allow this.

        // A member whose own type is a struct BY VALUE (not a pointer) must already be a COMPLETE type
        // -- an empty Members list here means either a genuinely-incomplete other tag (declared but
        // never defined) or, most commonly, this exact tag being defined right now (a by-value
        // self-reference) -- both are the same "incomplete type used by value" error, caught generically
        // rather than special-cased for self-reference specifically.
        if (memberType.IsStruct && memberType.Members!.Count == 0)
        {
          throw Error(memberNameLocation, $"field \"{memberName}\" has incomplete type \"{memberType}\" (a struct member cannot have an incomplete struct type by value -- did you mean a pointer, \"{memberType} *\"?)");
        }

        if (structType.Members!.Any(m => m.Name == memberName))
        {
          throw Error(memberNameLocation, $"\"{memberName}\" is already declared in \"struct {tag}\"");
        }

        int offset = structType.Members.Sum(m => Math.Max(m.Type.SizeInWords, 1));
        structType.Members.Add(new CStructMember(memberName, memberType, offset));
      } while (Match(","));

      Expect(";", "after a struct member declaration");
    }

    Expect("}", "to close a struct definition");
    return structType;
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
      type = PointerToChecked(type, Current.Location);
    }

    nameLocation = Current.Location;
    name = ExpectIdentifier(nameContext);

    if (Match("["))
    {
      CSourceLocation bracketLocation = Current.Location;
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

      type = ArrayOfChecked(type, length, bracketLocation);
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
      type = PointerToChecked(type, Current.Location);
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
      if (IsFloatLiteralText(Current.Text))
      {
        throw Error(Current.Location, "a floating-point literal cannot be used in a constant integer expression (array bounds, 'case' labels, etc.)");
      }

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

    // A bare "struct Tag { ... };" (or "struct Tag;") with no variable declared -- added 2026-09-26
    // alongside struct support. This is how a struct is USUALLY defined in real C, and without this
    // check ExpectIdentifier below would fail on the ";" with a confusing "expected an identifier"
    // error instead of simply accepting the tag's own definition.
    if (Match(";"))
    {
      if (!baseType.IsStruct)
      {
        throw Error(location, "expected a declarator after this declaration's type");
      }

      return;
    }

    // The base type carries no pointer stars of its own -- in "int *a, b;", only "a" is a pointer, not
    // "b", so each declarator (the first one included) consumes its own stars starting fresh from
    // baseType, never from a previous declarator's own type.
    CType type = baseType;
    while (Match("*"))
    {
      type = PointerToChecked(type, Current.Location);
    }

    string name = ExpectIdentifier("in a top-level declaration");

    if (Match("("))
    {
      List<CParameter> parameters = ParseParameterList();
      Expect(")", "to close the parameter list");

      // Added 2026-09-26 alongside struct support: a struct can never be returned BY VALUE (only
      // "struct Tag *" is supported -- see CType.StructOf's own remarks). Checked here, once, rather
      // than in CCodeGenerator, since the return type is already fully resolved at this point and this
      // is a pure syntax-level restriction, not something that needs codegen context.
      if (type.IsStruct)
      {
        throw Error(location, $"\"{name}\" cannot return \"{type}\" by value -- return a pointer (\"{type} *\") instead");
      }

      // Added 2026-09-26 alongside 'long' support -- see CType.Long's own remarks: this compiler has no
      // calling convention for returning a 2-word value by value yet (the CVM has no 'long' instructions
      // defined yet), so this is rejected the same way struct-by-value already is, for the same "pure
      // syntax-level restriction" reason given in the comment just above.
      if (type.IsLong)
      {
        throw Error(location, $"\"{name}\" cannot return \"{type}\" by value yet -- the CVM has no 'long' instructions defined yet (return a pointer, \"{type} *\", instead)");
      }

      // "__fastcall implies also __lower, so __fastcall includes __lower" (Stefan, 2026-09-07) -- a
      // __fastcall function is ALWAYS required to be placed in the lower half of memory too, whether or
      // not "__lower" was itself also written on the declaration. See CFunctionDecl's own remarks: a
      // caller only ever needs to check IsLower to decide the call/lcall encoding, never IsFastcall.
      bool isLower = isLowerSpecified || isFastcall;

      // "pointers must be allocated in ar[0..3] for fastcall functions" plus "a 5th __fastcall pointer
      // parameter also rises a compiler error" (Stefan, 2026-09-07, 3rd/4th rounds of claude/cvm-abi.md's
      // dictation): a __fastcall function may declare at most MaxFastcallPointerParameters pointer
      // parameters (the address-register node -- node 308 as of 2026-09-15, previously "node 306" --
      // has exactly that many address registers, ar[0..3]) -- a 5th has nowhere
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
          throw Error(location, $"\"{name}\" is '__fastcall' but declares {pointerParameterCount} pointer parameters -- a '__fastcall' function may have at most {MaxFastcallPointerParameters} (node 308's ar[0..3])");
        }

        int registerWordCount = parameters.Where(p => !p.Type.IsPointer).Sum(p => p.Type.SizeInWords);
        if (registerWordCount > MaxFastcallRegisterWords)
        {
          throw Error(location, $"\"{name}\" is '__fastcall' but its non-pointer parameters need {registerWordCount} node-511 registers -- a '__fastcall' function may use at most {MaxFastcallRegisterWords} (reg[0..{MaxFastcallRegisterWords - 1}])");
        }

        // Added 2026-09-26, per Stefan's own float-ABI dictation: "for fastcall functions the parameter
        // are passed by register fr[0] for the first float parameter, fr[1] for the second parameter,
        // ..." -- a separate, independent counter from both the pointer/ar-register check above and the
        // (not yet implemented -- see registerWordCount's own remarks) node-511 int-register count, since
        // node 305's floating-point register file is its own, separate 8-register file. See
        // CCodeGenerator's own ComputeParameterFloatRegisterSlots for the matching codegen-side count.
        int floatParameterCount = parameters.Count(p => p.Type.IsFloat);
        if (floatParameterCount > MaxFastcallFloatParameters)
        {
          throw Error(location, $"\"{name}\" is '__fastcall' but declares {floatParameterCount} 'float' parameters -- a '__fastcall' function may have at most {MaxFastcallFloatParameters} (node 305's fr[0..{MaxFastcallFloatParameters - 1}])");
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
        CSourceLocation bracketLocation = Current.Location;
        int length = -1;
        if (!CheckWord("]"))
        {
          length = checked((int)ParseConstantIntExpression());
        }

        Expect("]", "to close the array declarator");
        type = ArrayOfChecked(type, length, bracketLocation);
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
        type = PointerToChecked(type, Current.Location);
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

      CType type = ParseDeclaratorType(baseType, "in a parameter declaration", out string name, out CSourceLocation nameLocation);

      // Added 2026-09-26 alongside struct support -- see CType.StructOf's own remarks: only a pointer
      // to a struct is supported as a parameter, never the struct itself by value.
      if (type.IsStruct)
      {
        throw Error(nameLocation, $"\"{name}\" cannot be passed as \"{type}\" by value -- use a pointer (\"{type} *\") instead");
      }

      // Added 2026-09-26 alongside 'long' support -- see this method's own return-type rejection just
      // above (in ParseExternalDeclaration) for why this is a parser-level restriction, not a CCodeGenerator one.
      if (type.IsLong)
      {
        throw Error(nameLocation, $"\"{name}\" cannot be passed as \"{type}\" by value yet -- the CVM has no 'long' instructions defined yet (use a pointer, \"{type} *\", instead)");
      }

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

    // A bare "struct Tag { ... };" (or "struct Tag;") with no variable declared -- see
    // ParseExternalDeclaration's own identical check for why this is needed.
    if (Match(";"))
    {
      if (!baseType.IsStruct)
      {
        throw Error(specifierLocation, "expected a declarator after this declaration's type");
      }

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
    return token.IsIdentifier && (token.Text is "void" or "char" or "int" or "float" or "unsigned" or "signed" or "short" or "long" or "const" or "volatile" or "struct"
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
        bool isArrow = CheckWord("->");
        Advance();
        string member = ExpectIdentifier(isArrow ? "after '->'" : "after '.'");
        expr = new CMemberAccessExpr(location, expr, member, isArrow);
        continue;
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
      if (IsFloatLiteralText(token.Text))
      {
        return new CFloatLiteralExpr(token.Location, ParseFloatLiteralValue(token));
      }

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

  /// <summary>
  /// Disambiguates a lexer "pp-number" token (<see cref="CTokenKind.Number"/> -- the lexer itself does
  /// not distinguish "123" from "3.14" from "1e-3", see <see cref="CLexer"/>'s own remarks) as a
  /// <c>float</c> literal rather than an integer one -- added 2026-09-26 alongside basic <c>float</c>
  /// support. Standard C's own pp-number-to-literal rule: it is a floating literal if it contains a '.',
  /// or an exponent ('e'/'E' followed by an optional sign and digits -- '0x'-prefixed hex-float
  /// exponents use 'p'/'P' instead, but hexadecimal floating-point literals are not supported by this
  /// compiler at all, so 'p'/'P' is deliberately not checked here), or ends in a floating-point suffix
  /// ('f'/'F'/'l'/'L' -- 'l'/'L' alone is ambiguous with an integer's own "long" suffix in general C, but
  /// since this compiler already rejects "long" as a type, a trailing 'l'/'L' on a literal that also has
  /// a '.' or exponent is still unambiguously a float; a bare integer literal like "5L" is NOT floating,
  /// and is caught by the "contains '.' or exponent" checks below already excluding it -- so 'l'/'L' is
  /// only checked when 'f'/'F' would otherwise be needed, not standalone).
  /// </summary>
  private static bool IsFloatLiteralText(string text)
  {
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
    {
      return false; // hex/binary integer literals -- never floating, and 'e'/'f' inside them are hex digits, not markers.
    }

    if (text.Contains('.'))
    {
      return true;
    }

    for (int i = 0; i < text.Length; i++)
    {
      if (text[i] is 'e' or 'E' && i > 0)
      {
        return true;
      }
    }

    char last = text[^1];
    return last is 'f' or 'F';
  }

  /// <summary>Parses a <c>float</c> literal's text into its C# (32-bit IEEE-754, exactly matching the
  /// CVM's own <c>float</c>) value, stripping a trailing 'f'/'F'/'l'/'L' suffix first (this compiler
  /// treats <c>float</c> and <c>double</c> literal suffixes identically, since <c>double</c> itself is
  /// not supported -- see <see cref="CType.Float"/>'s own remarks).</summary>
  private float ParseFloatLiteralValue(CToken token)
  {
    string text = token.Text;
    int end = text.Length;
    while (end > 0 && "fFlL".IndexOf(text[end - 1]) >= 0)
    {
      end--;
    }

    string digits = text[..end];
    if (!float.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
    {
      Error(token.Location, $"\"{token.Text}\" is not a valid floating-point literal");
      return 0f;
    }

    return value;
  }

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