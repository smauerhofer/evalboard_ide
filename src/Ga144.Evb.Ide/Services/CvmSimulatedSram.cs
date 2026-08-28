using Ga144.Cvm.Toolchain;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Host-side stand-in for the CVM's backing memory: a 1 Mword (2^20) address space of 16-bit CVM
/// words. Real EVB01/EVB02 hardware for this CVM design would wire an actual SRAM chip behind node 708's
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
  /// 16 bits on the way in -- a CVM word, unlike the 18-bit F18 wire words that carry it across the
  /// serial link, is 16 bits wide (see <see cref="CvmWordCodec"/>).
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
      _words[startAddress + index] = program[index] & CvmWordCodec.WordMask;
    }
  }

  public int Read(int address) => _words[CheckAddress(address)];

  public void Write(int address, int value) => _words[CheckAddress(address)] = value & CvmWordCodec.WordMask;

  /// <summary>
  /// Copies out <paramref name="count"/> consecutive words starting at <paramref name="startAddress"/>
  /// -- built for the CVM Debugger's memory inspector, which needs to display a whole visible range
  /// at once rather than one <see cref="Read"/> call per cell. Read-only: never marks anything as
  /// having been "accessed" by the CVM the way a real wire transaction would.
  /// </summary>
  public IReadOnlyList<int> ReadRange(int startAddress, int count)
  {
    if (startAddress < 0 || count < 0 || (long)startAddress + count > WordCapacity)
    {
      throw new ArgumentOutOfRangeException(nameof(startAddress),
          $"A {count}-word range starting at 0x{startAddress:X6} does not fit in the {WordCapacity:N0}-word simulated SRAM.");
    }

    var result = new int[count];
    Array.Copy(_words, startAddress, result, 0, count);
    return result;
  }

  private static int CheckAddress(int address)
  {
    if (address < 0 || address >= WordCapacity)
    {
      throw new ArgumentOutOfRangeException(nameof(address), $"Address 0x{address:X6} is outside the {WordCapacity:N0}-word simulated SRAM.");
    }

    return address;
  }
}