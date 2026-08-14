namespace Ga144.Evb.Ide.Compiler;

public sealed class F18Compiler
{
  private static readonly HashSet<string> ReservedCompilerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ":", ";", "[", "]", "org", "entry", "const", "constant", "label", "data", "word", ".word", ",",
        "align", "..", "lit", "literal", "A[", "]]", "call", "jump", "jmp",
        "branch-if", "branch--if", "branch-next", "begin", "again", "until", "-until",
        "if", "-if", "zif", "else", "then", "ahead", "leap", "for", "next", "unext", "while", "-while",
        "repeat", "recurse", "exit", "import", "swap", "here", "end", "*next",
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

    if (_inDefinition)
    {
      if (!_compileMode)
      {
        AddError("F18C043", "The definition entered interpretation with '[' but did not resume compilation with ']'.", LastLocation());
      }

      // GreenArrays F18 style: a trailing ':' definition need not end with ';'.
      // An unterminated final definition simply falls through to the end of the
      // dictionary, so this is not an error. Any open control structures are
      // still reported below.
    }

    Builder.Align();
    ResolveSymbolRelocations();

    int? entryPoint = ResolveEntryPoint();
    if (_firstDefinitionAddress is null && !_diagnostics.Any(item => item.Severity == F18DiagnosticSeverity.Error))
    {
      AddWarning("F18C003", "The source contains no word definitions.", new F18SourceLocation(1, 1));
    }

    return new F18CompileResult
    {
      Words = Builder.CreateImage(),
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

    foreach (KeyValuePair<string, int> pair in _options.PredefinedConstants)
    {
      _constants[pair.Key] = pair.Value & F18InstructionSet.WordMask;
    }

    foreach (KeyValuePair<string, F18ExportedSymbol> pair in _options.PredefinedSymbols)
    {
      _externalSymbols[pair.Key] = pair.Value;
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
    _compileMode = false;
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
      case "import":
        InterpretNodeImport(token);
        return;
      case "label":
        CompileLabel(token);
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
      case "align":
      case "..":
        Builder.Align();
        return;
      case "lit":
        CompilePrefixLiteral(token);
        return;
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
      EmitKnownSymbol(localSymbol, token);
      return;
    }

    if (_externalSymbols.TryGetValue(token.Text, out F18ExportedSymbol? externalSymbol) && externalSymbol is not null)
    {
      EmitKnownSymbol(externalSymbol, token);
      return;
    }

    EmitSymbolControl(0x03, token);
  }

  private void BeginInterpretation(F18Token token)
  {
    if (!_inDefinition)
    {
      // The compiler is already interpreting at module level. Accepting '['
      // here makes generated source fragments composable without changing state.
      _compileMode = false;
      return;
    }

    if (!_compileMode)
    {
      AddError("F18C049", "Nested '[' is not allowed; the compiler is already interpreting.", token.Location);
      return;
    }

    _compileMode = false;
  }

  private void ResumeCompilation(F18Token token)
  {
    if (!_inDefinition)
    {
      AddError("F18C050", "']' can resume compilation only inside a ':' definition.", token.Location);
      return;
    }

    if (_compileMode)
    {
      AddError("F18C051", "Unexpected ']'; target compilation is already active.", token.Location);
      return;
    }

    _compileMode = true;
  }

  private void InterpretOrigin(F18Token token)
  {
    if (_inDefinition)
    {
      AddError("F18C006", "org is a module-level FORTH word and cannot be used inside a definition.", token.Location);
      return;
    }

    if (Interpreter.TryPopData(token, out int value))
    {
      SetOrigin(value, token);
    }
  }

  private void InterpretConstant(F18Token token)
  {
    if (_inDefinition)
    {
      AddError("F18C014", "Compile-time constants must be declared outside word definitions.", token.Location);
      return;
    }

    F18Token? name = ReadRequiredToken(token, "constant name");
    if (name is null || !Interpreter.TryPopData(token, out int value))
    {
      return;
    }

    if (NameExists(name.Text))
    {
      AddError("F18C016", $"The name '{name.Text}' is already defined or reserved.", name.Location);
      return;
    }

    value &= F18InstructionSet.WordMask;
    _constants[name.Text] = value;
    _userConstants[name.Text] = value;
  }

