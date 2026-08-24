namespace Ga144.Evb.Ide.Services;

/// <summary>
/// A pure-software stand-in for the AN003 SRAM cluster (<see cref="SramClusterPrograms"/>/
/// <see cref="SramClusterInstaller"/>), resident entirely on node 707 -- the head-adjacent
/// node at Tentacle 1 position 0 (<c>KrakenTopology.Tentacle1Nodes[0]</c>). Built to let CVM
/// (C virtual machine) development exercise SRAM-shaped reads/writes/compare-exchanges over
/// Kraken without the real external SRAM cluster (Tentacle 3, nodes 007/008/009/107, an actual
/// Cypress CY62167EV18LL chip) wired up or even installed.
///
/// <b>Why this reuses the real SRAM protocol unchanged.</b> This node's five resident
/// subroutines -- <c>sram-read</c>/<c>sram-write</c>/<c>sram-cx</c>/<c>sram-mask</c>/<c>echo</c>
/// -- deliberately keep the EXACT stack signatures <see cref="SramClusterPrograms.BuildMasterSupportSource"/>
/// defines for a real memory-master node (106/108/207), which means the existing, unmodified
/// host-side leaf builders (<c>KrakenSramProtocol.BuildSramReadWord</c>/<c>BuildSramWriteWord</c>/
/// <c>BuildSramCompareExchange</c>/<c>BuildSramSetMask</c>/<c>BuildEchoTest</c>) and the existing,
/// unmodified generic <c>route</c>-parameterized methods on <c>KrakenSession</c>/
/// <c>KrakenLiveController</c> (<c>ReadSramWordAsync</c>/<c>WriteSramWordAsync</c>/
/// <c>CompareExchangeSramWordAsync</c>/<c>SetSramMasterMaskAsync</c>/<c>EchoTestAsync</c>, each
/// already taking an explicit <c>KrakenNodeRoute</c> plus <c>subroutineAddress</c> -- never
/// hardcoded to 106/108/207) work against node 707 with zero changes. Nothing new needed to be
/// written on the host-transport side at all: only this node's own resident source, and a small
/// installer (<see cref="SramSimulatorInstaller"/>) that deploys just this one node instead of
/// the master-plus-four-node real cluster. CVM code exercised against this simulator can later
/// point at the real cluster (a different <c>subroutineAddress</c> set, from
/// <see cref="SramClusterInstaller"/> against a real master) with no protocol-level changes.
///
/// <b>Why this skips the master/007/008/009/107 layering entirely.</b> The real cluster needs
/// four separate nodes because AN003's node 107 talks to physically real SRAM control/address/data
/// pins across three more nodes (007/008/009) that a memory-master node (106/108/207) reaches by
/// relaying over a wire. There is no physical chip here to interface with -- node 707 simply reads
/// and writes its OWN local RAM directly, so it plays both the "master" role (Kraken puppets it
/// exactly like a real master, via the resident-support-code redesign: deployed with
/// <c>KrakenLiveController.WriteRamAsync</c> ONLY, never <c>JumpAsync</c>, so its P register never
/// leaves its incoming port and it stays puppetable indefinitely) and the "memory" role in one node,
/// with no B-port handshake, no Tentacle 3 reorganization, and no risk of stranding any other node --
/// 707 is already Tentacle 1 position 0 in the default fixed topology (<c>KrakenTopology</c>), so it
/// is always reachable the moment ANY Kraken is erected, independent of whatever Tentacle 3 is doing
/// for the real SRAM cluster.
///
/// <b>Backing store.</b> A node has only 64 RAM words total, shared between this simulator's own
/// code and its backing array -- nowhere near the real chip's 1M x 16 words. <see cref="CapacityWords"/>
/// (16 words, <see cref="CapacityMask"/> = 0x0F) is deliberately small: this is test/verification
/// scaffolding for CVM development, not a capacity claim. <c>ex@</c>/<c>ex!</c>/<c>cx?</c>'s 20-bit
/// page:address addressing is accepted (so the wire format matches the real cluster exactly) but the
/// "page" component is simply discarded -- the simulator has exactly one page, sized
/// <see cref="CapacityWords"/> words, addressed by <paramref name="address"/> masked to
/// <see cref="CapacityMask"/>. Widening this later (bumping <see cref="CapacityWords"/>, or spreading
/// the backing array across additional free Tentacle-1 nodes such as 706/705) is a follow-up, not a
/// redesign -- the host-side protocol and installer shape do not change.
///
/// <b><c>mk!</c> is a protocol no-op here</b>, for the same reason AN003 section 6.3's degenerate
/// single-fixed-master node 107 made it one (see <see cref="SramClusterPrograms.Node107Interface"/>'s
/// remarks): with exactly one simulated interface and no polling of multiple masters, there is nothing
/// for a mask to enable, disable, or post a stimulus for. <c>sram-mask</c> still exists and is still
/// recognised on the wire -- it just echoes the mask back, exactly like the real degenerate node 107
/// used to.
///
/// <b>Verification.</b> Compiled with zero diagnostics against this project's own real
/// <c>Compiler/F18Compiler.cs</c> (via <c>F18CompilerOptions.ForRam(707)</c>) in a standalone,
/// non-WPF <c>net10.0</c> console harness built for this purpose: 35 of 64 RAM words used (16 for the
/// backing array, 19 for all five subroutines), well inside budget, with headroom to grow. This
/// confirms the F18 syntax is valid and the image fits -- it does NOT substitute for exercising the
/// actual ex@/ex!/cx?/mk!/echo behavior against real or emulated running silicon, which has not been
/// done (this project has no F18 instruction-level simulator yet; only the compiler itself was
/// exercised). Each subroutine's stack effect was hand-traced against <see cref="SramClusterPrograms"/>'s
/// own real host-side push order (including AN003's inversion convention for write/compare-exchange
/// arguments, which this simulator must undo itself with 'inv' -- see the remarks on
/// <see cref="Source"/> -- since there is no node 107 downstream to interpret it).
/// </summary>
internal static class SramSimulatorPrograms
{
  /// <summary>The node this simulator is always deployed to -- Tentacle 1 position 0, one hop from the Kraken head (708).</summary>
  public const int SimulatedCoordinate = 707;

