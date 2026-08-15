namespace Ga144.Evb.Ide.Compiler;

public enum F18DiagnosticSeverity
{
  Info,
  Warning,
  Error
}

public enum F18MemorySpace
{
  Ram,
  Rom
}

public enum F18ExportKind
{
  Word,
  Label,
  Constant
}

public enum F18MacroKind
{
  System,
  User
}

public enum F18MacroLookupScope
{
  SystemOnly,
  UserAndSystem
}

public sealed class F18MacroResolution
{
  public required bool Success { get; init; }
  public string? Name { get; init; }
  public string? SourceCode { get; init; }
  public F18MacroKind Kind { get; init; }
  public string? ErrorMessage { get; init; }

  public static F18MacroResolution FromSource(string name, string sourceCode, F18MacroKind kind) => new()
  {
    Success = true,
    Name = name,
    SourceCode = sourceCode ?? string.Empty,
    Kind = kind
  };

  public static F18MacroResolution Failure(string message) => new()
  {
    Success = false,
    ErrorMessage = message
  };
}

public sealed record F18SourceLocation(int Line, int Column);

public sealed record F18Diagnostic(
    F18DiagnosticSeverity Severity,
    string Code,
    string Message,
    F18SourceLocation Location)
{
  public override string ToString() =>
      $"{Severity.ToString().ToLowerInvariant()} {Code} ({Location.Line},{Location.Column}): {Message}";
}

public sealed record F18ExportedSymbol(
    string Name,
    int Value,
    F18ExportKind Kind,
    int NodeCoordinate,
    F18MemorySpace MemorySpace);

public sealed class F18ExportSet
{
  public int NodeCoordinate { get; init; }
  public IReadOnlyDictionary<string, int> Constants { get; init; } =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
  public IReadOnlyDictionary<string, F18ExportedSymbol> Symbols { get; init; } =
      new Dictionary<string, F18ExportedSymbol>(StringComparer.OrdinalIgnoreCase);
}

public sealed class F18ImportResolution
{
  public required bool Success { get; init; }
  public F18ExportSet? Exports { get; init; }
  public string? ErrorMessage { get; init; }

  public static F18ImportResolution FromExports(F18ExportSet exports) => new()
  {
    Success = true,
    Exports = exports
  };

  public static F18ImportResolution Failure(string message) => new()
  {
    Success = false,
    ErrorMessage = message
  };
}

public sealed class F18CompilerOptions
{
  public F18MemorySpace MemorySpace { get; init; } = F18MemorySpace.Ram;
  public int NodeCoordinate { get; init; }
  public int MemoryBaseAddress { get; init; }
  public int MemoryWordCount { get; init; } = 64;
  public bool IncludeCommonRomWords { get; init; } = true;
  public IReadOnlyDictionary<string, int> PredefinedConstants { get; init; } =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
  public IReadOnlyDictionary<string, F18ExportedSymbol> PredefinedSymbols { get; init; } =
      new Dictionary<string, F18ExportedSymbol>(StringComparer.OrdinalIgnoreCase);
  public Func<int, F18ImportResolution>? ImportResolver { get; init; }
  public Func<string, F18MacroLookupScope, F18MacroResolution>? MacroResolver { get; init; }
  public F18MacroLookupScope MacroLookupScope { get; init; } = F18MacroLookupScope.UserAndSystem;

  // When true (the default), control transfers pack into the current instruction
  // word's next free slot when reachable through that slot's narrowed address
  // field, matching the silicon ROM (DB001 2.3.1). Backward transfers
  // (next/again/until/-until) pack when their known destination fits; forward
  // transfers (if/-if/ahead/leap/else) pack greedily into whatever slot is free
  // and error (F18M005) only if the resolved target is unreachable from that slot,
  // in which case the source must be aligned manually. Set false to force every
  // transfer into its own slot-0 word. The runtime-compiled Kraken node-708 reply
  // helper relies on the pre-packing layout, so it compiles with this disabled to
  // stay byte-stable.
  public bool PackControlTransfers { get; init; } = true;

  public string MemoryName => MemorySpace == F18MemorySpace.Ram ? "RAM" : "ROM";

