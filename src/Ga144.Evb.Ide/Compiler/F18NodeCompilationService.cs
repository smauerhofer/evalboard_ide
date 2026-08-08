using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Compiler;

public sealed class F18NodeCompilationResult
{
  public required F18CompileResult Rom { get; init; }
  public required F18CompileResult Ram { get; init; }
  public bool Success => Rom.Success && Ram.Success;
}

public sealed class F18NodeCompilationService
{
  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;

  public F18NodeCompilationService(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition>? userMacros = null)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _userMacros = userMacros ?? [];
    _chip.Normalize();
    _romLibrary.Normalize();
  }

  public F18NodeCompilationResult CompileNode(
      int coordinate,
      string? ramSourceOverride = null,
      string? romSourceOverride = null)
  {
    var session = new CompilationSession(
        _chip,
        _romLibrary,
        _userMacros,
        coordinate,
        ramSourceOverride,
        romSourceOverride);

    var rom = session.CompileRom(coordinate);
    var ram = rom.Success
        ? session.CompileRam(coordinate)
        : F18CompileResult.CreateFailure(
            F18MemorySpace.Ram,
            coordinate,
            0x000,
            "F18G001",
            $"RAM compilation was not started because node {coordinate:000} ROM compilation failed.");

    return new F18NodeCompilationResult
    {
      Rom = rom,
      Ram = ram
    };
  }

  private sealed class CompilationSession
  {
    private readonly Ga144ChipConfiguration _chip;
    private readonly Ga144RomLibrary _romLibrary;
    private readonly IReadOnlyDictionary<string, F18MacroDefinition> _systemMacros;
    private readonly IReadOnlyDictionary<string, F18MacroDefinition> _userMacros;
    private readonly int _overrideCoordinate;
    private readonly string? _ramSourceOverride;
    private readonly string? _romSourceOverride;
    private readonly Dictionary<int, F18CompileResult> _romCache = [];
    private readonly Dictionary<int, F18CompileResult> _ramCache = [];
    private readonly HashSet<int> _romStack = [];
    private readonly HashSet<int> _ramStack = [];

    public CompilationSession(
        Ga144ChipConfiguration chip,
        Ga144RomLibrary romLibrary,
        IReadOnlyList<F18MacroDefinition> userMacros,
        int overrideCoordinate,
        string? ramSourceOverride,
        string? romSourceOverride)
    {
      _chip = chip;
      _romLibrary = romLibrary;
      _systemMacros = BuildMacroDictionary(romLibrary.SystemMacros);
      _userMacros = BuildMacroDictionary(userMacros);
      _overrideCoordinate = overrideCoordinate;
      _ramSourceOverride = ramSourceOverride;
      _romSourceOverride = romSourceOverride;
    }

    public F18CompileResult CompileRom(int coordinate)
    {
      if (_romCache.TryGetValue(coordinate, out var cached))
      {
        return cached;
      }

      if (!_romStack.Add(coordinate))
      {
        return CacheRom(F18CompileResult.CreateFailure(
            F18MemorySpace.Rom,
            coordinate,
            0x080,
            "F18G002",
            $"Cyclic ROM import detected at node {coordinate:000}."));
      }

      try
      {
        var source = coordinate == _overrideCoordinate && _romSourceOverride is not null
            ? _romSourceOverride
            : _romLibrary.GetNode(coordinate).SourceCode;

        var options = F18CompilerOptions.ForRom(coordinate);
        options = new F18CompilerOptions
        {
          MemorySpace = options.MemorySpace,
          NodeCoordinate = options.NodeCoordinate,
          MemoryBaseAddress = options.MemoryBaseAddress,
          MemoryWordCount = options.MemoryWordCount,
          IncludeCommonRomWords = false,
          ImportResolver = importedCoordinate => ResolveRomImport(coordinate, importedCoordinate),
          MacroResolver = ResolveMacro,
          MacroLookupScope = F18MacroLookupScope.SystemOnly
        };

        var result = new F18Compiler().Compile(source, options);
        return CacheRom(result);
      }
      finally
      {
        _romStack.Remove(coordinate);
      }
    }

    public F18CompileResult CompileRam(int coordinate)
    {
      if (_ramCache.TryGetValue(coordinate, out var cached))
      {
        return cached;
      }

      if (!_ramStack.Add(coordinate))
      {
        return CacheRam(F18CompileResult.CreateFailure(
            F18MemorySpace.Ram,
            coordinate,
            0x000,
            "F18G003",
            $"Cyclic RAM import detected at node {coordinate:000}."));
      }

      try
      {
        var rom = CompileRom(coordinate);
        if (!rom.Success)
        {
          return CacheRam(F18CompileResult.CreateFailure(
              F18MemorySpace.Ram,
              coordinate,
              0x000,
              "F18G004",
              $"Node {coordinate:000} RAM requires a successful compile of its ROM dictionary."));
        }

        var source = coordinate == _overrideCoordinate && _ramSourceOverride is not null
            ? _ramSourceOverride
            : _chip.GetNode(coordinate).SourceCode;

        var options = new F18CompilerOptions
        {
          MemorySpace = F18MemorySpace.Ram,
          NodeCoordinate = coordinate,
          MemoryBaseAddress = 0x000,
          MemoryWordCount = 64,
          IncludeCommonRomWords = true,
          PredefinedConstants = rom.Constants,
          PredefinedSymbols = rom.Symbols,
          ImportResolver = importedCoordinate => ResolveRamImport(coordinate, importedCoordinate),
          MacroResolver = ResolveMacro,
          MacroLookupScope = F18MacroLookupScope.UserAndSystem
        };

        var result = new F18Compiler().Compile(source, options);
        return CacheRam(result);
      }
      finally
      {
        _ramStack.Remove(coordinate);
      }
    }

    private F18MacroResolution ResolveMacro(string name, F18MacroLookupScope scope)
    {
      string normalized = (name ?? string.Empty).Trim();
      if (scope == F18MacroLookupScope.UserAndSystem &&
          _userMacros.TryGetValue(normalized, out F18MacroDefinition? userMacro) &&
          userMacro is not null)
      {
        return F18MacroResolution.FromSource(userMacro.Name, userMacro.SourceCode, F18MacroKind.User);
      }

      if (_systemMacros.TryGetValue(normalized, out F18MacroDefinition? systemMacro) &&
          systemMacro is not null)
      {
        return F18MacroResolution.FromSource(systemMacro.Name, systemMacro.SourceCode, F18MacroKind.System);
      }

      if (scope == F18MacroLookupScope.SystemOnly && _userMacros.ContainsKey(normalized))
      {
        return F18MacroResolution.Failure(
            $"'{normalized}' is a project user macro. ROM code and system macros may import only system macros.");
      }

      return F18MacroResolution.Failure($"No macro named '{normalized}' is defined in the available scope.");
    }

    private static IReadOnlyDictionary<string, F18MacroDefinition> BuildMacroDictionary(
        IEnumerable<F18MacroDefinition> macros)
    {
      var result = new Dictionary<string, F18MacroDefinition>(StringComparer.OrdinalIgnoreCase);
      foreach (F18MacroDefinition macro in macros)
      {
        macro.Normalize();
        if (F18MacroDefinition.IsValidName(macro.Name) && !result.ContainsKey(macro.Name))
        {
          result[macro.Name] = macro;
        }
      }

      return result;
    }

    private F18ImportResolution ResolveRomImport(int requestingNode, int importedCoordinate)
    {
      if (!IsValidCoordinate(importedCoordinate))
      {
        return F18ImportResolution.Failure($"Node {importedCoordinate} is outside the GA144 array.");
      }

      if (requestingNode == importedCoordinate)
      {
        return F18ImportResolution.Failure("A ROM node cannot import itself.");
      }

      var importedRom = CompileRom(importedCoordinate);
      return importedRom.Success
          ? F18ImportResolution.FromExports(importedRom.Exports)
          : F18ImportResolution.Failure(DescribeFailure(importedRom));
    }

    private F18ImportResolution ResolveRamImport(int requestingNode, int importedCoordinate)
    {
      if (!IsValidCoordinate(importedCoordinate))
      {
        return F18ImportResolution.Failure($"Node {importedCoordinate} is outside the GA144 array.");
      }

      if (requestingNode == importedCoordinate)
      {
        return F18ImportResolution.Failure("A RAM node cannot import itself; its ROM dictionary is already in scope.");
      }

      var importedRom = CompileRom(importedCoordinate);
      if (!importedRom.Success)
      {
        return F18ImportResolution.Failure(DescribeFailure(importedRom));
      }

      var importedRam = CompileRam(importedCoordinate);
      if (!importedRam.Success)
      {
        return F18ImportResolution.Failure(DescribeFailure(importedRam));
      }

      return TryCombineExports(importedCoordinate, importedRom.Exports, importedRam.Exports);
    }

    private static F18ImportResolution TryCombineExports(
        int coordinate,
        F18ExportSet rom,
        F18ExportSet ram)
    {
      var constants = new Dictionary<string, int>(rom.Constants, StringComparer.OrdinalIgnoreCase);
      var symbols = new Dictionary<string, F18ExportedSymbol>(rom.Symbols, StringComparer.OrdinalIgnoreCase);

      foreach (var pair in ram.Constants)
      {
        if (constants.ContainsKey(pair.Key) || symbols.ContainsKey(pair.Key))
        {
          return F18ImportResolution.Failure(
              $"Node {coordinate:000} exports '{pair.Key}' from both ROM and RAM.");
        }

        constants[pair.Key] = pair.Value;
      }

      foreach (var pair in ram.Symbols)
      {
        if (constants.ContainsKey(pair.Key) || symbols.ContainsKey(pair.Key))
        {
          return F18ImportResolution.Failure(
              $"Node {coordinate:000} exports '{pair.Key}' from both ROM and RAM.");
        }

        symbols[pair.Key] = pair.Value;
      }

      return F18ImportResolution.FromExports(new F18ExportSet
      {
        NodeCoordinate = coordinate,
        Constants = constants,
        Symbols = symbols
      });
    }

    private F18CompileResult CacheRom(F18CompileResult result)
    {
      _romCache[result.NodeCoordinate] = result;
      return result;
    }

    private F18CompileResult CacheRam(F18CompileResult result)
    {
      _ramCache[result.NodeCoordinate] = result;
      return result;
    }

    private static string DescribeFailure(F18CompileResult result)
    {
      var firstError = result.Diagnostics.FirstOrDefault(
          diagnostic => diagnostic.Severity == F18DiagnosticSeverity.Error);
      return firstError is null
          ? $"Node {result.NodeCoordinate:000} {result.MemorySpace} compilation failed."
          : $"Node {result.NodeCoordinate:000} {result.MemorySpace}: {firstError.Message}";
    }

    private static bool IsValidCoordinate(int coordinate)
    {
      var row = coordinate / 100;
      var column = coordinate % 100;
      return row is >= 0 and <= 7 && column is >= 0 and <= 17;
    }
  }
}