  /// <summary>Size of the simulated backing store, in 16-bit words. Small and deliberate -- see the class remarks.</summary>
  public const int CapacityWords = 16;

  /// <summary>Bitmask (<see cref="CapacityWords"/> - 1) used to fold any 16-bit address into the backing array. <see cref="CapacityWords"/> must stay a power of two for this to mask correctly.</summary>
  public const int CapacityMask = CapacityWords - 1;

  /// <summary>
  /// Node 707's resident source: a zero-initialized <see cref="CapacityWords"/>-word backing array
  /// (label <c>sim-mem</c>, occupying addresses 0x000..0x00F) followed by the five subroutines. No
  /// <c>entry</c>/<c>org</c> directives are used, matching
  /// <see cref="SramClusterPrograms.BuildMasterSupportSource"/>'s own convention -- nothing here is
  /// ever started with <c>JumpAsync</c>, so the compiler's default entry point (the first <c>:</c>
  /// definition) is resolved but never used.
  ///
  /// Argument convention matches <see cref="SramClusterPrograms.BuildMasterSupportSource"/> exactly
  /// (see the class remarks on why): each subroutine consumes whatever
  /// <c>KrakenSramProtocol</c>'s existing leaf builders already push, in the same order, including
  /// AN003's sign-inversion convention for write/compare-exchange addresses -- but where the real
  /// master subroutines just relay the (still inverted) words on to node 107 over B for IT to
  /// interpret, these subroutines have to recover the true value themselves with 'inv' before using
  /// it as an array index, since there is no downstream node 107 to do that decoding.
  ///
  /// <c>sram-read ( addr page -- w )</c>: drops the page, masks the address into the backing array's
  /// window, and fetches. <c>sram-write ( value addr page -- value )</c>: drops the page, un-inverts
  /// and masks the address, stores, and echoes the value back (no protocol reply is defined for ex!,
  /// matching <c>KrakenProtocol.BuildWriteA</c>/<c>BuildWriteMemory</c>'s own echo-the-value
  /// convention). <c>sram-cx ( w a p n -- f )</c>: un-inverts the compare value, drops the page,
  /// masks the address once (reused for both the fetch and, on a match, the store -- A is never
  /// reassigned in between), compares with 'xor' (zero means equal), and on a match stores the new
  /// value and returns 0xFFFF, or on a mismatch leaves memory untouched and returns 0. <c>sram-mask
  /// ( m f x -- m )</c>: a deliberate protocol no-op (see the class remarks) that just echoes the
  /// mask. <c>echo ( n -- n+1 )</c>: identical to the real master support code's diagnostic-only
  /// word, unchanged -- it never touched B or node 107 even in the real design, so there is nothing
  /// simulator-specific about it at all.
  /// </summary>
  public static string BuildSource()
  {
    // CapacityMask is interpolated (rather than hardcoded in the source text) so the
    // backing array's declared size and the mask every subroutine uses to fold an
    // address into it can never drift apart if CapacityWords ever changes.
    string mask = $"0x{CapacityMask:X}";
    string zeros = string.Join(" , ", Enumerable.Repeat("0", CapacityWords)) + " ,";

    return $$"""
        label sim-mem
        [
          {{zeros}}
        ]

        : sram-read ( addr page -- w )
          drop
          {{mask}} and a!
          @ ;

        : sram-write ( value addr page -- value )
          drop
          inv {{mask}} and a!
          dup !
          ;

        : sram-cx ( w a p n -- f )
          inv >r
          drop
          {{mask}} and a!
          @
          r>
          xor if
            drop 0
          else
            ! 0xFFFF
          then
          ;

        : sram-mask ( m f x -- m )
          drop
          drop
          ;

        : echo ( n -- n+1 )
          1 + ;
        """;
  }
}