  private void InterpretNodeImport(F18Token token)
  {
    if (_inDefinition)
    {
      AddError("F18C036", "import is a module-level directive and cannot appear inside a definition.", token.Location);
      return;
    }

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
      AddError("F18C042", $"Import from node {sourceNode:000} conflicts with existing name '{name}'.", token.Location);
      return false;
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

  private bool IsInterpreting => !_inDefinition || !_compileMode;

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
    if (_inDefinition)
    {
      // GreenArrays F18 style: a ':' definition need not be terminated by ';'.
      // A following ':' simply sets a new label; execution falls through into it
      // (the boot-ROM 'relay'/'done' idiom). Pending control values on the shared
      // stack intentionally CARRY ACROSS the ':' — e.g. a 'then' after ': done'
      // resolves a handle left before the label — so they are not cleared here.
      if (!_compileMode)
      {
        // An open '[' interpretation must be closed with ']' first.
        AddError("F18C052", "The definition cannot end while '[' interpretation is active; insert ']' before starting a new definition.", token.Location);
        _compileMode = true;
      }

      _inDefinition = false;
      _currentDefinition = null;
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
    _compileMode = true;
    _currentDefinition = name.Text;
    _firstDefinitionAddress ??= Builder.CurrentAddress;
  }

  private void EndDefinition(F18Token token)
  {
    if (!_inDefinition)
    {
      AddError("F18C008", "Unexpected ';' outside a word definition.", token.Location);
      return;
    }

    if (!_compileMode)
    {
      AddError("F18C052", "The definition cannot end while '[' interpretation is active; insert ']' before ';'.", token.Location);
      _compileMode = true;
    }

    Builder.EmitPrimitive(0x00, token);
    _inDefinition = false;
    _compileMode = false;
    _currentDefinition = null;
  }

  private void SetOrigin(int value, F18Token token)
  {
    var first = _options.MemoryBaseAddress;
    var last = first + _options.MemoryWordCount - 1;
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

  private void CompilePrefixLiteral(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    // Preserve the earlier convenient prefix form: lit <value>. It never
    // consumes a possibly unrelated value left on the interpreter stack.
    F18Token? valueToken = ReadRequiredToken(token, "literal value");
    if (valueToken is null || !TryResolveInterpretValue(valueToken, out int value))
    {
      if (valueToken is not null)
      {
        AddError("F18C018", $"'{valueToken.Text}' is not a numeric value, constant, label, or word address.", valueToken.Location);
      }

      return;
    }

    Builder.EmitLiteral(value, valueToken);
  }

  private void CompileQuotedInstruction(F18Token openingToken)
  {
    var opcodes = new List<byte>();
    var terminated = false;

    while (_tokenIndex < _tokens.Count)
    {
      var token = _tokens[_tokenIndex++];
      if (token.Text == "]]")
      {
        terminated = true;
        break;
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
        AddError("F18C019", $"'{token.Text}' is not a primitive F18A opcode and cannot appear inside A[ ... ]].", token.Location);
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

    if (opcodes.Count == 0 || opcodes.Count > 4)
    {
      return;
    }

    if (opcodes.Count == 4 && !F18InstructionSet.IsSlot3Compatible(opcodes[3]))
    {
      AddError("F18C022", "The fourth opcode is not legal in F18A slot 3.", openingToken.Location);
      return;
    }

    int encoded;
    try
    {
      encoded = F18InstructionSet.EncodePackedInstruction(opcodes);
    }
    catch (ArgumentException exception)
    {
      AddError("F18C023", exception.Message, openingToken.Location);
      return;
    }

    if (_inDefinition && _compileMode)
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
      // Packing is skipped when PackBackwardBranches is disabled.
      if (!_options.PackBackwardBranches ||
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
  // (bits 0-9) with the slot-0 transfer opcode (bits 12-14) so 'then' can restore
  // the exact opcode (jump/call/if/-if/zif) when it fills in the destination.
  private const int HandleOpcodeShift = 12;

  private static int EncodeHandle(int patchAddress, byte opcode) =>
      (patchAddress & 0x3FF) | ((opcode & 0x07) << HandleOpcodeShift);

  private static int HandleAddress(int handle) => handle & 0x3FF;
  private static byte HandleOpcode(int handle) => (byte)((handle >> HandleOpcodeShift) & 0x07);

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

  // Forward transfer: emit the branch with its opcode and leave a handle 'r'.
  private void CompileForwardIf(F18Token token, byte opcode, ControlKind kind)
  {
    _ = kind; // kind is no longer used; the opcode is carried in the handle.
    if (!RequireDefinition(token))
    {
      return;
    }

    var patchAddress = Builder.EmitControlPlaceholder(opcode, token);
    PushControlValue(EncodeHandle(patchAddress, opcode), token);
  }

  private void CompileAhead(F18Token token)
  {
    if (!RequireDefinition(token))
    {
      return;
    }

    var patchAddress = Builder.EmitControlPlaceholder(0x02, token);
    PushControlValue(EncodeHandle(patchAddress, 0x02), token);
  }

  private void CompileLeap(F18Token token)
  {
    // DB013 5.3.2.1: leap compiles a CALL (0x03) to the matching then.
    if (!RequireDefinition(token))
    {
      return;
    }

    var patchAddress = Builder.EmitControlPlaceholder(0x03, token);
    PushControlValue(EncodeHandle(patchAddress, 0x03), token);
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
    Builder.PatchControl(HandleAddress(handle), HandleOpcode(handle), Builder.CurrentAddress, token);
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

    var jumpPatch = Builder.EmitControlPlaceholder(0x02, token);
    Builder.PatchControl(HandleAddress(handle), HandleOpcode(handle), Builder.CurrentAddress, token);
    PushControlValue(EncodeHandle(jumpPatch, 0x02), token);
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
    // can be disabled (PackBackwardBranches = false) for layout-stable artifacts
    // such as the runtime-compiled Kraken node-708 reply helper.
    if (!_options.PackBackwardBranches ||
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
    Builder.EmitControl(0x05, destination & 0x3FF, token);
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
    // PackBackwardBranches is disabled.
    if (!_options.PackBackwardBranches ||
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
    var patchAddress = Builder.EmitControlPlaceholder(opcode, token);
    int handle = EncodeHandle(patchAddress, opcode);

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
    Builder.EmitControl(0x02, destination & 0x3FF, token);
    Builder.PatchControl(HandleAddress(handle), HandleOpcode(handle), Builder.CurrentAddress, token);
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

    Builder.EmitControl(0x03, symbol.Value, token);
  }

  private bool DefineSymbol(F18Token token, int address, F18ExportKind kind)
  {
    if (NameExists(token.Text))
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

  private bool NameExists(string name) =>
      _symbols.ContainsKey(name) ||
      _externalSymbols.ContainsKey(name) ||
      _constants.ContainsKey(name) ||
      F18InstructionSet.Opcodes.ContainsKey(name) ||
      ReservedCompilerWords.Contains(name);

  private void EmitKnownSymbol(F18ExportedSymbol symbol, F18Token token)
  {
    if (symbol.Kind == F18ExportKind.Word)
    {
      Builder.EmitControl(0x03, symbol.Value, token);
    }
    else
    {
      Builder.EmitLiteral(symbol.Value, token);
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
    if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
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

  private bool RequireDefinition(F18Token token)
  {
    if (_inDefinition && _compileMode)
    {
      return true;
    }

    if (_inDefinition)
    {
      AddError(
          "F18C053",
          $"'{token.Text}' is a target-compilation word and cannot be used while '[' interpretation is active. Use ']' first.",
          token.Location);
      return false;
    }

    AddError("F18C034", $"'{token.Text}' is only valid inside a word definition.", token.Location);
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
    private readonly int _lastAddress;
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
      _lastAddress = baseAddress + wordCount - 1;
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

    public void SetOrigin(int address, F18Token token)
    {
      Align();
      if (address < _baseAddress || address > _lastAddress)
      {
        ReportError(
            "F18M001",
            $"{_memoryName} origin must be between 0x{_baseAddress:X3} and 0x{_lastAddress:X3}.",
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

      if (!F18InstructionSet.ControlFitsSlot(slot, _cursor, destination))
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

    public void PatchControl(int memoryAddress, byte opcode, int destination, F18Token token)
    {
      var index = memoryAddress - _baseAddress;
      if (index < 0 || index >= _memory.Length || !_memory[index].HasValue)
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

    public void Align()
    {
      FlushInstruction();
      FlushPendingData();
    }

    public IReadOnlyList<int> CreateImage()
    {
      // The image always spans the full memory (64 words for ROM/RAM). Unwritten
      // words are the F18A empty-word value 0x15555 (an all-zero instruction word
      // XOR-encodes to 0x15555, which is what the compiler pre-fills slots with and
      // what the chip reads back for never-written ROM/RAM). This makes a compiled
      // image directly comparable, word-for-word, with the ROM read from silicon.
      var result = new int[_memory.Length];
      for (var index = 0; index < _memory.Length; index++)
      {
        result[index] = _memory[index] ?? F18InstructionSet.EncodingXor;
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

    private void WriteWord(int value, F18Token token)
    {
      var index = _cursor - _baseAddress;
      if (index < 0 || index >= _memory.Length)
      {
        ReportError(
            "F18M005",
            $"Compiled output exceeds the node's {_memory.Length}-word {_memoryName} range.",
            token);
        _cursor++;
        return;
      }

      if (_memory[index].HasValue)
      {
        ReportError(
            "F18M006",
            $"{_memoryName} address 0x{_cursor:X3} is written more than once.",
            token);
        _cursor++;
        return;
      }

      _memory[index] = value & F18InstructionSet.WordMask;
      _cursor++;
    }

    private void ReportError(string code, string message, F18Token token) =>
        _report(new F18Diagnostic(F18DiagnosticSeverity.Error, code, message, token.Location));
  }

}