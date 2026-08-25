namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// One step in the CVM cluster's boot LOAD order: which node's program is deposited and started
/// at this step, and which node it is reached "via" -- the node that must already be sitting in
/// its temporary pass-through relay role, with its own B register pointed at THIS node's port, at
/// the moment this step's frame is sent. <c>ViaNodeCoordinate</c> is <c>null</c> only for node
/// 708 itself, the external serial entry point with no "via" -- everything else in this cluster
/// is reached across the mesh through exactly one relay hop.
///
/// Confirmed directly by Stefan (2026-08-25) as the definitive load sequence for the whole tree,
/// leaves first / root last -- exactly the DB013 6.1.2.4 "Root Node Programming" pattern
/// <see cref="CvmBootStreamBuilder"/>'s own remarks had flagged as the open design question this
/// answers:
/// <code>
///   407 (via 507), 506 (via 507), 508 (via 507),  -- 507's three children, while 507 is still
///                                                     just a relay
///   507 (via 607),                                -- 507 itself, now that its children are
///                                                     loaded and running, while 607 is still
///                                                     just a relay
///   606 (via 607), 608 (via 607),                 -- 607's other two children
///   607 (via 707),                                -- 607 itself, last of 607's own subtree
///   707 (via 708),                                -- 707 itself (no resident program yet --
///                                                     "node 707 will come later")
///   708                                            -- the boot node itself, no "via": the
///                                                     external serial entry point
/// </code>
/// This is a POST-ORDER walk of the physical tree (708 -&gt; 707 -&gt; 607 -&gt; {507 -&gt; {407, 506,
/// 508}, 606, 608}): every child finishes loading, and is left running its OWN real program,
/// before its parent gives up its temporary relay role and takes on a real program of its own.
/// See <see cref="CvmBootStreamBuilder"/>'s remarks for why a branching tree needs this ordering
/// at all, unlike Kraken's simple linear tentacles (one predecessor, one successor), where load
/// order and compile-dependency order coincide and no such sequencing question ever arises.
/// </summary>
public sealed record CvmBootLoadStep(int NodeCoordinate, int? ViaNodeCoordinate);
