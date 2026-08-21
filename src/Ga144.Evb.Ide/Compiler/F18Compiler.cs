namespace Ga144.Evb.Ide.Compiler;

public sealed class F18Compiler
{
  private static readonly HashSet<string> ReservedCompilerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ":", ";", "[", "]", "org", "entry", "const", "constant", "equ", "f18var", "label", "data", "word", ".word", ",",
        "align", "..", "lit", "literal", "'", "A[", "]]", "call", "jump", "jmp",
        "branch-if", "branch--if", "branch-next", "begin", "again", "until", "-until",
        "if", "-if", "zif", "else", "then", "ahead", "leap", "for", "next", "unext", "while", "-while",
        "repeat", "recurse", "exit", "import", "swap", "here", "end", "*next", "avail", "+cy", "-cy",
        "rot", "nip", "tuck", "1+", "1-", "negate", "=", "<>", "<", ">", "0=", "0<",
        "depth", "rdepth", "clear", "rclear", "invert",
        // Former GreenArrays spellings are reserved so a source file receives a
        // precise migration diagnostic instead of an unresolved-word error.
        "push", "pop", "or"
    };

  private readonly Dictionary<string, int> _constants = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, int> _userConstants = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, F18ExportedSymbol> _symbols = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, F18ExportedSymbol> _externalSymbols = new(StringComparer.OrdinalIgnoreCase);

  private readonly List<F18Diagnostic> _diagnostics = [];
  private readonly List<SymbolRelocation> _symbolRelocations = [];

  private MemoryBuilder? _builder;
  private F18CompileTimeInterpreter? _interpreter;
  private IReadOnlyList<F18Token> _tokens = [];
  private int _tokenIndex;
  private bool _inDefinition;
  private bool _compileMode;
  private string? _currentDefinition;
  private int? _firstDefinitionAddress;
  private F18Token? _entryToken;
  private F18CompilerOptions _options = F18CompilerOptions.ForRam();

  public F18CompileResult Compile(string source, F18CompilerOptions? options = null)
  {
    _options = options ?? F18CompilerOptions.ForRam();
    Reset();
    F18MacroExpansionResult expansion = F18MacroExpander.Expand(source ?? string.Empty, _options, _diagnostics);
    _tokens = F18Tokenizer.Tokenize(expansion.Source, _diagnostics);
    _builder = new MemoryBuilder(
        AddDiagnostic,
        _options.MemoryBaseAddress,
        _options.MemoryWordCount,
        _options.MemoryName);

    while (_tokenIndex < _tokens.Count)
    {
      F18Token token = _tokens[_tokenIndex++];
      CompileToken(token);
    }

    if (!_compileMode)
    {
      AddError("F18C043", "A '[' interpretation section was opened but not closed with ']'.", LastLocation());
    }

    // The F18 compiler compiles continuously; a trailing routine need not end with
    // ';'. An unterminated final routine simply falls through to the end of the
    // dictionary, so that is not an error. Any open control structures are still
    // reported below.

    Builder.Align();
    ResolveSymbolRelocations();

    int? entryPoint = ResolveEntryPoint();
    if (_firstDefinitionAddress is null && !_diagnostics.Any(item => item.Severity == F18DiagnosticSeverity.Error))
    {
      AddWarning("F18C003", "The source contains no word definitions.", new F18SourceLocation(1, 1));
    }

    return new F18CompileResult
    {
      Words = Builder.CreateImage(ResolveUnwrittenRomFillWord()),
      Diagnostics = _diagnostics.ToArray(),
      Symbols = new Dictionary<string, F18ExportedSymbol>(_symbols, StringComparer.OrdinalIgnoreCase),
      Constants = new Dictionary<string, int>(_userConstants, StringComparer.OrdinalIgnoreCase),
      MemorySpace = _options.MemorySpace,
      MemoryBaseAddress = _options.MemoryBaseAddress,
      NodeCoordinate = _options.NodeCoordinate,
      ExpandedSource = expansion.Source,
      EntryPoint = entryPoint,
      UsedWordCount = Builder.UsedWordCount,
      InterpreterDataStack = Interpreter.DataStack.ToArray(),
      InterpreterReturnStack = Interpreter.ReturnStack.ToArray()
    };
  }

  // A ROM word the compiled source never claims reads back on silicon as a CALL to
  // 'warm', not as 0x15555 (see CreateImage). Resolve 'warm' the same way an
  // ordinary bareword reference would -- a local definition first (the common case:
  // rom_warm's ': warm await ;' compiled earlier in the same source), falling back
  // to an external/predefined symbol (a caller-supplied override, e.g. a test
  // harness) -- and pre-encode the CALL word for CreateImage to drop into any
  // untouched cell. Returns null (leaving untouched cells at 0x15555, the prior
  // behavior) for RAM compiles or a ROM compile that never defines 'warm'.
  private int? ResolveUnwrittenRomFillWord()
  {
    if (_options.MemorySpace != F18MemorySpace.Rom)
    {
      return null;
    }

    int warmAddress;
    if (_symbols.TryGetValue("warm", out F18ExportedSymbol? localWarm) && localWarm is not null)
    {
      warmAddress = localWarm.Value;
    }
    else if (_externalSymbols.TryGetValue("warm", out F18ExportedSymbol? externalWarm) && externalWarm is not null)
    {
      warmAddress = externalWarm.Value;
    }
    else
    {
      return null;
    }

    return F18InstructionSet.EncodeSlot0Control(0x03, warmAddress & 0x3FF);
  }

  private void Reset()
  {
    _constants.Clear();
    _userConstants.Clear();
    _symbols.Clear();
    _externalSymbols.Clear();
    _diagnostics.Clear();

    foreach (KeyValuePair<string, int> pair in F18InstructionSet.Constants)
    {
      _constants[pair.Key] = pair.Value;
    }

    // DB013 4.2.7.2: 'north'/'south'/'east'/'west' are per-node Named Literals --
    // they resolve to whichever local port ('up'/'down'/'left'/'right') that
    // geographic direction maps to for this node's position (the F18A swaps
    // up/down on odd rows and left/right on odd columns). Seeded here, alongside
    // the other Named Literals, so PredefinedConstants below can still override.
    foreach (CardinalDirection direction in Enum.GetValues<CardinalDirection>())
    {
      string localName = F18AwaitAddresses.LocalPortName(_options.NodeCoordinate, direction);
      _constants[direction.ToString().ToLowerInvariant()] = F18InstructionSet.Constants[localName];
    }

    foreach (KeyValuePair<string, int> pair in _options.PredefinedConstants)
    {
      _constants[pair.Key] = pair.Value & F18InstructionSet.WordMask;
    }

    foreach (KeyValuePair<string, F18ExportedSymbol> pair in _options.PredefinedSymbols)
    {
      _externalSymbols[pair.Key] = pair.Value;
    }

    // DB013 4.2.7.3: each of the 15 named multiport addresses normally assembles a
    // CALL to that address (a tail jump in 'name ;' position, like any other word
    // reference) -- unlike Constants, this is unconditional (not gated by
    // IncludeCommonRomWords): these are fixed silicon addresses, not borrowed ROM
    // subroutines. PredefinedSymbols above still takes priority if a caller wants
    // to override one.
    foreach (KeyValuePair<string, int> pair in F18InstructionSet.NamedMultiportCalls)
    {
      if (!_externalSymbols.ContainsKey(pair.Key))
      {
        _externalSymbols[pair.Key] = new F18ExportedSymbol(
            pair.Key,
            pair.Value,
            F18ExportKind.Word,
            _options.NodeCoordinate,
            F18MemorySpace.Rom);
      }
    }

    if (_options.IncludeCommonRomWords)
    {
      foreach (KeyValuePair<string, int> pair in F18InstructionSet.CallableRomWords)
      {
        if (!_externalSymbols.ContainsKey(pair.Key))
        {
          _externalSymbols[pair.Key] = new F18ExportedSymbol(
              pair.Key,
              pair.Value,
              F18ExportKind.Word,
              _options.NodeCoordinate,
              F18MemorySpace.Rom);
        }
      }
    }

    _symbolRelocations.Clear();
    _tokens = [];
    _tokenIndex = 0;
    _inDefinition = false;
    // The compiler starts in compile mode; only '[' switches to interpretation.
    _compileMode = true;
    _currentDefinition = null;
    _firstDefinitionAddress = null;
    _entryToken = null;
    _builder = null;
    _interpreter = new F18CompileTimeInterpreter(AddDiagnostic);
  }

  private void CompileToken(F18Token token)
  {
    string word = token.Text.ToLowerInvariant();

    switch (word)
    {
      case "[":
        BeginInterpretation(token);
        return;
      case "]":
        ResumeCompilation(token);
        return;
      case ":":
        BeginDefinition(token);
        return;
      case ";":
        EndDefinition(token);
        return;
      case "push":
        AddError("F18C044", "The F18A source word 'push' is named '>r' in this textual FORTH syntax.", token.Location);
        return;
      case "pop":
        AddError("F18C045", "The F18A source word 'pop' is named 'r>' in this textual FORTH syntax.", token.Location);
        return;
      case "or":
        AddError("F18C046", "The F18A opcode at 0x16 is exclusive OR; use the source word 'xor'.", token.Location);
        return;
      case "-":
        AddError("F18C054", "The F18A opcode at 0x13 inverts all bits and is now named 'inv' (or 'not'); the former '-' spelling was removed to avoid confusion with subtraction. A leading '-' still denotes a negative numeric literal.", token.Location);
        return;
      case "org":
        InterpretOrigin(token);
        return;
      case "entry":
        CompileEntry(token);
        return;
      case "const":
      case "constant":
        InterpretConstant(token);
        return;
      case "equ":
        InterpretEqu(token);
        return;
      case "f18var":
        CompileF18Var(token);
        return;
      case "import":
        InterpretNodeImport(token);
        return;
      case "label":
        CompileLabel(token);
        return;
      case ".loc":
        ShowLocation(token);
        return;
      case "data":
        // Preserve the earlier module-level raw-data directive. Inside a
        // definition, including a [ ... ] section, data is the I/O constant.
        if (_inDefinition)
        {
          DispatchOrdinaryToken(token);
        }
        else
        {
          CompileRawData(token);
        }
        return;
      case "word":
      case ".word":
        CompileRawData(token);
        return;
      case ",":
        InterpretComma(token);
        return;
      case "+cy":
        // Enter Extended Arithmetic Mode: set bit P9 on the location counter so
        // every address captured here carries it (DB001 2.1, DB002 3.2).
        Builder.SetExtendedArithmetic(true);
        return;
      case "-cy":
        // Leave Extended Arithmetic Mode: clear bit P9 on the location counter.
        Builder.SetExtendedArithmetic(false);
        return;
      case "align":
      case "..":
        Builder.Align();
        return;
      case "#":
        PushHashValue(token);
        return;
      case "'":
        PushTickValue(token);
        return;
      // 'lit' is the real GA144/DB013 word: postfix, same as 'literal' -- it
      // compiles whatever is already on the compile-time stack (typically left
      // there by a preceding '[ ... ]' section) as an @p literal. Confirmed
      // against rom_sync's ROM source: 'avail 0x3.FC00 [ + ] lit' computes a
      // value in brackets, then 'lit' takes it off the stack; it is not a prefix
      // 'lit <value>' operator.
      case "lit":
      case "literal":
        CompileStackLiteral(token);
        return;
      case "a[":
        CompileQuotedInstruction(token);
        return;
      case "call":
        CompileExplicitControl(token, 0x03, "call");
        return;
      case "jump":
      case "jmp":
        CompileExplicitControl(token, 0x02, "jump");
        return;
      case "branch-if":
        CompileExplicitControl(token, 0x06, "conditional branch");
        return;
      case "branch--if":
        CompileExplicitControl(token, 0x07, "minus-if branch");
        return;
      case "branch-next":
        CompileExplicitControl(token, 0x05, "next branch");
        return;
      case "begin":
        CompileBegin(token);
        return;
      case "again":
        CompileBackwardBranch(token, ControlKind.Begin, 0x02, "begin");
        return;
      case "until":
        CompileBackwardBranch(token, ControlKind.Begin, 0x06, "begin");
        return;
      case "-until":
        CompileBackwardBranch(token, ControlKind.Begin, 0x07, "begin");
        return;
      case "if":
        CompileForwardIf(token, 0x06, ControlKind.If);
        return;
      case "-if":
        CompileForwardIf(token, 0x07, ControlKind.MinusIf);
        return;
      case "zif":
        // DB013 5.3.2.1: if R is zero, pop R and continue; otherwise decrement R
        // and jump to matching 'then'. This is the R-controlled 'next' opcode
        // (0x05) used as a FORWARD transfer resolved by 'then'.
        CompileForwardIf(token, 0x05, ControlKind.If);
        return;
      case "else":
        CompileElse(token);
        return;
      case "then":
        CompileThen(token);
        return;
      case "ahead":
        CompileAhead(token);
        return;
      case "leap":
        // DB013 5.3.2.1: leap compiles a CALL to matching then (ahead jumps).
        CompileLeap(token);
        return;
      case "for":
        CompileFor(token);
        return;
      case "next":
        CompileNext(token);
        return;
      case "unext":
        // DB013 5.3.2.2: ends a micronext loop (opcode 0x04). The loop body is
        // within a single instruction word, so no destination address is used;
        // the matching 'for' marker is consumed.
        CompileUnext(token);
        return;
      case "*next":
        // DB013 5.3.2.3: equivalent to 'swap next'.
        CompileStarNext(token);
        return;
      case "end":
        // DB013 5.3.2.3: unconditionally jumps to a (backward jump, opcode 0x02),
        // consuming a 'begin' marker.
        CompileBackwardBranch(token, ControlKind.Begin, 0x02, "begin");
        return;
      case "while":
        CompileWhile(token, 0x06, ControlKind.While);
        return;
      case "-while":
        CompileWhile(token, 0x07, ControlKind.MinusWhile);
        return;
      case "repeat":
        CompileRepeat(token);
        return;
      case "recurse":
        CompileRecurse(token);
        return;
      case "exit":
        if (RequireDefinition(token))
        {
          Builder.EmitPrimitive(0x00, token);
        }
        return;
      case "]]":
        AddError("F18C004", "Unexpected quoted-instruction terminator.", token.Location);
        return;
      case "here":
        InterpretHere(token);
        return;
      case "avail":
        InterpretAvail(token);
        return;
    }

    DispatchOrdinaryToken(token);
  }

  private void DispatchOrdinaryToken(F18Token token)
  {
    if (IsInterpreting)
    {
      InterpretOrdinaryToken(token);
    }
    else
    {
      CompileOrdinaryToken(token);
    }
  }

  private void InterpretOrdinaryToken(F18Token token)
  {
    if (TryResolveInterpretValue(token, out int value))
    {
      Interpreter.TryPushData(value, token);
      return;
    }

    if (Interpreter.TryExecute(token.Text, token))
    {
      return;
    }

    if (F18InstructionSet.Opcodes.ContainsKey(token.Text))
    {
      AddError(
          "F18C047",
          $"Target primitive '{token.Text}' cannot execute in the compile-time interpreter. Use ']' to resume target compilation.",
          token.Location);
      return;
    }

    AddError("F18C048", $"Unknown compile-time FORTH word '{token.Text}'.", token.Location);
  }

  private void CompileOrdinaryToken(F18Token token)
  {
    if (token.Text.Equals("swap", StringComparison.OrdinalIgnoreCase))
    {
      CompileSwap(token);
      return;
    }

    // In the Assembler environment a number (or resolved constant/label) compiles
    // directly into the code as an @p literal. Precede it with '#' to leave it on
    // the compile-time stack instead (see the '#' handling in CompileToken).
    if (TryResolveValue(token, out int value))
    {
      Builder.EmitLiteral(value, token);
      return;
    }

    if (F18InstructionSet.Opcodes.TryGetValue(token.Text, out byte opcode))
    {
      Builder.EmitPrimitive(opcode, token);
      return;
    }

    if (_symbols.TryGetValue(token.Text, out F18ExportedSymbol? localSymbol) && localSymbol is not null)
    {
      EmitWordReference(localSymbol, token);
      return;
    }

    if (_externalSymbols.TryGetValue(token.Text, out F18ExportedSymbol? externalSymbol) && externalSymbol is not null)
    {
      // DB013 4.2.7.3 named multiport calls (---u..rdlu and their un-dashed
      // aliases) are confirmed against silicon to always occupy a dedicated
      // slot-0 word: the chip never packs one into a free slot alongside a
      // preceding op, unlike an ordinary word call/jump. Force alignment only
      // for the un-shadowed built-in binding; a caller-supplied
      // PredefinedSymbols override for the same name keeps normal packing.
      bool forceAligned = F18InstructionSet.NamedMultiportCalls.ContainsKey(token.Text) &&
          !_options.PredefinedSymbols.ContainsKey(token.Text);
      EmitWordReference(externalSymbol, token, forceAligned);
      return;
    }

    // Unknown word: a forward reference resolved later. In tail position ('name ;')
    // it is a jump, otherwise a call (DB001 2.1: 'name ;' compiles to jump to name).
    if (ConsumeTailSemicolon())
    {
      EmitSymbolControl(0x02, token);
    }
    else
    {
      EmitSymbolControl(0x03, token);
    }
  }

  // Emit a reference to a resolved word/symbol. A Word in tail position ('name ;')
  // compiles to a jump (the jump is the return), otherwise a call; the trailing
  // ';' is consumed here so it does not also emit a 'return'. Non-Word symbols are
  // literals and are unaffected. 'forceAligned' skips the free-slot packing
  // optimization and always emits a dedicated slot-0 word (see the named
  // multiport call case above).
  private void EmitWordReference(F18ExportedSymbol symbol, F18Token token, bool forceAligned = false)
  {
    if (symbol.Kind != F18ExportKind.Word)
    {
      Builder.EmitLiteral(symbol.Value, token);
      return;
    }

    var opcode = ConsumeTailSemicolon() ? (byte)0x02 : (byte)0x03;
    if (forceAligned)
    {
      Builder.EmitControl(opcode, symbol.Value & 0x3FF, token);
      return;
    }

    EmitKnownControl(opcode, symbol.Value, token);
  }

  // When the next token is ';' (and we are compiling, not in a '[' section),
  // consume it and return true, signalling that the just-emitted transfer is a
  // tail call: it is encoded as a jump instead of a call, and the swallowed ';'
  // emits no 'return'. Consuming the ';' does NOT change compile mode -- the F18
  // compiler stays in compile mode across ';'.
  private bool ConsumeTailSemicolon()
  {
    if (!_compileMode || !Peek(";"))
    {
      return false;
    }

    _tokenIndex++;
    return true;
  }

  private void BeginInterpretation(F18Token token)
  {
    // '[' switches from compile mode to interpretation. It is valid anywhere the
    // compiler is currently compiling.
    if (!_compileMode)
    {
      AddError("F18C049", "Nested '[' is not allowed; the compiler is already interpreting.", token.Location);
      return;
    }

    _compileMode = false;
  }

  private void ResumeCompilation(F18Token token)
  {
    // ']' resumes compile mode after a '[' interpretation section.
    if (_compileMode)
    {
      AddError("F18C051", "Unexpected ']'; target compilation is already active.", token.Location);
      return;
    }

    _compileMode = true;
  }

  private void InterpretOrigin(F18Token token)
  {
    // 'org' sets the location counter. It is an immediate directive and takes its
    // argument from the compile-time stack (push it with '#', e.g. '# xA9 org').
    // The F18 compiler compiles continuously, so there is no "inside a definition"
    // state to forbid it in.
    if (Interpreter.TryPopData(token, out int value))
    {
      SetOrigin(value, token);
    }
  }

  private void InterpretConstant(F18Token token)
  {
    // 'constant' names the value on the compile-time stack (push it with '#'). It
    // is an immediate directive; there is no definition state to forbid it in.
    F18Token? name = ReadRequiredToken(token, "constant name");
    if (name is null || !Interpreter.TryPopData(token, out int value))
    {
      return;
    }

    // Like a ':'/'label'/'equ' definition, a constant may take over a name that
    // only an 'import' has bound (warning, see TryShadowImportedName).
    if (NameExists(name.Text) && !TryShadowImportedName(name.Text, name))
    {
      AddError("F18C016", $"The name '{name.Text}' is already defined or reserved.", name.Location);
      return;
    }

    value &= F18InstructionSet.WordMask;
    _constants[name.Text] = value;
    _userConstants[name.Text] = value;
  }

  // 'equ' (n --): names the value on the compile-time stack (push it with '#',
  // e.g. '# 0xBE equ sget') as a CALLABLE label -- 'equ' assigns an explicit
  // address to a name, the same way a ':' definition's name gets the address it
  // starts at, except the address comes from the stack instead of the location
  // counter. Confirmed against rom_sync's ROM: 'sget' (defined via '# 0xBE equ
  // sget') is referenced bare three times in 'ser-exec' and each site compiles
  // to a CALL to 0xBE, not a literal push of 0xBE -- so equ'd names default to
  // Word (auto-call) like ordinary colon-definitions, matching DB013 4.2.7.3's
  // 'Named Calls' default rather than 4.2.7.1's 'Named Literals' one. '#' before
  // the name still pushes its raw address value (TryResolveInterpretValue reads
  // any symbol's Value regardless of Kind), so it can still feed
  // 'call'/'jump'/a control-transfer word explicitly when needed. It is an
  // immediate directive; there is no definition state to forbid it in.
  private void InterpretEqu(F18Token token)
  {
    F18Token? name = ReadRequiredToken(token, "equ name");
    if (name is null || !Interpreter.TryPopData(token, out int value))
    {
      return;
    }

    DefineSymbol(name, value & F18InstructionSet.WordMask, F18ExportKind.Word);
  }

  // 'f18var' (n --): declares a RAM variable using the classic F18 @p/!p
  // storage-cell idiom instead of requiring it to be hand-written every time.
  // 'n', the variable's initial value, must already be on the compile-time
  // stack (push it with '#', the same convention 'constant' and 'equ' use).
  // Expands to exactly:
  //     : !name @p drop !p ; : name @p ; ,
  // -- 'name' fetches: '@p' reads the memory word that immediately follows
  // name's own one-word body, which is exactly the cell the trailing ','
  // appends right after it. '!name' stores: '@p' reads (and discards, via
  // 'drop') that same cell so P is left pointing at it, then '!p' writes the
  // new top-of-stack there. Both 'name' and '!name' end up as perfectly
  // ordinary words -- this directive is pure sugar over ':'/opcodes/',' the
  // compiler already supports, so rather than emit instruction words
  // directly it splices the equivalent source text in at the current read
  // position and lets the normal pipeline compile it (the same way an
  // 'import'ed macro's body is woven into compilation).
  //
  // Only useful in RAM: '!p' cannot store into ROM (DB001/DB002), so an
  // f18var compiled into ROM would build without error but be inert on real
  // hardware -- flagged here instead of failing silently later.
  private void CompileF18Var(F18Token token)
  {
    F18Token? name = ReadRequiredToken(token, "variable name");
    if (name is null)
    {
      return;
    }

    if (_options.MemorySpace != F18MemorySpace.Ram)
    {
      AddWarning(
          "F18C058",
          $"'f18var {name.Text}' declares a RAM variable, but this compilation targets ROM. " +
          $"'!{name.Text}' would try to store via '!p', which does not work in ROM, so this " +
          "variable would be unusable on real hardware.",
          token.Location);
    }

    string syntheticSource = $": !{name.Text} @p drop !p ; : {name.Text} @p ; ,";
    var syntheticDiagnostics = new List<F18Diagnostic>();
    IReadOnlyList<F18Token> syntheticTokens = F18Tokenizer.Tokenize(syntheticSource, syntheticDiagnostics);

    if (syntheticDiagnostics.Count > 0 || syntheticTokens.Count != 11)
    {
      // Should be unreachable for any name the real tokenizer already accepted
      // once (as the token this method itself just read) -- guarded rather
      // than assumed, so a future tokenizer change fails loudly here instead
      // of silently miscompiling.
      AddError(
          "F18C059",
          $"'f18var {name.Text}' could not build its generated definitions.",
          token.Location);
      return;
    }

    // Re-point every generated token at the variable name's own source
    // position, so any diagnostic raised while compiling them (most likely a
    // 'name'/'!name' collision) lands on something the user actually wrote,
    // not on the throwaway synthetic snippet's own line 1.
    List<F18Token> remapped = syntheticTokens
        .Select(syntheticToken => syntheticToken with { Line = name.Line, Column = name.Column })
        .ToList();

    // Splice the generated tokens in at the current read position, exactly
    // where 'f18var name' itself sat. The main compile loop (and every
    // nested ReadRequiredToken call the generated ':'/';'/',' processing
    // makes) reads from _tokens/_tokenIndex, so once this returns, the very
    // next tokens it sees are '!name's body, then 'name's, then the comma --
    // it never learns this text wasn't in the original source.
    var spliced = new List<F18Token>(_tokens.Count + remapped.Count);
    spliced.AddRange(_tokens.Take(_tokenIndex));
    spliced.AddRange(remapped);
    spliced.AddRange(_tokens.Skip(_tokenIndex));
    _tokens = spliced;
  }

  private void InterpretNodeImport(F18Token token)
  {
    // 'import' is an immediate directive taking a node coordinate from the
    // compile-time stack. There is no definition state to forbid it in.
    if (!Interpreter.TryPopData(token, out int coordinate))
    {
      return;
    }

    CompileImportCoordinate(coordinate, token);
  }

  private void InterpretComma(F18Token token)
  {
    if (!Interpreter.TryPopData(token, out int value))
    {
      return;
    }

    Builder.EmitRaw(value, token);
  }

  private void InterpretHere(F18Token token)
  {
    Builder.Align();
    Interpreter.TryPushData(Builder.CurrentAddress, token);
  }

  // 'avail' (GreenArrays): pushes the current node's natural multiport-execute
  // address -- the address an idle node waits at, whose available ports depend on
  // the node's position (corner/edge/interior). This is the same value 'await'
  // resolves to, but pushed onto the compile-time stack as a value (e.g. for
  // '-until' to use as a loop destination) rather than compiled as a call target.
  private void InterpretAvail(F18Token token) =>
      Interpreter.TryPushData(F18AwaitAddresses.ForNode(_options.NodeCoordinate), token);

  private void CompileSwap(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    // F18A has no native SWAP opcode. This sequence preserves the previous
    // return stack and transforms (... x y -- ... y x):
    // over >r >r drop r> r>
    byte[] opcodes = [0x1A, 0x1D, 0x1D, 0x17, 0x19, 0x19];
    foreach (byte opcode in opcodes)
    {
      Builder.EmitPrimitive(opcode, token);
    }
  }

  private void CompileImportCoordinate(int coordinate, F18Token directive)
  {
    int row = coordinate / 100;
    int column = coordinate % 100;
    if (coordinate < 0 || row is < 0 or > 7 || column is < 0 or > 17)
    {
      AddError("F18C037", $"'{coordinate}' is not a valid GA144 node coordinate.", directive.Location);
      return;
    }

    if (coordinate == _options.NodeCoordinate)
    {
      AddError("F18C038", "A node cannot import itself. Its own ROM dictionary is available automatically when compiling RAM.", directive.Location);
      return;
    }

    if (_options.ImportResolver is null)
    {
      AddError("F18C039", "No node-import resolver is available in this compilation context.", directive.Location);
      return;
    }

    F18ImportResolution resolution;
    try
    {
      resolution = _options.ImportResolver(coordinate);
    }
    catch (Exception exception)
    {
      AddError("F18C040", $"Node {coordinate:000} import failed: {exception.Message}", directive.Location);
      return;
    }

    if (!resolution.Success || resolution.Exports is null)
    {
      AddError(
          "F18C041",
          $"Node {coordinate:000} import failed: {resolution.ErrorMessage ?? "unknown compilation error"}",
          directive.Location);
      return;
    }

    foreach (KeyValuePair<string, int> pair in resolution.Exports.Constants)
    {
      if (!TryAddImportedValue(pair.Key, pair.Value, coordinate, F18ExportKind.Constant, directive))
      {
        return;
      }
    }

    foreach (F18ExportedSymbol symbol in resolution.Exports.Symbols.Values)
    {
      // Cross-node names are addresses, never local calls. Treat words and labels alike as imported values.
      if (!TryAddImportedValue(symbol.Name, symbol.Value, coordinate, F18ExportKind.Label, directive))
      {
        return;
      }
    }
  }

  private bool TryAddImportedValue(
      string name,
      int value,
      int sourceNode,
      F18ExportKind kind,
      F18Token token)
  {
    if (NameExists(name))
    {
      // One import may replace a name a PREVIOUS import bound (warning, last
      // import wins) -- the same rule a local definition gets in
      // TryShadowImportedName. A collision with anything locally defined stays an
      // error, and deliberately so: unlike the shadowing case, letting the import
      // win would retarget forward references that ResolveSymbolRelocations has
      // not patched yet, silently redirecting already-compiled call sites to
      // another node's address.
      if (!IsImportedNameOnly(name))
      {
        AddError("F18C042", $"Import from node {sourceNode:000} conflicts with existing name '{name}'.", token.Location);
        return false;
      }

      F18ExportedSymbol previous = _externalSymbols[name];
      AddWarning(
          "F18C056",
          $"Import from node {sourceNode:000} replaces '{name}', already imported from node " +
          $"{previous.NodeCoordinate:000} (0x{previous.Value:X3}). The newer import is used from here on.",
          token.Location);
    }

    _externalSymbols[name] = new F18ExportedSymbol(
        name,
        value & F18InstructionSet.WordMask,
        kind,
        sourceNode,
        _options.MemorySpace);
    return true;
  }

  private bool TryResolveInterpretValue(F18Token token, out int value)
  {
    if (_constants.TryGetValue(token.Text, out value))
    {
      return true;
    }

    if (_symbols.TryGetValue(token.Text, out F18ExportedSymbol? localSymbol) && localSymbol is not null)
    {
      value = localSymbol.Value;
      return true;
    }

    if (_externalSymbols.TryGetValue(token.Text, out F18ExportedSymbol? externalSymbol) && externalSymbol is not null)
    {
      value = externalSymbol.Value;
      return true;
    }

    return TryParseNumber(token.Text, out value);
  }

  // Per the F18/colorForth model the compiler is ALWAYS in compile mode except
  // between '[' and ']'. ':' and ';' do not change the mode (':' flushes and
  // labels; ';' emits a return), so interpretation is governed solely by whether
  // a '[' section is open.
  private bool IsInterpreting => !_compileMode;

  private F18CompileTimeInterpreter Interpreter =>
      _interpreter ?? throw new InvalidOperationException("Compile-time FORTH interpreter is not initialized.");

  private static bool TryParseNodeCoordinate(string text, out int coordinate)
  {
    coordinate = 0;
    if (string.IsNullOrWhiteSpace(text) || text.Any(character => !char.IsDigit(character)) ||
        !int.TryParse(text, out coordinate))
    {
      return false;
    }

    var row = coordinate / 100;
    var column = coordinate % 100;
    return row is >= 0 and <= 7 && column is >= 0 and <= 17;
  }

  private void BeginDefinition(F18Token token)
  {
    // ':' does not enter compile mode (the compiler is already compiling); it
    // flushes the current instruction word and assigns a label at the current
    // address. Execution falls through into the new label -- a following ':' after
    // an unterminated word simply sets a new label (the 'relay'/'done' idiom).
    // Pending control values on the shared stack intentionally carry across ':'.
    if (!_compileMode)
    {
      // A '[' interpretation section must be closed with ']' before a new label.
      AddError("F18C052", "A new ':' label cannot start inside a '[' interpretation section; insert ']' first.", token.Location);
      _compileMode = true;
    }

    var name = ReadRequiredToken(token, "word name");
    if (name is null)
    {
      return;
    }

    Builder.Align();
    if (!DefineSymbol(name, Builder.CurrentAddress, F18ExportKind.Word))
    {
      return;
    }

    _inDefinition = true;
    _currentDefinition = name.Text;
    _firstDefinitionAddress ??= Builder.CurrentAddress;
  }

  private void EndDefinition(F18Token token)
  {
    // Per the F18 model ';' does not exit compile mode or end the "definition"; it
    // only emits a 'return' (unless a preceding call was converted to a tail jump,
    // handled at the call site). Code after ';' continues to compile, so a ';' in
    // the middle of a routine (e.g. 'inv 2* ; then drop 2* inv ;') is valid.
    if (!_compileMode)
    {
      // Inside a '[' interpretation section ';' has no meaning; it must be a target
      // (compiled) construct. Resume compilation with ']' first.
      AddError("F18C052", "';' cannot appear inside a '[' interpretation section; insert ']' first.", token.Location);
      _compileMode = true;
    }

    Builder.EmitPrimitive(0x00, token);
  }

  private void SetOrigin(int value, F18Token token)
  {
    // The origin may land in the memory region or its mirror (DB001 Figure 2): the
    // 64 words repeat once (RAM x000-x03F at x040-x07F, ROM x080-x0BF at x0C0-x0FF).
    var first = _options.MemoryBaseAddress;
    var last = first + (_options.MemoryWordCount * 2) - 1;
    if (value < first || value > last)
    {
      AddError(
          "F18C012",
          $"{_options.MemoryName} origin must be between 0x{first:X3} and 0x{last:X3}.",
          token.Location);
      return;
    }

    Builder.SetOrigin(value, token);
  }

  private void CompileEntry(F18Token token)
  {
    if (_inDefinition)
    {
      AddError("F18C013", "entry is a module-level directive and cannot appear inside a definition.", token.Location);
      return;
    }

    _entryToken = ReadRequiredToken(token, "entry-point symbol or address");
  }

  private void CompileLabel(F18Token token)
  {
    var name = ReadRequiredToken(token, "label name");
    if (name is null)
    {
      return;
    }

    Builder.Align();
    DefineSymbol(name, Builder.CurrentAddress, F18ExportKind.Label);
  }

  // Debug-only, colorForth-grey-word-style location annotation: reports where
  // compilation currently sits without emitting any code, defining any symbol,
  // or altering output in any way. Deliberately does NOT call Builder.Align()
  // the way 'label' does -- forcing alignment here would flush/pad whatever
  // word is currently in progress just because a debug directive was inserted,
  // silently changing the compiled ROM/RAM layout. Builder.PendingAddress
  // reports the in-progress word's own address instead, safe to read whether
  // or not a partial word is pending.
  private void ShowLocation(F18Token token) =>
      AddInfo("F18I010", $"'.loc': current address 0x{Builder.PendingAddress:X3}.", token.Location);

  private void CompileRawData(F18Token token)
  {
    var valueToken = ReadRequiredToken(token, "raw 18-bit word");
    if (valueToken is null || !TryResolveValue(valueToken, out var value))
    {
      if (valueToken is not null)
      {
        AddError("F18C017", $"'{valueToken.Text}' is not a numeric value or constant.", valueToken.Location);
      }

      return;
    }

    Builder.EmitRaw(value, valueToken);
  }

  private void CompileStackLiteral(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    // Standard FORTH form: [ ...compile-time calculation... ] literal.
    // The value must already be on the compile-time data stack.
    if (Interpreter.TryPopData(token, out int value))
    {
      Builder.EmitLiteral(value, token);
    }
  }

  // '#' (GreenArrays Assembler): the following number/value is left on the
  // compile-time stack instead of being compiled as a literal. Also conditions
  // named values (ports, constants) and named calls to push rather than compile.
  // Used to pass arguments to directives, e.g. '# xA9 org'.
  private void PushHashValue(F18Token token)
  {
    F18Token? valueToken = ReadRequiredToken(token, "value after '#'");
    if (valueToken is null)
    {
      return;
    }

    if (TryResolveInterpretValue(valueToken, out int value))
    {
      Interpreter.TryPushData(value, token);
      return;
    }

    AddError(
        "F18C018",
        $"'{valueToken.Text}' after '#' is not a numeric value, constant, label, or named value.",
        valueToken.Location);
  }

  // ' (tick, colorForth/AN003 convention): the following word's ADDRESS is
  // left on the compile-time stack rather than being compiled as a call --
  // functionally the same lookup '#' already performs (PushHashValue), just
  // spelled the way AN003's original listings write it, e.g. node 107's
  // 'cmd @ -if @ ' cx -until ...' and 'poll ... dup ! ' re end'. Both need the
  // ENTRY ADDRESS of an already-defined, separately ';'-terminated word so a
  // backward branch (-until/end) can jump straight into it, rather than the
  // 'begin' marker a plain backward branch would otherwise capture at the
  // branch site itself. Transcribed source may write either '[ ' cx ] -until'
  // or the equivalent bareword form '[ cx ] -until' (a name inside '[ ... ]'
  // already resolves to its address per TryResolveInterpretValue) -- tick adds
  // no new capability, only the original spelling.
  private void PushTickValue(F18Token token)
  {
    F18Token? nameToken = ReadRequiredToken(token, "word name after '''");
    if (nameToken is null)
    {
      return;
    }

    if (TryResolveInterpretValue(nameToken, out int value))
    {
      Interpreter.TryPushData(value, token);
      return;
    }

    AddError(
        "F18C060",
        $"''' cannot find a word, constant, or label named '{nameToken.Text}'.",
        nameToken.Location);
  }

  private void CompileQuotedInstruction(F18Token openingToken)
  {
    var opcodes = new List<byte>();
    var terminated = false;

    // A[ ... ]] normally assembles a plain instruction word from up to four
    // primitive opcodes. But a quoted word may also embed a CALL to a resolved
    // word -- local or (per DB002 3.1) imported from another node -- when the
    // whole point of the word is to be shipped elsewhere over a port and executed
    // there: the target node has no way to reach this node's dictionary, so the
    // call target must be assembled as raw bits here, in this node's compiler,
    // before transmission. A bare (unquoted) reference to a cross-node name still
    // pushes its address as a literal (see EmitWordReference) -- that rule is
    // about ordinary code in THIS node, which can never execute a call into
    // another node's address space. Inside a quote the resulting word is data,
    // not something this node runs, so encoding a call into it is meaningful.
    // Once a word reference appears, it occupies the rest of the word (same rule
    // as any other packed control transfer) -- no further opcodes may follow it.
    F18Token? callToken = null;
    var callTarget = 0;
    var hasCall = false;

    while (_tokenIndex < _tokens.Count)
    {
      var token = _tokens[_tokenIndex++];
      if (token.Text == "]]")
      {
        terminated = true;
        break;
      }

      if (hasCall)
      {
        AddError(
            "F18C057",
            $"'{token.Text}' cannot follow '{callToken!.Text}' inside A[ ... ]]; a word reference occupies the rest of the quoted word.",
            token.Location);
        continue;
      }

      byte opcode;
      if (token.Text == ";")
      {
        opcode = 0x00;
      }
      else if (token.Text.Equals("push", StringComparison.OrdinalIgnoreCase))
      {
        AddError("F18C044", "Inside A[ ... ]], use '>r' instead of the former F18A name 'push'.", token.Location);
        continue;
      }
      else if (token.Text.Equals("pop", StringComparison.OrdinalIgnoreCase))
      {
        AddError("F18C045", "Inside A[ ... ]], use 'r>' instead of the former F18A name 'pop'.", token.Location);
        continue;
      }
      else if (token.Text.Equals("or", StringComparison.OrdinalIgnoreCase))
      {
        AddError("F18C046", "Inside A[ ... ]], use 'xor'; F18A opcode 0x16 is exclusive OR.", token.Location);
        continue;
      }
      else if (token.Text.Equals("-", StringComparison.OrdinalIgnoreCase))
      {
        AddError("F18C055", "Inside A[ ... ]], the F18A opcode at 0x13 is named 'inv' (or 'not'); the former '-' spelling was removed to avoid confusion with subtraction.", token.Location);
        continue;
      }
      else if (!F18InstructionSet.Opcodes.TryGetValue(token.Text, out opcode))
      {
        int? target = null;
        if (_symbols.TryGetValue(token.Text, out F18ExportedSymbol? localSymbol) && localSymbol is not null)
        {
          target = localSymbol.Value;
        }
        else if (_externalSymbols.TryGetValue(token.Text, out F18ExportedSymbol? externalSymbol) && externalSymbol is not null)
        {
          target = externalSymbol.Value;
        }

        if (target is null)
        {
          AddError("F18C019", $"'{token.Text}' is not a primitive F18A opcode and cannot appear inside A[ ... ]].", token.Location);
          continue;
        }

        hasCall = true;
        callTarget = target.Value;
        callToken = token;
        continue;
      }

      opcodes.Add(opcode);
      if (opcodes.Count > 4)
      {
        AddError("F18C020", "A quoted F18A instruction word can contain at most four opcodes.", token.Location);
      }
    }

    if (!terminated)
    {
      AddError("F18C021", "A[ is missing its closing ]].", openingToken.Location);
      return;
    }

    int encoded;
    if (hasCall)
    {
      try
      {
        encoded = F18InstructionSet.EncodePackedControl(opcodes, 0x03, callTarget, opcodes.Count);
      }
      catch (ArgumentException exception)
      {
        AddError("F18C023", exception.Message, callToken?.Location ?? openingToken.Location);
        return;
      }
    }
    else
    {
      if (opcodes.Count == 0 || opcodes.Count > 4)
      {
        return;
      }

      if (opcodes.Count == 4 && !F18InstructionSet.IsSlot3Compatible(opcodes[3]))
      {
        AddError("F18C022", "The fourth opcode is not legal in F18A slot 3.", openingToken.Location);
        return;
      }

      try
      {
        encoded = F18InstructionSet.EncodePackedInstruction(opcodes);
      }
      catch (ArgumentException exception)
      {
        AddError("F18C023", exception.Message, openingToken.Location);
        return;
      }
    }

    if (_compileMode)
    {
      Builder.EmitLiteral(encoded, openingToken);
    }
    else
    {
      Interpreter.TryPushData(encoded, openingToken);
    }
  }

  private void CompileExplicitControl(F18Token token, byte opcode, string description)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    var target = ReadRequiredToken(token, $"{description} target");
    if (target is null)
    {
      return;
    }

    if (TryResolveAddress(target, out var address))
    {
      // A resolved target is a known (typically backward) destination, so it may
      // pack into the current word's next free slot; otherwise force-align it.
      // Packing is skipped when PackControlTransfers is disabled.
      if (!_options.PackControlTransfers ||
          !Builder.TryEmitPackedControl(opcode, address, token))
      {
        Builder.EmitControl(opcode, address, token);
      }
    }
    else if (!TryResolveValue(target, out _))
    {
      // A name may be a forward reference. A numeric/constant address that was
      // rejected by TryResolveAddress has already produced a precise diagnostic.
      EmitSymbolControl(opcode, target);
    }
  }

  private void CompileBegin(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    Builder.Align();
    // begin (-a): push the current address as a destination onto the shared stack.
    PushControlValue(Builder.CurrentAddress, token);
  }

  // ---- Unified control stack -------------------------------------------------
  // Per the F18 language (DB013 5.3.x), the compiler's control stack IS the
  // interpreter data stack. Forward transfers leave a HANDLE 'r'; loop/branch
  // openers leave a DESTINATION 'a'. Both are ordinary integer values, so 'swap'
  // (including yellow/interpret-mode '[ swap ]') reorders them naturally and
  // composite structures like 'while' = 'if swap' work by construction.
  //
  // A destination is a bare 10-bit address. A handle packs the patch address
  // (bits 0-9), the transfer opcode (bits 12-14) so 'then' can restore the exact
  // opcode (jump/call/if/-if/zif), and the slot (bits 15-16) the transfer occupies
  // in its word so 'then' patches the correct address field of a greedily-packed
  // forward branch.
  private const int HandleOpcodeShift = 12;
  private const int HandleSlotShift = 15;
  private const int HandleLiteralShift = 10;

  // A control handle packs everything PatchPackedControl needs into one 18-bit
  // stack value: the placeholder address (bits 0-9), the count of '@p' literal
  // words that follow it in the same instruction word (bits 10-11, each advances P
  // by one for reachability), the transfer opcode (bits 12-14), and the slot the
  // transfer occupies (bits 15-16).
  private static int EncodeHandle(int patchAddress, byte opcode, int slot, int literalCount = 0) =>
      (patchAddress & 0x3FF)
      | ((literalCount & 0x03) << HandleLiteralShift)
      | ((opcode & 0x07) << HandleOpcodeShift)
      | ((slot & 0x03) << HandleSlotShift);

  private static int HandleAddress(int handle) => handle & 0x3FF;
  private static int HandleLiteralCount(int handle) => (handle >> HandleLiteralShift) & 0x03;
  private static byte HandleOpcode(int handle) => (byte)((handle >> HandleOpcodeShift) & 0x07);
  private static int HandleSlot(int handle) => (handle >> HandleSlotShift) & 0x03;

  private void PushControlValue(int value, F18Token token) =>
      Interpreter.TryPushData(value, token);

  private bool TryPopControlValue(F18Token token, string need, out int value)
  {
    if (Interpreter.DataStack.Count == 0)
    {
      AddError("F18C028", $"'{token.Text}' requires {need} on the stack, but it is empty.", token.Location);
      value = 0;
      return false;
    }

    return Interpreter.TryPopData(token, out value);
  }

  // Emit a forward-transfer placeholder, packing greedily into the current word's
  // next free slot when enabled (returning that slot), else a force-aligned slot-0
  // word. Centralizes the option branch shared by if/-if/ahead/leap/else/while.
  private int EmitForwardPlaceholder(byte opcode, F18Token token, out int slot, out int literalCount)
  {
    if (_options.PackControlTransfers)
    {
      return Builder.EmitPackedControlPlaceholder(opcode, token, out slot, out literalCount);
    }

    slot = 0;
    literalCount = 0;
    return Builder.EmitControlPlaceholder(opcode, token);
  }

  // Forward transfer: emit the branch with its opcode and leave a handle 'r'.
  private void CompileForwardIf(F18Token token, byte opcode, ControlKind kind)
  {
    _ = kind; // kind is no longer used; the opcode is carried in the handle.
    if (!RequireDefinition(token))
    {
      return;
    }

    var patchAddress = EmitForwardPlaceholder(opcode, token, out int slot, out int literalCount);
    PushControlValue(EncodeHandle(patchAddress, opcode, slot, literalCount), token);
  }

  private void CompileAhead(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    var patchAddress = EmitForwardPlaceholder(0x02, token, out int slot, out int literalCount);
    PushControlValue(EncodeHandle(patchAddress, 0x02, slot, literalCount), token);
  }

  private void CompileLeap(F18Token token)
  {
    // DB013 5.3.2.1: leap compiles a CALL (0x03) to the matching then.
    if (!RequireDefinition(token))
    {
      return;
    }

    var patchAddress = EmitForwardPlaceholder(0x03, token, out int slot, out int literalCount);
    PushControlValue(EncodeHandle(patchAddress, 0x03, slot, literalCount), token);
  }

  // then (r-): resolve a forward handle to here.
  private void CompileThen(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    if (!TryPopControlValue(token, "a forward reference (r)", out int handle))
    {
      return;
    }

    Builder.Align();
    PatchForwardHandle(handle, Builder.CurrentAddress, token);
  }

  // Resolve a forward handle to 'destination'. Uses the slot-aware packed patch
  // when packing is enabled (the handle records the transfer's slot), else the
  // legacy slot-0 patch. The destination already carries any P9 bit, since it comes
  // from the location counter, so an in-region branch keeps Extended Arithmetic Mode.
  private void PatchForwardHandle(int handle, int destination, F18Token token)
  {
    if (_options.PackControlTransfers)
    {
      Builder.PatchPackedControl(
          HandleAddress(handle), HandleSlot(handle), HandleLiteralCount(handle), destination, token);
    }
    else
    {
      Builder.PatchControl(HandleAddress(handle), HandleOpcode(handle), destination, token);
    }
  }

  // else (r-r): resolve the previous handle to here and open a new forward jump.
  private void CompileElse(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    if (!TryPopControlValue(token, "a forward reference (r)", out int handle))
    {
      return;
    }

    var jumpPatch = EmitForwardPlaceholder(0x02, token, out int slot, out int literalCount);
    PatchForwardHandle(handle, Builder.CurrentAddress, token);
    PushControlValue(EncodeHandle(jumpPatch, 0x02, slot, literalCount), token);
  }

  // Backward transfer to a destination 'a' with the given opcode (until/-until/end/again).
  private void CompileBackwardBranch(F18Token token, ControlKind expected, byte opcode, string expectedName)
  {
    _ = expected;
    _ = expectedName;
    if (!RequireDefinition(token))
    {
      return;
    }

    if (!TryPopControlValue(token, "a destination (a)", out int destination))
    {
      return;
    }

    // Prefer packing the backward branch into the current word's next free slot
    // (matching the ROM); fall back to a force-aligned slot-0 control word when it
    // cannot pack or the destination is out of the narrowed field's reach. Packing
    // can be disabled (PackControlTransfers = false) for layout-stable artifacts
    // such as the runtime-compiled Kraken node-708 reply helper.
    if (!_options.PackControlTransfers ||
        !Builder.TryEmitPackedControl(opcode, destination & 0x3FF, token))
    {
      Builder.EmitControl(opcode, destination & 0x3FF, token);
    }
  }

  private void CompileUnext(F18Token token)
  {
    // DB013 5.3.2.2: unext (micronext) loops within one instruction word using R,
    // with no destination address (opcode 0x04). It consumes the loop's
    // destination 'a' left by 'for' or 'begin'.
    if (!RequireDefinition(token))
    {
      return;
    }

    if (!TryPopControlValue(token, "a loop destination (a)", out _))
    {
      return;
    }

    Builder.EmitPrimitive(0x04, token);
  }

  private void CompileStarNext(F18Token token)
  {
    // DB013 5.3.2.3: *next (a x - x) == 'swap next'. Bring the loop destination to
    // the top past one intervening value, then emit next.
    if (!RequireDefinition(token))
    {
      return;
    }

    if (Interpreter.DataStack.Count < 2)
    {
      AddError("F18C028", "'*next' requires a destination and one value (a x) on the stack.", token.Location);
      return;
    }

    Interpreter.TryPopData(token, out int x);
    Interpreter.TryPopData(token, out int destination);
    PushControlValue(x, token);

    // Like ordinary 'next', pack the transfer into the current word's next free
    // slot when the destination is reachable there (the ROM packs 'drop r> *next'
    // into a single word); fall back to a force-aligned slot-0 word otherwise.
    if (!_options.PackControlTransfers ||
        !Builder.TryEmitPackedControl(0x05, destination & 0x3FF, token))
    {
      Builder.EmitControl(0x05, destination & 0x3FF, token);
    }
  }

  private void CompileFor(F18Token token)
  {
    // for (-a): push count-load primitive, align, and leave the loop destination.
    if (!RequireDefinition(token))
    {
      return;
    }

    Builder.EmitPrimitive(0x1D, token);
    Builder.Align();
    PushControlValue(Builder.CurrentAddress, token);
  }

  // next (a-): backward transfer to the loop destination.
  private void CompileNext(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    if (!TryPopControlValue(token, "a loop destination (a)", out int destination))
    {
      return;
    }

    // 'next' (0x05) is a jump-class transfer. Pack it into the current word's next
    // free slot when the loop destination is reachable there (ROM: 'for @+ next'),
    // otherwise emit a force-aligned slot-0 control word. Packing is skipped when
    // PackControlTransfers is disabled.
    if (!_options.PackControlTransfers ||
        !Builder.TryEmitPackedControl(0x05, destination & 0x3FF, token))
    {
      Builder.EmitControl(0x05, destination & 0x3FF, token);
    }
  }

  // while (x - r x) == 'if swap'; -while (x - r x) == '-if swap'.
  private void CompileWhile(F18Token token, byte opcode, ControlKind kind)
  {
    _ = kind;
    if (!RequireDefinition(token))
    {
      return;
    }

    // 'if': emit the conditional forward branch and produce a handle.
    var patchAddress = EmitForwardPlaceholder(opcode, token, out int slot, out int literalCount);
    int handle = EncodeHandle(patchAddress, opcode, slot, literalCount);

    // 'swap': exchange the new handle with the value beneath (the begin
    // destination), leaving ( ... r x ) so repeat/again resolves them correctly.
    if (Interpreter.DataStack.Count >= 1)
    {
      Interpreter.TryPopData(token, out int beneath);
      PushControlValue(handle, token);
      PushControlValue(beneath, token);
    }
    else
    {
      // Nothing beneath to swap with; just leave the handle.
      PushControlValue(handle, token);
    }
  }

  // repeat: ( r a - ) close a begin..while loop: jump back to 'a', resolve 'r' here.
  private void CompileRepeat(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    if (Interpreter.DataStack.Count < 2)
    {
      AddError("F18C026", "repeat requires begin ... while (or -while): a handle and destination (r a) must be on the stack.", token.Location);
      return;
    }

    Interpreter.TryPopData(token, out int destination);
    Interpreter.TryPopData(token, out int handle);
    if (!_options.PackControlTransfers ||
        !Builder.TryEmitPackedControl(0x02, destination & 0x3FF, token))
    {
      Builder.EmitControl(0x02, destination & 0x3FF, token);
    }

    Builder.Align();
    PatchForwardHandle(handle, Builder.CurrentAddress, token);
  }

  private void CompileRecurse(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    if (_currentDefinition is null || !_symbols.TryGetValue(_currentDefinition, out var symbol))
    {
      AddError("F18C027", "recurse is not inside a valid named definition.", token.Location);
      return;
    }

    EmitKnownControl(0x03, symbol.Value, token);
  }

  private bool DefineSymbol(F18Token token, int address, F18ExportKind kind)
  {
    if (NameExists(token.Text) && !TryShadowImportedName(token.Text, token))
    {
      AddError("F18C029", $"The name '{token.Text}' is already defined or reserved.", token.Location);
      return false;
    }

    _symbols[token.Text] = new F18ExportedSymbol(
        token.Text,
        address,
        kind,
        _options.NodeCoordinate,
        _options.MemorySpace);
    return true;
  }

  // A name bound only by an 'import' may be redefined locally: the local
  // dictionary always outranks a name borrowed from another node, so this is a
  // WARNING (reported, then ignored), not an error. Any other collision -- an
  // existing local definition, a constant, a target opcode, or a reserved
  // compiler word -- stays an error, because silently rebinding one of those
  // would change the meaning of code already compiled against it.
  //
  // The stale import binding is REMOVED rather than merely shadowed. Several
  // lookups consult _externalSymbols on a miss whose criteria differ from
  // _symbols' (TryResolveValue, for one, accepts a local symbol only when it is
  // Label-kind but an external one whenever it is not Word-kind), so leaving both
  // entries in place would let the import still win in those paths.
  private bool TryShadowImportedName(string name, F18Token token)
  {
    if (!IsImportedNameOnly(name))
    {
      return false;
    }

    F18ExportedSymbol imported = _externalSymbols[name];
    AddWarning(
        "F18C050",
        $"'{name}' redefines the name imported from node {imported.NodeCoordinate:000} (0x{imported.Value:X3}). " +
        "The local definition is used from here on; the imported one is no longer reachable by this name.",
        token.Location);
    _externalSymbols.Remove(name);
    return true;
  }

  // True when 'name' is currently bound ONLY by an imported/external symbol.
  private bool IsImportedNameOnly(string name) =>
      _externalSymbols.ContainsKey(name) &&
      !_symbols.ContainsKey(name) &&
      !_constants.ContainsKey(name) &&
      !F18InstructionSet.Opcodes.ContainsKey(name) &&
      !ReservedCompilerWords.Contains(name);

  private bool NameExists(string name) =>
      _symbols.ContainsKey(name) ||
      _externalSymbols.ContainsKey(name) ||
      _constants.ContainsKey(name) ||
      F18InstructionSet.Opcodes.ContainsKey(name) ||
      ReservedCompilerWords.Contains(name);

  // Emit a transfer to a KNOWN address (a call to an already-defined word, an
  // external symbol, or recurse). Packs into the current word's next free slot
  // when enabled and the destination is reachable through that slot's narrowed
  // field (matching the chip ROM, e.g. 'a call'), otherwise force-aligns into a
  // slot-0 word. TryEmitPackedControl's reachability check makes this safe for
  // cross-node/external addresses: an out-of-reach target simply force-aligns.
  private void EmitKnownControl(byte opcode, int destination, F18Token token)
  {
    if (!_options.PackControlTransfers ||
        !Builder.TryEmitPackedControl(opcode, destination & 0x3FF, token))
    {
      Builder.EmitControl(opcode, destination & 0x3FF, token);
    }
  }

  private void EmitSymbolControl(byte opcode, F18Token target)
  {
    var memoryAddress = Builder.EmitControlPlaceholder(opcode, target);
    _symbolRelocations.Add(new SymbolRelocation(memoryAddress, opcode, target.Text, target));
  }

  private void ResolveSymbolRelocations()
  {
    foreach (var relocation in _symbolRelocations)
    {
      if (_symbols.TryGetValue(relocation.Symbol, out var localSymbol) &&
          localSymbol.Kind == F18ExportKind.Word)
      {
        Builder.PatchControl(relocation.MemoryAddress, relocation.Opcode, localSymbol.Value, relocation.Token);
        continue;
      }

      if (_externalSymbols.TryGetValue(relocation.Symbol, out var externalSymbol) &&
          externalSymbol.Kind == F18ExportKind.Word)
      {
        Builder.PatchControl(relocation.MemoryAddress, relocation.Opcode, externalSymbol.Value, relocation.Token);
        continue;
      }

      AddError("F18C030", $"Unknown callable word '{relocation.Symbol}'. Imports must precede their first use; labels are numeric values, not implicit calls.", relocation.Token.Location);
    }
  }

  private int? ResolveEntryPoint()
  {
    if (_entryToken is null)
    {
      return _firstDefinitionAddress;
    }

    if (TryResolveAddress(_entryToken, out var address))
    {
      return address;
    }

    if (!TryResolveValue(_entryToken, out _))
    {
      AddError("F18C031", $"Unknown entry point '{_entryToken.Text}'.", _entryToken.Location);
    }

    return _firstDefinitionAddress;
  }

  private bool TryResolveAddress(F18Token token, out int value)
  {
    if (_symbols.TryGetValue(token.Text, out var localSymbol))
    {
      value = localSymbol.Value;
      return true;
    }

    if (_externalSymbols.TryGetValue(token.Text, out var externalSymbol))
    {
      value = externalSymbol.Value;
      return true;
    }

    if (TryResolveValue(token, out value))
    {
      if (value <= 0x3FF)
      {
        return true;
      }

      AddError("F18C032", "A control-transfer address must fit in the ten-bit P register.", token.Location);
    }

    value = 0;
    return false;
  }

  private bool TryResolveValue(F18Token token, out int value)
  {
    if (_constants.TryGetValue(token.Text, out value))
    {
      return true;
    }

    if (_symbols.TryGetValue(token.Text, out var localSymbol) && localSymbol.Kind == F18ExportKind.Label)
    {
      value = localSymbol.Value;
      return true;
    }

    if (_externalSymbols.TryGetValue(token.Text, out var externalSymbol) &&
        externalSymbol.Kind != F18ExportKind.Word)
    {
      value = externalSymbol.Value;
      return true;
    }

    return TryParseNumber(token.Text, out value);
  }

  private static bool TryParseNumber(string text, out int value)
  {
    value = 0;
    if (string.IsNullOrWhiteSpace(text))
    {
      return false;
    }

    var normalized = text.Replace("_", string.Empty, StringComparison.Ordinal);

    // A dot marks a double-precision number (e.g. '16.', '1.0'). We use 32-bit
    // values, so the dot carries no extra magnitude here and is simply removed.
    // (Only genuine numeric text should reach this point; a lone '.' or a word
    // containing a dot that is not otherwise numeric will fail conversion below.)
    if (normalized.Contains('.', StringComparison.Ordinal))
    {
      normalized = normalized.Replace(".", string.Empty, StringComparison.Ordinal);
      if (normalized.Length == 0)
      {
        return false;
      }
    }

    var sign = 1;
    if (normalized.StartsWith('+'))
    {
      normalized = normalized[1..];
    }
    else if (normalized.StartsWith('-'))
    {
      sign = -1;
      normalized = normalized[1..];
    }

    var numberBase = 10;
    if (normalized.StartsWith('x') || normalized.StartsWith('X'))
    {
      // GreenArrays convention (arrayForth, DB014 3.3.2): a leading lowercase 'x'
      // means the rest is hexadecimal, regardless of the current radix. We accept
      // upper-case 'X' too for convenience.
      numberBase = 16;
      normalized = normalized[1..];
    }
    else if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      numberBase = 16;
      normalized = normalized[2..];
    }
    else if (normalized.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
    {
      numberBase = 2;
      normalized = normalized[2..];
    }
    else if (normalized.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
    {
      numberBase = 8;
      normalized = normalized[2..];
    }

    if (normalized.Length == 0)
    {
      return false;
    }

    long magnitude;
    try
    {
      magnitude = Convert.ToInt64(normalized, numberBase);
    }
    catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
    {
      return false;
    }

    var signed = magnitude * sign;
    if (signed is < -0x20000 or > F18InstructionSet.WordMask)
    {
      return false;
    }

    value = (int)signed & F18InstructionSet.WordMask;
    return true;
  }

  private F18Token? ReadRequiredToken(F18Token directive, string description)
  {
    if (_tokenIndex < _tokens.Count)
    {
      return _tokens[_tokenIndex++];
    }

    AddError("F18C033", $"'{directive.Text}' requires a {description}.", directive.Location);
    return null;
  }

  private bool Peek(string text) =>
      _tokenIndex < _tokens.Count && _tokens[_tokenIndex].Text.Equals(text, StringComparison.OrdinalIgnoreCase);

  // Target-compilation words (if/-if/then/exit/...) are valid whenever the compiler
  // is compiling. They do NOT require being between a specific ':' and ';': the F18
  // compiler compiles continuously, so 'then' after a mid-routine ';' is valid. The
  // only invalid context is inside a '[' interpretation section.
  private bool RequireDefinition(F18Token token)
  {
    if (_compileMode)
    {
      return true;
    }

    AddError(
        "F18C053",
        $"'{token.Text}' is a target-compilation word and cannot be used inside a '[' interpretation section. Use ']' first.",
        token.Location);
    return false;
  }

  private MemoryBuilder Builder => _builder ?? throw new InvalidOperationException("Compiler memory builder is not initialized.");

  private F18SourceLocation LastLocation() =>
      _tokens.Count == 0 ? new F18SourceLocation(1, 1) : _tokens[^1].Location;

  private void AddDiagnostic(F18Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

  private void AddError(string code, string message, F18SourceLocation location) =>
      _diagnostics.Add(new F18Diagnostic(F18DiagnosticSeverity.Error, code, message, location));

  private void AddWarning(string code, string message, F18SourceLocation location) =>
      _diagnostics.Add(new F18Diagnostic(F18DiagnosticSeverity.Warning, code, message, location));

  private void AddInfo(string code, string message, F18SourceLocation location) =>
      _diagnostics.Add(new F18Diagnostic(F18DiagnosticSeverity.Info, code, message, location));

  private sealed record SymbolRelocation(int MemoryAddress, byte Opcode, string Symbol, F18Token Token);

  private enum ControlKind
  {
    Begin,
    If,
    MinusIf,
    Else,
    Ahead,
    For,
    While,
    MinusWhile
  }

  private sealed class MemoryBuilder
  {
    private readonly int?[] _memory;
    private readonly Action<F18Diagnostic> _report;
    private readonly List<byte> _slots = [];
    private readonly List<(int Value, F18Token Token)> _pendingData = [];
    private readonly int _baseAddress;
    private readonly string _memoryName;
    private F18Token? _instructionToken;
    private int _cursor;

    public MemoryBuilder(
        Action<F18Diagnostic> report,
        int baseAddress,
        int wordCount,
        string memoryName)
    {
      if (wordCount <= 0)
      {
        throw new ArgumentOutOfRangeException(nameof(wordCount));
      }

      _report = report;
      _baseAddress = baseAddress;
      _memoryName = memoryName;
      _memory = new int?[wordCount];
      _cursor = baseAddress;
    }

    public int CurrentAddress
    {
      get
      {
        if (_slots.Count != 0)
        {
          throw new InvalidOperationException("CurrentAddress requires aligned instruction output.");
        }

        return _cursor;
      }
    }

    public int UsedWordCount => _memory.Count(word => word.HasValue);

    /// <summary>
    /// The address of the word currently being assembled -- unlike
    /// <see cref="CurrentAddress"/>, this is always readable, whether or not a
    /// partial word (pending slots) is in progress. Purely observational: reading
    /// it never flushes, pads, or otherwise changes anything that gets compiled.
    /// </summary>
    public int PendingAddress => _cursor;

    // '+cy'/'-cy' (DB002 3.2): Extended Arithmetic Mode is selected by bit P9 of the
    // running address, so the assembler simply sets or clears P9 on the location
    // counter. Every address captured while it is set (labels, markers, forward and
    // backward references) then carries P9, and every transfer to such an address
    // keeps EAM active -- no separate mode flag is needed. Flush first so the change
    // applies to the next word, not a partially filled one.
    public void SetExtendedArithmetic(bool enabled)
    {
      Align();
      _cursor = enabled
          ? _cursor | F18InstructionSet.ExtendedArithmeticBit
          : _cursor & ~F18InstructionSet.ExtendedArithmeticBit;
    }

    public void SetOrigin(int address, F18Token token)
    {
      Align();
      // The origin may land anywhere in the space or its mirror (DB001 Figure 2);
      // the mirror maps onto the same 64 physical words. P9 is not part of decoding,
      // so mask it out for the boundary check while preserving it in the cursor.
      var decoded = address & ~F18InstructionSet.ExtendedArithmeticBit;
      if (decoded < _baseAddress || decoded >= _baseAddress + _memory.Length * 2)
      {
        ReportError(
            "F18M001",
            $"{_memoryName} origin must be between 0x{_baseAddress:X3} and 0x{_baseAddress + _memory.Length * 2 - 1:X3}.",
            token);
        return;
      }

      _cursor = address;
    }

    public void EmitPrimitive(byte opcode, F18Token token)
    {
      if (_slots.Count == 4 || (_slots.Count == 3 && !F18InstructionSet.IsSlot3Compatible(opcode)))
      {
        FlushInstruction();
      }

      _instructionToken ??= token;
      _slots.Add(opcode);
      if (_slots.Count == 4 || opcode is 0x00 or 0x01)
      {
        FlushInstruction();
      }
    }

    public void EmitLiteral(int value, F18Token token)
    {
      EmitPrimitive(0x08, token);
      _pendingData.Add((value & F18InstructionSet.WordMask, token));

      if (_slots.Count == 0 && _pendingData.Count > 0)
      {
        FlushPendingData();
      }
    }

    public void EmitRaw(int value, F18Token token)
    {
      Align();
      WriteWord(value, token);
    }

    public void EmitControl(byte opcode, int destination, F18Token token)
    {
      Align();
      WriteWord(F18InstructionSet.EncodeSlot0Control(opcode, destination), token);
    }

    // Try to pack a control transfer with a KNOWN destination (a backward branch:
    // next/again/until/-until) into the next free slot of the current instruction
    // word rather than force-aligning it into its own slot-0 word. This matches the
    // ROM, which packs e.g. 'for @+ next' into a single word. Returns true and emits
    // the completed word when the transfer fits slot 1 or 2 and the destination is
    // reachable through that slot's narrowed address field; returns false (emitting
    // nothing) when the current word offers no usable transfer slot or the
    // destination is out of reach, so the caller can fall back to EmitControl.
    public bool TryEmitPackedControl(byte opcode, int destination, F18Token token)
    {
      // The transfer would land in slot _slots.Count. Slots 1 and 2 can hold a
      // transfer; slot 0 is the ordinary aligned case (handled by EmitControl) and
      // slot 3 can never hold a transfer, so there must be one or two slots pending.
      var slot = _slots.Count;
      if (slot is not (1 or 2))
      {
        return false;
      }

      // P after this word is the wrapped next address (the F18A P register wraps at
      // the mirror boundary, e.g. 0x0FF -> 0x080, not 0x100), then advances once
      // more past each inline '@p' literal this word will consume (written next by
      // FlushPendingData). Wrapping matters for a transfer in the last word of the
      // space: its reachability base is 0x080, not 0x100.
      var nextP = NextAddress(_cursor);
      for (var i = 0; i < _pendingData.Count; i++)
      {
        nextP = NextAddress(nextP);
      }

      if (!F18InstructionSet.ControlFitsSlot(slot, nextP, destination, _memory.Length))
      {
        return false;
      }

      int encoded;
      try
      {
        // ControlFitsSlot has confirmed the destination's high bits match the next
        // word, so only the low field bits are stored; EncodePackedControl re-checks.
        encoded = F18InstructionSet.EncodePackedControl(_slots, opcode, destination, slot);
      }
      catch (ArgumentException)
      {
        return false;
      }

      var instructionToken = GetInstructionToken();
      WriteWord(encoded, instructionToken);
      _slots.Clear();
      _instructionToken = null;
      FlushPendingData();
      return true;
    }

    public int EmitControlPlaceholder(byte opcode, F18Token token)
    {
      Align();
      var address = _cursor;
      WriteWord(F18InstructionSet.EncodeSlot0Control(opcode, 0), token);
      return address;
    }

    // Greedy forward placeholder: place the transfer opcode into the current word's
    // next free slot (0, 1, or 2) with a zero address field, flush the word, and
    // return the slot it occupies via 'slot'. The destination is filled in later by
    // PatchPackedControl once the target is known. This matches the chip ROM, which
    // packs forward transfers into whatever slot is free rather than force-aligning
    // them. Slot 3 cannot hold a transfer, so a full 3-slot word is flushed first.
    public int EmitPackedControlPlaceholder(byte opcode, F18Token token, out int slot, out int literalCount)
    {
      if (_slots.Count == 3)
      {
        FlushInstruction();
      }

      slot = _slots.Count;
      // '@p' literals in this same word (pending flush) each advance P by one, so
      // record how many follow the transfer for the reachability check at patch time.
      literalCount = _pendingData.Count;
      var leading = _slots.ToArray();

      int encoded;
      try
      {
        encoded = F18InstructionSet.EncodePackedControl(leading, opcode, 0, slot);
      }
      catch (ArgumentException exception)
      {
        // Should not happen: slot is 0..2 and leading exactly fills the lower slots.
        ReportError("F18M004", exception.Message, token);
        Align();
        var fallback = _cursor;
        WriteWord(F18InstructionSet.EncodeSlot0Control(opcode, 0), token);
        slot = 0;
        return fallback;
      }

      var instructionToken = GetInstructionToken();
      var address = _cursor;
      WriteWord(encoded, instructionToken);
      _slots.Clear();
      _instructionToken = null;
      FlushPendingData();
      return address;
    }

    public void PatchControl(int memoryAddress, byte opcode, int destination, F18Token token)
    {
      var index = ToPhysicalIndex(memoryAddress);
      if (index < 0 || !_memory[index].HasValue)
      {
        ReportError(
            "F18M002",
            $"Cannot patch control transfer at {_memoryName} address 0x{memoryAddress:X3}.",
            token);
        return;
      }

      try
      {
        _memory[index] = F18InstructionSet.EncodeSlot0Control(opcode, destination);
      }
      catch (ArgumentOutOfRangeException exception)
      {
        ReportError("F18M003", exception.Message, token);
      }
    }

    // Patch a greedily-packed forward transfer at (memoryAddress, slot). The
    // placeholder was flushed with a zero RAW address field, but the stored word is
    // XOR-encoded (raw ^ EncodingXor), so its field bits hold EncodingXor's low bits
    // -- NOT zero. To set the destination we must clear the field bits and write the
    // encoded field, which is (destination ^ EncodingXor) masked to the slot width.
    // (An earlier version XORed the field straight in, which is wrong precisely
    // because the empty word is 0x15555, not 0.) Reachability is checked first: a
    // slot 1/2 transfer only reaches a destination whose high bits match the
    // following word. If the target is out of reach, this reports F18M005 telling
    // the user to align the source manually, per the greedy-pack policy.
    public void PatchPackedControl(int memoryAddress, int slot, int literalCount, int destination, F18Token token)
    {
      var index = ToPhysicalIndex(memoryAddress);
      if (index < 0 || !_memory[index].HasValue)
      {
        ReportError(
            "F18M002",
            $"Cannot patch control transfer at {_memoryName} address 0x{memoryAddress:X3}.",
            token);
        return;
      }

      // P after this word is the wrapped next address (the P register wraps at the
      // mirror boundary, e.g. 0x0FF -> 0x080), advanced once more past each '@p'
      // literal in the same word. Reachability is relative to that, not
      // memoryAddress + 1 + literalCount.
      var nextP = NextAddress(memoryAddress);
      for (var i = 0; i < literalCount; i++)
      {
        nextP = NextAddress(nextP);
      }

      if (!F18InstructionSet.ControlFitsSlot(slot, nextP, destination, _memory.Length))
      {
        int width = F18InstructionSet.AddressFieldWidth(slot);
        ReportError(
            "F18M005",
            $"Forward transfer at {_memoryName} address 0x{memoryAddress:X3} slot {slot} cannot reach " +
            $"destination 0x{destination:X3}: the {width}-bit slot field does not span it. Align the " +
            "source so the branch lands in a wider slot (slot 0 reaches any address in the node).",
            token);
        return;
      }

      // The address field is stored UNENCODED (see EncodePackedControl): only the
      // opcode slots are XOR-encoded, the low 'width' bits hold the raw destination.
      // The placeholder was written with a zero field, so clear the field region and
      // OR in the raw destination bits. (Earlier versions XORed the field in, or
      // XORed the destination with EncodingXor -- both corrupted the field, since the
      // field is not part of the XOR-encoded region at all.)
      var mask = (1 << F18InstructionSet.AddressFieldWidth(slot)) - 1;
      var patched = (_memory[index]!.Value & ~mask) | (destination & mask);
      _memory[index] = patched & F18InstructionSet.WordMask;
    }

    public void Align()
    {
      FlushInstruction();
      FlushPendingData();
    }

    public IReadOnlyList<int> CreateImage(int? unwrittenFillWord = null)
    {
      // The image always spans the full memory (64 words for ROM/RAM). A word never
      // written by the compiled source defaults to the F18A empty-word value
      // 0x15555 (an all-zero instruction word XOR-encodes to 0x15555, which is what
      // the compiler pre-fills slots with and what RAM -- and most never-written ROM
      // words -- read back as on silicon). This makes a compiled image directly
      // comparable, word-for-word, with the ROM/RAM read from silicon.
      //
      // ROM is the one exception: confirmed against two different nodes (705 at
      // 0x081, 300 at 0xA0 -- different addresses because each node's macro set
      // leaves a different single word unclaimed), any word a node's ROM program
      // never claims reads back on silicon as a CALL to 'warm', not as 0x15555. This
      // matches the factory ROM's own boot safety net: an untouched cell retreats to
      // the multiport-wait loop instead of falling through as a spurious ';' chain.
      // 'unwrittenFillWord' carries that pre-encoded CALL word (or null when 'warm'
      // isn't resolvable, e.g. RAM compiles or a ROM compile that omits rom_warm),
      // so only cells the source left genuinely untouched get it -- any word the
      // source actually wrote (even to a bit-identical 0x15555, e.g. an all-';'
      // terminated word) keeps its real compiled value.
      var fill = unwrittenFillWord ?? F18InstructionSet.EncodingXor;
      var result = new int[_memory.Length];
      for (var index = 0; index < _memory.Length; index++)
      {
        result[index] = _memory[index] ?? fill;
      }

      return result;
    }

    private void FlushInstruction()
    {
      if (_slots.Count == 0)
      {
        return;
      }

      int encoded;
      try
      {
        encoded = F18InstructionSet.EncodePackedInstruction(_slots);
      }
      catch (ArgumentException exception)
      {
        var token = GetInstructionToken();
        ReportError("F18M004", exception.Message, token);
        _slots.Clear();
        _pendingData.Clear();
        _instructionToken = null;
        return;
      }

      var instructionToken = GetInstructionToken();
      WriteWord(encoded, instructionToken);
      _slots.Clear();
      _instructionToken = null;
      FlushPendingData();
    }

    private F18Token GetInstructionToken() =>
        _instructionToken ?? (_pendingData.Count > 0
            ? _pendingData[0].Token
            : new F18Token(string.Empty, 1, 1, 0, 0));

    private void FlushPendingData()
    {
      foreach (var (value, token) in _pendingData)
      {
        WriteWord(value, token);
      }

      _pendingData.Clear();
    }

    // Map a decoded address to a physical word index. Per DB001 Figure 2 the 64
    // words repeat once within the space: RAM x000-x03F is mirrored at x040-x07F,
    // ROM x080-x0BF at x0C0-x0FF, and incrementing wraps (x07F->x000, and the
    // equivalent in ROM). So an address in the base region or its mirror maps into
    // the 64-word array modulo the word count; only an address outside the whole
    // space (e.g. I/O at x100+) is genuinely out of range. Returns -1 if unmapped.
    // Bit P9 (0x200) is not part of memory decoding (DB001 2.2): it only selects
    // Extended Arithmetic Mode. It rides along in the location counter and in every
    // address value so that transfers carry it, but it must be masked out before any
    // physical-memory calculation or boundary check. P8 is left intact -- it does
    // participate in decoding (it selects the I/O region), unlike P9.
    private int ToPhysicalIndex(int address)
    {
      var offset = (address & ~F18InstructionSet.ExtendedArithmeticBit) - _baseAddress;
      if (offset < 0 || offset >= _memory.Length * 2)
      {
        return -1;
      }

      return offset % _memory.Length;
    }

    // The next logical address after 'address', wrapping within the memory space
    // and its mirror (DB001 Figure 2): the address runs baseAddress ..
    // baseAddress + 2*wordCount - 1 and then wraps back to baseAddress (e.g. ROM
    // 0x0FF -> 0x080), never running into I/O space. P9 is not part of decoding and
    // is preserved. This is exactly how the F18A P register increments, so it is
    // used both to advance the location counter and to compute the P value a
    // transfer sees (its reachability base).
    private int NextAddress(int address)
    {
      var p9 = address & F18InstructionSet.ExtendedArithmeticBit;
      var decoded = address & ~F18InstructionSet.ExtendedArithmeticBit;
      var span = _memory.Length * 2;
      var offset = decoded - _baseAddress;
      offset = offset < 0 || offset >= span - 1 ? 0 : offset + 1;
      return (_baseAddress + offset) | p9;
    }

    private void AdvanceCursor()
    {
      _cursor = NextAddress(_cursor);
    }

    private void WriteWord(int value, F18Token token)
    {
      var index = ToPhysicalIndex(_cursor);
      if (index < 0)
      {
        ReportError(
            "F18M005",
            $"Compiled output at 0x{_cursor:X3} is outside the node's {_memoryName} space " +
            $"(0x{_baseAddress:X3}-0x{_baseAddress + _memory.Length * 2 - 1:X3}).",
            token);
        AdvanceCursor();
        return;
      }

      if (_memory[index].HasValue)
      {
        ReportError(
            "F18M006",
            $"{_memoryName} address 0x{_cursor:X3} is written more than once.",
            token);
        AdvanceCursor();
        return;
      }

      _memory[index] = value & F18InstructionSet.WordMask;
      AdvanceCursor();
    }

    private void ReportError(string code, string message, F18Token token) =>
        _report(new F18Diagnostic(F18DiagnosticSeverity.Error, code, message, token.Location));
  }

}