namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 504's resident F18 source -- CVM2's floating-point subprocessor relay: 96 <c>@b !</c> pairs
/// forwarding node 503's (left) output straight on to node 404 (down). Supplied by Stefan 2026-09-26 as
/// part of the complete FP subprocessor rewrite (see <see cref="Node102Program"/>'s own remarks for the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role).
///
/// <b>REPLACES an unrelated, obsolete design.</b> The previously stored <c>Source</c> for this coordinate
/// was a completely different "stage 7 node. final values" (classification/pass/zero/inf/qnan dispatch,
/// <c>fp7/*</c> words); that responsibility has moved into nodes 102-105 and node 204's special-case
/// shortcut, and this coordinate is now a pure relay -- replaced wholesale.
///
/// Added 2026-09-26; earlier revisions of this coordinate predate the current FP subprocessor design.
/// </summary>
internal static class Node504Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor relay, node 503 (left) to node 404 (down). Replaced 2026-09-26.</summary>
  public const int Coordinate = 504;

  /// <summary>
  /// Node 504's full resident F18 source, verbatim from Stefan's 2026-09-26 paste. Replaces the obsolete "stage 7, final values" classification design previously stored here.
  /// </summary>
  public const string Source = """
      ( CVM2 node 504. VM 32 bit floatingpoint relay.

      503 {left} -> 404 {down}

      )

      # left /b
      # down /a
      # 0 /p
      # 0 org

        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b ! 
        @b ! @b ! @b ! @b ! @b ! @b ! @b ! @b !
      """;
}
