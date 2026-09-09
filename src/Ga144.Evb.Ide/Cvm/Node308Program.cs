namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 308's resident F18 source -- CVM2's 4x 32-bit "VM 32 arithmetic" (double-word) register node,
/// reached from node 307's own LEFT port ("1101_11??_????_????", <c># right /b</c> on this node's own
/// side -- the same alternating-mirror local-port-name pattern used throughout this mesh). Imports node
/// 307 (<c>k/pop</c>/<c>k/push</c>/<c>k/leave</c>). BRAND NEW to this toolchain: added 2026-09-09 as part
/// of the opcode/assembler-vs-node reconciliation audit against Stefan's <c>workspace.yaml</c> project
/// export -- no earlier revision of this file existed here.
///
/// Holds four 32-bit "d" registers as word pairs (low word in <c>x</c>, high word in <c>x+1</c>, per the
/// header's own second/third lines) in its own local RAM. <c>d/main</c> uses the SAME "mask low bits into
/// a register index, mask+offset the rest into a jump target" direct-field-extraction dispatch shape as
/// node 511's own <c>r/main</c> (<see cref="Node511Program"/>), just with a 2-bit register field
/// (<c>0x03 and</c>, four registers, not 511's 32) instead of 511's 5-bit one, and no separate
/// <c># 0x20 org</c>-style reservation (this node's own code starts at <c># 0x08 org</c> instead). Each of
/// <c>'dpop</c>/<c>'dpush</c>/<c>'dinc</c>/<c>'ddec</c>/<c>'dadd</c>/<c>'dor</c> is reached by direct jump
/// to its own compiled address, register index embedded in the low 2 bits of the CVM opcode word --
/// <see cref="CvmInstructionSet.CvmOperandEncoding.NodeResolvedEmbeddedValue"/>, the same shape node 511's
/// own four register-file ops use.
/// </summary>
internal static class Node308Program
{
  /// <summary>The node this program is always deployed to -- CVM2's 4x 32-bit "VM 32 arithmetic" register node.</summary>
  public const int Coordinate = 308;

  /// <summary>
  /// Node 308's full resident F18 source, verbatim from Stefan's own <c>workspace.yaml</c> project export
  /// (the "CVM2" project's Host chip), added 2026-09-09 as part of the opcode/assembler-vs-node
  /// reconciliation audit.
  /// </summary>
  public const string Source = """
      ( CVM2 node 308. VM 32 arithmetic, 1101_11??_????_???? )
      ( contains 4 32-bit address register )
      ( low word in x, high word in x+1 )
      # 307 import
      # 0x08 org
      entry d/main
      # 0 /a
      # left /b

      : d/inc 1 . + ;
      : d/dec -1 . + ;
      : d/!+ 0xffff and !+ ;
      : d/pop ( -w) A[ k/pop ]] lit !b A[ !p ]] lit !b @b ;
      : d/push ( w) A[ @p k/push ]] lit !b !b ;

      : d/leave A[ k/leave ; ]] lit !b
      : d/main  A[ 2* !p !p ]] lit !b @b
        @b
        // set register in a
        dup 0x03 and 2* a!
        // call word
        2/ 2/ 0x3f and ex d/leave ;

      : 'dpop d/pop !+ d/pop ! ;
      : 'dpush @+ @ d/push d/push ;
      : 'dinc @ d/inc dup d/!+ 0x10000 and # d/leave until @ d/inc d/!+ ;
      : 'ddec @ d/dec dup d/!+ # d/leave -until @ d/dec d/!+ ;
      : 'dadd @ d/pop + dup d/!+ 2* -if dup xor d/inc else dup xor then @ d/pop + . + d/!+ ;
      : 'dor @+ inv @ inv and inv d/push

      (
      opcode dpop  1101_11??_????_??aa pop from stack into register d[a]
      opcode dpush 1101_11??_????_??aa push register d[a] onto stack
      opcode dinc  1101_11??_????_??aa increment register d[a]
      opcode ddec  1101_11??_????_??aa decrement register d[a]
      opcode dadd  1101_11??_????_??aa add register d[a] with stack
      opcode dor   1101_11??_????_??aa or hi & lo part of r[a] and push result on stack. used to detect if a register is 0
      )
      """;
}
