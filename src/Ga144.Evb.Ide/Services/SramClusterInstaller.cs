using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.Services;

/// <summary>Compile/deploy outcome for one of the five SRAM cluster nodes (the master plus 007/008/009/107).</summary>
public sealed record SramClusterInstallNodeResult(
    int Coordinate,
    bool Success,
    IReadOnlyList<F18Diagnostic> Diagnostics);

/// <summary>
/// Addresses (resolved from the compiled RAM symbol table) of the memory
/// master's own resident subroutines -- see
/// <see cref="SramClusterPrograms.BuildMasterSupportSource"/>: the four real
/// AN003 primitives, plus <see cref="EchoSubroutineAddress"/> (diagnostic
/// only, not part of AN003 -- see <see cref="KrakenSramProtocol.BuildEchoTest"/>).
/// Each leaf (<see cref="KrakenSramProtocol"/>) needs the address of the
/// specific subroutine it calls into; these are resolved once per Install
/// and then held by the caller (see <c>SramTentacleViewModel</c>) for the
/// rest of the session, until the master changes or the cluster is
/// re-installed.
/// </summary>
public sealed record SramMasterSupportAddresses(
    int ReadSubroutineAddress,
    int WriteSubroutineAddress,
    int CompareExchangeSubroutineAddress,
    int SetMaskSubroutineAddress,
    int EchoSubroutineAddress);

/// <summary>Overall outcome of <see cref="SramClusterInstaller.InstallAsync"/>.</summary>
public sealed record SramClusterInstallResult(
    IReadOnlyList<SramClusterInstallNodeResult> Nodes,
    SramMasterSupportAddresses? MasterSupport = null)
{
  public bool Success => Nodes.Count > 0 && Nodes.All(node => node.Success) && MasterSupport is not null;
}