  public static F18CompilerOptions ForRam(int nodeCoordinate = 0) => new()
  {
    MemorySpace = F18MemorySpace.Ram,
    NodeCoordinate = nodeCoordinate,
    MemoryBaseAddress = 0x000,
    MemoryWordCount = 64,
    IncludeCommonRomWords = true,
    MacroLookupScope = F18MacroLookupScope.UserAndSystem
  };

  public static F18CompilerOptions ForRom(int nodeCoordinate) => new()
  {
    MemorySpace = F18MemorySpace.Rom,
    NodeCoordinate = nodeCoordinate,
    MemoryBaseAddress = 0x080,
    MemoryWordCount = 64,
    IncludeCommonRomWords = false,
    MacroLookupScope = F18MacroLookupScope.SystemOnly
  };
}

public sealed class F18CompileResult
{
  public required IReadOnlyList<int> Words { get; init; }
  public required IReadOnlyList<F18Diagnostic> Diagnostics { get; init; }
  public required IReadOnlyDictionary<string, F18ExportedSymbol> Symbols { get; init; }
  public required IReadOnlyDictionary<string, int> Constants { get; init; }
  public required F18MemorySpace MemorySpace { get; init; }
  public required int MemoryBaseAddress { get; init; }
  public required int NodeCoordinate { get; init; }
  public string ExpandedSource { get; init; } = string.Empty;
  public int? EntryPoint { get; init; }
  public int UsedWordCount { get; init; }
  public IReadOnlyList<int> InterpreterDataStack { get; init; } = [];
  public IReadOnlyList<int> InterpreterReturnStack { get; init; } = [];

  // Backward-compatible name used by the current node editor.
  public IReadOnlyList<int> RamWords => Words;

  public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != F18DiagnosticSeverity.Error);

  public F18ExportSet Exports => new()
  {
    NodeCoordinate = NodeCoordinate,
    Constants = Constants,
    Symbols = Symbols
  };

  public string CreateListing()
  {
    if (Words.Count == 0)
    {
      return $"No {MemorySpace.ToString().ToUpperInvariant()} words generated.";
    }

    return string.Join(
        Environment.NewLine,
        Words.Select((word, index) =>
            $"{MemoryBaseAddress + index:X3}: 0x{word & F18InstructionSet.WordMask:X5}"));
  }

  public string CreateSymbolListing()
  {
    var rows = new List<(string Name, int Value, F18ExportKind Kind)>();
    rows.AddRange(Constants.Select(pair => (pair.Key, pair.Value, F18ExportKind.Constant)));
    rows.AddRange(Symbols.Values.Select(symbol => (symbol.Name, symbol.Value, symbol.Kind)));

    if (rows.Count == 0)
    {
      return "No exported constants or symbols defined.";
    }

    return string.Join(
        Environment.NewLine,
        rows.OrderBy(item => item.Value)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.Name,-24} 0x{item.Value:X3}  {item.Kind.ToString().ToLowerInvariant()}"));
  }


  public string CreateInterpreterStackListing()
  {
    static string Format(string name, IReadOnlyList<int> values, int capacity)
    {
      string body = values.Count == 0
          ? "<empty>"
          : string.Join(" ", values.Select(value => $"0x{value & F18InstructionSet.WordMask:X5}"));
      return $"{name} ({values.Count}/{capacity}, bottom -> top): {body}";
    }

    return Format("data", InterpreterDataStack, F18CompileTimeInterpreter.DataStackCapacity) +
           Environment.NewLine +
           Format("return", InterpreterReturnStack, F18CompileTimeInterpreter.ReturnStackCapacity);
  }

  public static F18CompileResult CreateFailure(
      F18MemorySpace memorySpace,
      int nodeCoordinate,
      int memoryBaseAddress,
      string code,
      string message) => new()
      {
        Words = [],
        Diagnostics =
      [
          new F18Diagnostic(
                F18DiagnosticSeverity.Error,
                code,
                message,
                new F18SourceLocation(1, 1))
      ],
        Symbols = new Dictionary<string, F18ExportedSymbol>(StringComparer.OrdinalIgnoreCase),
        Constants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        MemorySpace = memorySpace,
        MemoryBaseAddress = memoryBaseAddress,
        NodeCoordinate = nodeCoordinate,
        ExpandedSource = string.Empty,
        UsedWordCount = 0
      };
}