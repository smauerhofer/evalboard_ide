using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>Outcome of <see cref="SramSimulatorInstaller.InstallAsync"/>.</summary>
public sealed record SramSimulatorInstallResult(
    bool Success,
    IReadOnlyList<F18Diagnostic> Diagnostics,
    SramMasterSupportAddresses? Addresses);

/// <summary>
/// Deploys the node-707 SRAM simulator (<see cref="SramSimulatorPrograms"/>) -- a single-node
/// stand-in for AN003's real SRAM cluster, built for exercising a CVM's SRAM-shaped memory
/// traffic over Kraken without the real external SRAM hardware. Much simpler than
/// <see cref="SramClusterInstaller"/>: one node, no Tentacle reorganization (node 707 is already
/// Tentacle 1 position 0 in the default fixed <see cref="KrakenTopology"/>, so it needs no special
/// routing the way the real cluster's chosen master does), and deployed with
/// <c>KrakenLiveController.WriteRamAsync</c> ONLY -- never <c>JumpAsync</c> -- so node 707 stays
/// puppetable indefinitely, exactly like a real memory-master node's own resident support code.
///
/// The compiled source is written into node 707's own <see cref="Ga144NodeConfiguration.SourceCode"/>
/// first (not compiled from a private copy), so after installing it is visible and editable in the
/// Node Editor like any other configured node -- this mutates the project, the same way
/// <see cref="SramClusterInstaller"/> does for its five nodes.
/// </summary>
public sealed class SramSimulatorInstaller
{
  private readonly Ga144ChipConfiguration _chip;
  private readonly F18NodeCompilationService _compiler;

  public SramSimulatorInstaller(
      Ga144ChipConfiguration chip,
      Ga144RomLibrary romLibrary,
      IReadOnlyList<F18MacroDefinition>? userMacros = null)
  {
    _chip = chip ?? throw new ArgumentNullException(nameof(chip));
    ArgumentNullException.ThrowIfNull(romLibrary);
    _compiler = new F18NodeCompilationService(chip, romLibrary, userMacros);
  }

  /// <summary>
  /// Compiles and deploys node 707's resident support source. Requires a Kraken already erected
  /// (any of the default tentacles is enough -- node 707 needs no reorganization), and requires a
  /// route to node 707 specifically, which the caller resolves the same way any other Kraken
  /// operation does (<c>KrakenTopology.BuildRouteMap</c>).
  /// </summary>
  public async Task<SramSimulatorInstallResult> InstallAsync(
      KrakenLiveController controller,
      KrakenNodeRoute route,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(controller);
    ArgumentNullException.ThrowIfNull(route);
    if (route.Coordinate != SramSimulatorPrograms.SimulatedCoordinate)
    {
      throw new ArgumentException(
          $"The SRAM simulator only ever installs to node {SramSimulatorPrograms.SimulatedCoordinate:000}, not {route.Coordinate:000}.",
          nameof(route));
    }

    Ga144NodeConfiguration node = _chip.GetNode(SramSimulatorPrograms.SimulatedCoordinate);
    node.SourceCode = SramSimulatorPrograms.BuildSource();
    node.Enabled = true;

    F18NodeCompilationResult compiled = _compiler.CompileNode(SramSimulatorPrograms.SimulatedCoordinate);
    if (!compiled.Ram.Success)
    {
      return new SramSimulatorInstallResult(false, compiled.Ram.Diagnostics, null);
    }

    // WriteRamAsync only -- deliberately never JumpAsync. See the class remarks and
    // SramClusterInstaller's identical reasoning for the real cluster's master node: a Jump would
    // move P away from the incoming port for good, removing 707 from the tentacle Kraken needs to
    // keep puppeting it through.
    await controller.WriteRamAsync(route, compiled.Ram.Words, cancellationToken);

    var addresses = new SramMasterSupportAddresses(
        compiled.Ram.Symbols["sram-read"].Value,
        compiled.Ram.Symbols["sram-write"].Value,
        compiled.Ram.Symbols["sram-cx"].Value,
        compiled.Ram.Symbols["sram-mask"].Value,
        compiled.Ram.Symbols["echo"].Value);

    return new SramSimulatorInstallResult(true, compiled.Ram.Diagnostics, addresses);
  }
}
