using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class NodeEditorViewModel : ObservableObject
{
  private readonly Ga144ChipConfiguration _chip;
  private readonly Ga144RomLibrary _romLibrary;
  private readonly Ga144RomNodeDefinition _romNode;
  private readonly IReadOnlyList<F18MacroDefinition> _userMacros;
  private readonly string _originalRomSource;
  private readonly string _originalRomWords;
  private bool _enabled;
  private string _sourceCode;
  private string _romSourceCode;
  private string _ramWordsText;
  private string _romWordsText;
  private string _entryPoint;
  private string _p;
  private string _a;
  private string _b;
  private string _io;
  private string _returnStackText;
  private string _parameterStackText;
  private string _compilationStatus = "Source has not been compiled in this editor session.";
  private string _compilationDiagnostics = "Press Compile ROM + RAM to validate both dictionaries.";
  private string _compilationListing = "No RAM compiler listing is available.";
  private string _romCompilationListing = "No ROM compiler listing is available.";
  private string _compilationSymbols = "No compiler symbol tables are available.";
  private string _expandedRamSource = "Compile to view RAM source after macro expansion.";
  private string _expandedRomSource = "Compile to view ROM source after macro expansion.";

  public NodeEditorViewModel(
      Ga144NodeConfiguration node,
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition> userMacros,
      string romLibraryPath,
      Ga144ChipRole chipRole,
      KrakenNodeRoute? krakenRoute,
      Func<KrakenEndpointInfo?> krakenEndpointResolver,
      KrakenLiveController krakenController)
  {
    Node = node;
    _chip = chip;
    _romLibrary = romLibrary;
    _userMacros = userMacros ?? [];
    RomLibraryPath = romLibraryPath;
    ChipRole = chipRole;
    KrakenRoute = krakenRoute;
    KrakenEndpointResolver = krakenEndpointResolver;
    KrakenController = krakenController;

    node.Normalize();
    chip.Normalize();
    romLibrary.Normalize();
    _romNode = romLibrary.GetNode(node.Coordinate);
    _romNode.Normalize();

    _enabled = node.Enabled;
    _sourceCode = node.SourceCode;
    _romSourceCode = _romNode.SourceCode;
    _ramWordsText = Join(node.RamWords);
    _romWordsText = Join(_romNode.RomWords);
    _entryPoint = node.Startup.EntryPoint;
    _p = node.Startup.P;
    _a = node.Startup.A;
    _b = node.Startup.B;
    _io = node.Startup.Io;
    _returnStackText = Join(node.Startup.ReturnStack);
    _parameterStackText = Join(node.Startup.ParameterStack);
    _originalRomSource = _romSourceCode;
    _originalRomWords = _romWordsText;
    CompileCommand = new RelayCommand(Compile);
  }

  public Ga144NodeConfiguration Node { get; }
  public string NodeCoordinate => Node.Coordinate.ToString("000");
  public string RomLibraryPath { get; }
  public Ga144ChipRole ChipRole { get; }
  public KrakenNodeRoute? KrakenRoute { get; }
  public KrakenConfiguration KrakenConfiguration => _chip.Kraken;
  public Func<KrakenEndpointInfo?> KrakenEndpointResolver { get; }
  public KrakenLiveController KrakenController { get; }
  public bool KrakenOnlineAvailable => KrakenRoute is { IsHead: false };
  public string KrakenOnlineHint => KrakenRoute switch
  {
    null => "Install a Kraken on this chip before opening online node control.",
    { IsHead: true } => "Node 708 is the Kraken head/session endpoint and is not a tentacle-controlled node.",
    { } route when KrakenController.IsConnected => $"Open live Kraken control via T{route.TentacleNumber}:{route.Position:00}. The already erected Kraken will be reused without resetting the GA144.",
    { } route => $"Open live Kraken control via T{route.TentacleNumber}:{route.Position:00}. Use Connect & erect if no live Kraken has been established yet."
  };
  public RelayCommand CompileCommand { get; }
  public int SystemMacroCount => _romLibrary.SystemMacros.Count;
  public int UserMacroCount => _userMacros.Count;

  public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

  public string SourceCode
  {
    get => _sourceCode;
    set
    {
      if (SetProperty(ref _sourceCode, value ?? string.Empty))
      {
        CompilationStatus = "RAM source changed; compile again to refresh RAM output.";
      }
    }
  }

  public string RomSourceCode
  {
    get => _romSourceCode;
    set
    {
      if (SetProperty(ref _romSourceCode, value ?? string.Empty))
      {
        CompilationStatus = "ROM source changed; ROM must compile before RAM can compile.";
      }
    }
  }

  public string RamWordsText { get => _ramWordsText; set => SetProperty(ref _ramWordsText, value ?? string.Empty); }
  public string RomWordsText { get => _romWordsText; set => SetProperty(ref _romWordsText, value ?? string.Empty); }
  public string EntryPoint { get => _entryPoint; set => SetProperty(ref _entryPoint, value ?? string.Empty); }
  public string P { get => _p; set => SetProperty(ref _p, value ?? string.Empty); }
  public string A { get => _a; set => SetProperty(ref _a, value ?? string.Empty); }
  public string B { get => _b; set => SetProperty(ref _b, value ?? string.Empty); }
  public string Io { get => _io; set => SetProperty(ref _io, value ?? string.Empty); }
  public string ReturnStackText { get => _returnStackText; set => SetProperty(ref _returnStackText, value ?? string.Empty); }
  public string ParameterStackText { get => _parameterStackText; set => SetProperty(ref _parameterStackText, value ?? string.Empty); }
  public string CompilationStatus { get => _compilationStatus; private set => SetProperty(ref _compilationStatus, value); }
  public string CompilationDiagnostics { get => _compilationDiagnostics; private set => SetProperty(ref _compilationDiagnostics, value); }
  public string CompilationListing { get => _compilationListing; private set => SetProperty(ref _compilationListing, value); }
  public string RomCompilationListing { get => _romCompilationListing; private set => SetProperty(ref _romCompilationListing, value); }
  public string CompilationSymbols { get => _compilationSymbols; private set => SetProperty(ref _compilationSymbols, value); }
  public string ExpandedRamSource { get => _expandedRamSource; private set => SetProperty(ref _expandedRamSource, value); }
  public string ExpandedRomSource { get => _expandedRomSource; private set => SetProperty(ref _expandedRomSource, value); }

  public void Compile()
  {
    var service = new F18NodeCompilationService(_chip, _romLibrary, _userMacros);
    var result = service.CompileNode(Node.Coordinate, SourceCode, RomSourceCode);

    CompilationDiagnostics = FormatDiagnostics(result.Rom, result.Ram);
    ExpandedRomSource = result.Rom.ExpandedSource;
    ExpandedRamSource = result.Ram.ExpandedSource;
    RomCompilationListing = result.Rom.CreateListing();
    CompilationListing = result.Ram.CreateListing();
    CompilationSymbols =
        "ROM exports" + Environment.NewLine +
        "-----------" + Environment.NewLine +
        result.Rom.CreateSymbolListing() + Environment.NewLine + Environment.NewLine +
        "ROM compile-time FORTH stacks" + Environment.NewLine +
        "-----------------------------" + Environment.NewLine +
        result.Rom.CreateInterpreterStackListing() + Environment.NewLine + Environment.NewLine +
        "RAM exports" + Environment.NewLine +
        "-----------" + Environment.NewLine +
        result.Ram.CreateSymbolListing() + Environment.NewLine + Environment.NewLine +
        "RAM compile-time FORTH stacks" + Environment.NewLine +
        "-----------------------------" + Environment.NewLine +
        result.Ram.CreateInterpreterStackListing();

    if (result.Rom.Success)
    {
      RomWordsText = string.Join(
          Environment.NewLine,
          result.Rom.Words.Select(word => $"0x{word & F18InstructionSet.WordMask:X5}"));
    }

    if (!result.Success)
    {
      var errors = result.Rom.Diagnostics.Concat(result.Ram.Diagnostics)
          .Count(item => item.Severity == F18DiagnosticSeverity.Error);
      var warnings = result.Rom.Diagnostics.Concat(result.Ram.Diagnostics)
          .Count(item => item.Severity == F18DiagnosticSeverity.Warning);
      CompilationStatus =
          $"Compilation failed: {errors} error(s), {warnings} warning(s). Any successful ROM output was retained; RAM output was not replaced.";
      return;
    }

    RamWordsText = string.Join(
        Environment.NewLine,
        result.Ram.Words.Select(word => $"0x{word & F18InstructionSet.WordMask:X5}"));

    if (result.Ram.EntryPoint is int entryPoint)
    {
      EntryPoint = $"0x{entryPoint:X3}";
    }

    var warningCount = result.Rom.Diagnostics.Concat(result.Ram.Diagnostics)
        .Count(item => item.Severity == F18DiagnosticSeverity.Warning);
    CompilationStatus =
        $"ROM compiled first ({result.Rom.UsedWordCount} word(s)); RAM compiled second ({result.Ram.UsedWordCount} word(s)); {warningCount} warning(s).";
  }

  public bool Apply()
  {
    Node.Enabled = Enabled;
    Node.SourceCode = SourceCode;
    Node.RamWords = Split(RamWordsText, 64);
    Node.Startup.EntryPoint = NormalizeWord(EntryPoint, "0x000");
    Node.Startup.P = NormalizeWord(P, "0x000");
    Node.Startup.A = NormalizeWord(A, "0x000");
    Node.Startup.B = NormalizeWord(B, "0x000");
    Node.Startup.Io = NormalizeWord(Io, "0x00000");
    Node.Startup.ReturnStack = Split(ReturnStackText, 9);
    Node.Startup.ParameterStack = Split(ParameterStackText, 10);

    _romNode.SourceCode = RomSourceCode;
    _romNode.RomWords = Split(RomWordsText, 64);

    return !string.Equals(_originalRomSource, RomSourceCode, StringComparison.Ordinal) ||
           !string.Equals(_originalRomWords, RomWordsText, StringComparison.Ordinal);
  }

  private static string FormatDiagnostics(F18CompileResult rom, F18CompileResult ram)
  {
    static string Section(string name, F18CompileResult result)
    {
      var body = result.Diagnostics.Count == 0
          ? "No diagnostics."
          : string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
      return name + Environment.NewLine + new string('-', name.Length) + Environment.NewLine + body;
    }

    return Section("ROM", rom) + Environment.NewLine + Environment.NewLine + Section("RAM", ram);
  }

  private static string Join(IEnumerable<string> values) => string.Join(Environment.NewLine, values);

  private static List<string> Split(string text, int maximum)
  {
    return text
        .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Take(maximum)
        .ToList();
  }

  private static string NormalizeWord(string value, string fallback) =>
      string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