/// <summary>
/// Deploys AN003's SRAM cluster (see <see cref="SramClusterPrograms"/>) for a
/// given SRAM memory-master node (106, 108, or 207): the master's own
/// resident support subroutines (<c>WriteRamAsync</c> only -- never
/// <c>JumpAsync</c>, so the master stays puppetable), then the four cluster
/// nodes 007, 008, 009, and 107. Mirrors how the Node Editor already loads
/// and starts a single node's program -- compile via
/// <see cref="F18NodeCompilationService"/>, then
/// <c>KrakenLiveController.WriteRamAsync</c>, then any DB013 startup
/// register configuration the source requested via '/a'/'/b'/'/io'
/// (<c>WriteAAsync</c>/<c>WriteBAsync</c>/<c>WriteIoAsync</c>), then
/// <c>JumpAsync</c> to the compiler-resolved entry point (for the four
/// cluster nodes, but not the master) -- just run across all five nodes in
/// one action, with node 107's source (and the master's own support source)
/// generated for the requested master immediately before compiling.
///
/// The bundled source is written into each node's own
/// <see cref="Ga144NodeConfiguration.SourceCode"/> first (not compiled from a
/// private copy), so after installing, all five nodes are visible and
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
  /// Reorganizes Tentacle 3 (if needed) for <paramref name="masterCoordinate"/>,
  /// deploys the master's own resident AN003 support subroutines, then
  /// compiles and deploys the four cluster nodes. Stops at the first step
  /// whose compile fails (leaving it and everything after it out of the
  /// result) rather than deploying a partial/inconsistent cluster silently;
  /// a node that compiled but could not be reached over Kraken (no route, or
  /// a transport failure) is reported as failed too, via the calling
  /// <c>WriteRamAsync</c>/<c>JumpAsync</c> exception propagating out.
  /// </summary>
  public async Task<SramClusterInstallResult> InstallAsync(
      KrakenLiveController controller,
      int masterCoordinate,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(controller);
    if (!ValidMasterCoordinates.Contains(masterCoordinate))
    {
      throw new ArgumentException(
          $"Node {masterCoordinate:000} cannot act as an SRAM memory master; must be one of " +
          string.Join(", ", ValidMasterCoordinates.Select(coordinate => coordinate.ToString("000"))) + ".",
          nameof(masterCoordinate));
    }

    await ReorganizeTentacleForMasterAsync(controller, masterCoordinate, cancellationToken);
    IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(_chip.Kraken);

    var results = new List<SramClusterInstallNodeResult>(5);

    // The master's own resident support subroutines (see
    // SramClusterPrograms.BuildMasterSupportSource / KrakenSramProtocol) are
    // deployed FIRST, into the master node's own RAM -- via WriteRamAsync
    // ONLY, deliberately never followed by JumpAsync. Jumping would move the
    // master's P register away from its incoming port for good, exactly the
    // effect JumpAsync has on 007/008/009/107 below, and would remove the
    // master from the tentacle: Kraken would no longer be able to puppet it
    // at all. Leaving P where it is keeps the master puppetable indefinitely;
    // each SRAM op leaf (see KrakenSramProtocol) later calls into one of
    // these subroutines with a real 'call' opcode injected through the same
    // puppet stream, which is safe for exactly this reason -- P does not
    // advance when it holds a port address, so 'call' pushes back the same
    // valid port address, and the subroutine's own closing ';' returns
    // straight into puppet mode.
    SramMasterSupportAddresses? masterSupport = null;
    if (!routes.TryGetValue(masterCoordinate, out KrakenNodeRoute? masterRoute))
    {
      results.Add(new SramClusterInstallNodeResult(
          masterCoordinate,
          false,
          [
            new F18Diagnostic(
                F18DiagnosticSeverity.Error,
                "SRAM001",
                $"No Kraken route to node {masterCoordinate:000}. Is the Kraken erected?",
                new F18SourceLocation(0, 0))
          ]));
    }
    else
    {
      string masterSupportPortName = KrakenTopology.PortName(masterCoordinate, InterfaceNodeCoordinate);
      Ga144NodeConfiguration masterNode = _chip.GetNode(masterCoordinate);
      masterNode.SourceCode = SramClusterPrograms.BuildMasterSupportSource(masterSupportPortName);
      masterNode.Enabled = true;

      F18NodeCompilationResult masterCompiled = _compiler.CompileNode(masterCoordinate);
      if (!masterCompiled.Ram.Success)
      {
        results.Add(new SramClusterInstallNodeResult(masterCoordinate, false, masterCompiled.Ram.Diagnostics));
      }
      else
      {
        await controller.WriteRamAsync(masterRoute, masterCompiled.Ram.Words, cancellationToken);
        masterSupport = new SramMasterSupportAddresses(
            masterCompiled.Ram.Symbols["sram-read"].Value,
            masterCompiled.Ram.Symbols["sram-write"].Value,
            masterCompiled.Ram.Symbols["sram-cx"].Value,
            masterCompiled.Ram.Symbols["sram-mask"].Value,
            masterCompiled.Ram.Symbols["echo"].Value);
        results.Add(new SramClusterInstallNodeResult(masterCoordinate, true, masterCompiled.Ram.Diagnostics));
      }
    }

    if (masterSupport is not null)
    {
      // Node 107's own source (SramClusterPrograms.Node107Interface) is now
      // AN003's real, full 3-master polling node (section 4.1) -- it polls
      // right/left/up itself, so unlike the earlier degenerate (section 6.3)
      // reimplementation it is no longer generated per master and needs no
      // port name baked in here.
      //
      // Fixed, predictable order (address bus, control pins, data bus, then
      // the interface node last) rather than parallel: there is no
      // compile-time dependency between the four, but deploying the
      // interface node -- the one that starts fielding master requests --
      // last means a partial failure never leaves a half-wired cluster
      // answering requests it can't actually service yet.
      (int Coordinate, string Source)[] plan =
      [
        (AddressBusNodeCoordinate, SramClusterPrograms.Node009AddressBus),
        (ControlPinsNodeCoordinate, SramClusterPrograms.Node008ControlPins),
        (DataBusNodeCoordinate, SramClusterPrograms.Node007DataBusAndControl),
        (InterfaceNodeCoordinate, SramClusterPrograms.Node107Interface)
      ];

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

        // Every one of these four sources now places its real code with an
        // explicit 'org' (past a data table for 008, or simply not at 0) and
        // names its true entry point with 'entry' -- jump to whatever the
        // compiler actually resolved, not a hardcoded 0x000 (which used to
        // be correct only because the earlier, prose-reimplemented sources
        // all happened to start at address 0 with no 'org'/'entry' at all).
        int jumpTarget = compiled.Ram.EntryPoint ?? 0x000;
        await controller.WriteRamAsync(route, compiled.Ram.Words, cancellationToken);

        // Apply DB013's node-configuration directives ('/a', '/b', '/io'),
        // when the source used them, before jumping -- these are the node's
        // startup register state, so they must land while the node is still
        // parked and puppetable, not after JumpAsync hands control to its own
        // resident program. None of this cluster's four sources currently use
        // '/a'/'/b'/'/io' -- each sets its own registers directly in its own
        // 'start' word instead (including node 107's, which sets B toward
        // node 007 via a plain 'down b!') -- so these three are no-ops today,
        // kept here so a future source that DOES rely on one of them (or a
        // hand-edit to one of these four) is honored automatically rather
        // than silently ignored. '/stack' (up to ten startup data-stack
        // values) is not applied here at all yet -- KrakenSession's
        // WriteParameterStackAsync expects exactly nine words (S plus eight
        // circular cells, T handled separately), and no source here uses
        // '/stack' either, so that reconciliation is deferred until a source
        // actually needs it rather than guessed at now.
        if (compiled.Ram.InitialA is int initialA)
        {
          await controller.WriteAAsync(route, initialA, cancellationToken);
        }

        if (compiled.Ram.InitialB is int initialB)
        {
          await controller.WriteBAsync(route, initialB, cancellationToken);
        }

        if (compiled.Ram.InitialIo is int initialIo)
        {
          await controller.WriteIoAsync(route, initialIo, cancellationToken);
        }

        await controller.JumpAsync(route, jumpTarget, cancellationToken);
        results.Add(new SramClusterInstallNodeResult(coordinate, true, compiled.Ram.Diagnostics));
      }
    }

    return new SramClusterInstallResult(results, masterSupport);
  }

  /// <summary>
  /// Ensures Tentacle 3 is the short, direct path to <paramref name="masterCoordinate"/>
  /// (see <see cref="KrakenTopology.ApplySramMasterTentacle"/>) and that a Kraken is
  /// erected against it. The physical relay wiring is only set at erection time, so
  /// switching which nodes Tentacle 3 covers -- or switching masters, which changes
  /// where the cluster nodes sit relative to the new master -- requires tearing down
  /// and re-erecting: this uses the one sanctioned re-erection path
  /// (<see cref="KrakenLiveController.ResetTransientErectionAsync"/> followed by
  /// <see cref="KrakenLiveController.EnsureOnlineAsync"/>), the same one the Chip
  /// window's own "Install Kraken" button uses. This resets the WHOLE chip (every
  /// node's resident RAM program, not just Tentacle 3's), exactly as any Kraken
  /// erection always has -- Tentacles 1 and 2 keep their full node lists and are
  /// simply re-erected unchanged alongside the new, short Tentacle 3.
  /// A no-op if Tentacle 3 already matches this master's path and a Kraken is
  /// already erected (re-Install for the same master does not reset the chip again).
  /// </summary>
  private async Task ReorganizeTentacleForMasterAsync(
      KrakenLiveController controller, int masterCoordinate, CancellationToken cancellationToken)
  {
    bool changed = KrakenTopology.ApplySramMasterTentacle(_chip.Kraken, masterCoordinate);
    if (!changed && controller.HardwareErected)
    {
      return;
    }

    if (controller.HardwareErected)
    {
      await controller.ResetTransientErectionAsync(cancellationToken);
    }

    IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(_chip.Kraken);
    KrakenNodeRoute? anchor = routes.Values
        .Where(route => !route.IsHead)
        .OrderBy(route => route.TentacleNumber)
        .ThenBy(route => route.Position)
        .FirstOrDefault();
    if (anchor is null)
    {
      throw new InvalidOperationException("No Kraken route is available to erect the SRAM tentacle.");
    }

    await controller.EnsureOnlineAsync(anchor, verifyTarget: false, allowErect: true, cancellationToken);
    await controller.ParkTransportAsync(cancellationToken);
  }
}