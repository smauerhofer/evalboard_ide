using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>Compile/deploy outcome for one of the four SRAM cluster nodes.</summary>
public sealed record SramClusterInstallNodeResult(
    int Coordinate,
    bool Success,
    IReadOnlyList<F18Diagnostic> Diagnostics);

/// <summary>Overall outcome of <see cref="SramClusterInstaller.InstallAsync"/>.</summary>
public sealed record SramClusterInstallResult(IReadOnlyList<SramClusterInstallNodeResult> Nodes)
{
  public bool Success => Nodes.Count > 0 && Nodes.All(node => node.Success);
}

/// <summary>
/// Deploys AN003's SRAM cluster (see <see cref="SramClusterPrograms"/>) onto
/// nodes 007, 008, 009, and 107, for a given SRAM memory-master node (106,
/// 108, or 207). Mirrors how the Node Editor already loads and starts a
/// single node's program -- compile via <see cref="F18NodeCompilationService"/>,
/// then <c>KrakenLiveController.WriteRamAsync</c> + <c>JumpAsync(0x000)</c> --
/// just run across all four cluster nodes in one action, with node 107's
/// source generated for the requested master immediately before compiling.
///
/// The bundled source is written into each node's own
/// <see cref="Ga144NodeConfiguration.SourceCode"/> first (not compiled from a
/// private copy), so after installing, all four nodes are visible and
/// editable in the Node Editor exactly like any other configured node -- this
/// mutates the project, the same way opening a node and clicking OK does.
/// </summary>
public sealed class SramClusterInstaller
{
  public const int DataBusNodeCoordinate = 7;
  public const int ControlPinsNodeCoordinate = 8;
  public const int AddressBusNodeCoordinate = 9;
  public const int InterfaceNodeCoordinate = 107;

  /// <summary>The three nodes AN003 permits as SRAM memory masters.</summary>
  public static readonly IReadOnlyList<int> ValidMasterCoordinates = [106, 108, 207];

  private readonly Ga144ChipConfiguration _chip;
  private readonly F18NodeCompilationService _compiler;

  public SramClusterInstaller(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition>? userMacros = null)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    ArgumentNullException.ThrowIfNull(romLibrary);
    _compiler = new F18NodeCompilationService(chip, romLibrary, userMacros);
  }

  /// <summary>
  /// Compiles and deploys all four cluster nodes for <paramref name="masterCoordinate"/>.
  /// Stops at the first node whose compile fails (leaving it and every node
  /// after it out of the result) rather than deploying a partial/inconsistent
  /// cluster silently; a node that compiled but could not be reached over
  /// Kraken (no route, or a transport failure) is reported as failed too, via
  /// the calling <c>WriteRamAsync</c>/<c>JumpAsync</c> exception propagating out.
  /// </summary>
  public async Task<SramClusterInstallResult> InstallAsync(
      KrakenLiveController controller,
      IReadOnlyDictionary<int, KrakenNodeRoute> routes,
      int masterCoordinate,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(controller);
    ArgumentNullException.ThrowIfNull(routes);
    if (!ValidMasterCoordinates.Contains(masterCoordinate))
    {
      throw new ArgumentException(
          $"Node {masterCoordinate:000} cannot act as an SRAM memory master; must be one of " +
          string.Join(", ", ValidMasterCoordinates.Select(coordinate => coordinate.ToString("000"))) + ".",
          nameof(masterCoordinate));
    }

    string masterPortName = KrakenTopology.PortName(InterfaceNodeCoordinate, masterCoordinate);

    // Fixed, predictable order (address bus, control pins, data bus, then the
    // interface node last) rather than parallel: there is no compile-time
    // dependency between the four, but deploying the interface node -- the
    // one that starts fielding master requests -- last means a partial
    // failure never leaves a half-wired cluster answering requests it can't
    // actually service yet.
    (int Coordinate, string Source)[] plan =
    [
      (AddressBusNodeCoordinate, SramClusterPrograms.Node009AddressBus),
      (ControlPinsNodeCoordinate, SramClusterPrograms.Node008ControlPins),
      (DataBusNodeCoordinate, SramClusterPrograms.Node007DataBusAndControl),
      (InterfaceNodeCoordinate, SramClusterPrograms.BuildNode107Source(masterPortName))
    ];

    var results = new List<SramClusterInstallNodeResult>(plan.Length);
    foreach (var (coordinate, source) in plan)
    {
      Ga144NodeConfiguration node = _chip.GetNode(coordinate);
      node.SourceCode = source;
      node.Enabled = true;

      F18NodeCompilationResult compiled = _compiler.CompileNode(coordinate);
      if (!compiled.Ram.Success)
      {
        results.Add(new SramClusterInstallNodeResult(coordinate, false, compiled.Ram.Diagnostics));
        break;
      }

      if (!routes.TryGetValue(coordinate, out KrakenNodeRoute? route))
      {
        results.Add(new SramClusterInstallNodeResult(
            coordinate,
            false,
            [
              new F18Diagnostic(
                  F18DiagnosticSeverity.Error,
                  "SRAM001",
                  $"No Kraken route to node {coordinate:000}. Is the Kraken erected?",
                  new F18SourceLocation(0, 0))
            ]));
        break;
      }

      await controller.WriteRamAsync(route, compiled.Ram.Words, cancellationToken);
      await controller.JumpAsync(route, 0x000, cancellationToken);
      results.Add(new SramClusterInstallNodeResult(coordinate, true, compiled.Ram.Diagnostics));
    }

    return new SramClusterInstallResult(results);
  }
}
