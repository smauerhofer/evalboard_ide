using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Host-side stand-in for the CVM's backing memory: a 1 Mword (2^20), 18-bit-word address space.
/// Real EVB01/EVB02 hardware for this CVM design would wire an actual SRAM chip behind node 708's
/// async serial link (the same general shape AN003's SRAM Control Cluster describes, just talked
/// to more directly by this newer design); this class is what answers node 607's read/write
/// requests during testing, with no physical SRAM attached -- see
/// <see cref="Ga144CvmHardwareInstaller"/>'s own remarks for how those requests are decoded and
/// serviced against an instance of this class.
///
/// This is a plain in-memory model, not a hardware simulator in any deeper sense: it only exists
/// so a test program can be preloaded once (<see cref="LoadProgram"/>) and then answered
/// word-for-word exactly as real SRAM would, including recording whatever the CVM writes back into
/// it mid-run.
/// </summary>
public sealed class CvmSimulatedSram
{
  /// <summary>1 Mword, matching the CVM's 20-bit address space (Stefan: "a real SRAM memory with 1 Mword capacity").</summary>
  public const int WordCapacity = 1 << 20;

  private readonly int[] _words = new int[WordCapacity];

  /// <summary>
  /// Copies <paramref name="program"/> into the simulated SRAM starting at <paramref name="startAddress"/>
  /// (0 by default) -- Stefan's step 1, done once before the CVM is started. Each word is masked to
  /// 18 bits on the way in, same as everywhere else words cross this project's own host/chip boundary.
  /// </summary>
  public void LoadProgram(IReadOnlyList<int> program, int startAddress = 0)
  {
    ArgumentNullException.ThrowIfNull(program);
    if (startAddress < 0 || (long)startAddress + program.Count > WordCapacity)
    {
      throw new ArgumentOutOfRangeException(nameof(startAddress),
          $"A {program.Count}-word program starting at 0x{startAddress:X6} does not fit in the {WordCapacity:N0}-word simulated SRAM.");
    }

    for (int index = 0; index < program.Count; index++)
    {
      _words[startAddress + index] = program[index] & F18InstructionSet.WordMask;
    }
  }

  public int Read(int address) => _words[CheckAddress(address)];

  public void Write(int address, int value) => _words[CheckAddress(address)] = value & F18InstructionSet.WordMask;

  private static int CheckAddress(int address)
  {
    if (address < 0 || address >= WordCapacity)
    {
      throw new ArgumentOutOfRangeException(nameof(address), $"Address 0x{address:X6} is outside the {WordCapacity:N0}-word simulated SRAM.");
    }

    return address;
  }
}
