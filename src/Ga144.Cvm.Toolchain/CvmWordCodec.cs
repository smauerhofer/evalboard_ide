namespace Ga144.Cvm.Toolchain;

/// <summary>
/// Packs/unpacks a 16-bit CVM word to/from exactly 2 bytes, little-endian. A CVM word is NOT the same
/// thing as an F18 wire word: the serial link's own async framing (see
/// Ga144.Evb.Ide.Services.CvmMemoryProtocol.ReadWord / Ga144Node708Probe.EncodeAsynchronousWord) moves
/// 18-bit values 3 bytes at a time because that is the physical F18 hardware's own word width, but
/// the CVM's own opcodes and data -- what actually lives at an address in the simulated/real SRAM,
/// and what this toolchain's object/library/image files store -- are 16 bits wide. The high 2 bits
/// the wire framing carries are always zero for a genuine CVM word; this codec simply never has them
/// to worry about, and files built with it are correspondingly a third smaller than if they reused
/// the wire's own 3-byte layout.
/// </summary>
public static class CvmWordCodec
{
  public const int WordMask = 0xFFFF;
  public const int BytesPerWord = 2;

  public static void Encode(int word, Span<byte> destination)
  {
    if ((uint)word > WordMask)
    {
      throw new ArgumentOutOfRangeException(nameof(word), $"A CVM word must fit in 16 bits (0..0x{WordMask:X4}), got 0x{word:X}.");
    }

    destination[0] = (byte)(word & 0xFF);
    destination[1] = (byte)((word >> 8) & 0xFF);
  }

  public static int Decode(ReadOnlySpan<byte> source) =>
      (source[0] | (source[1] << 8)) & WordMask;

  public static byte[] EncodeAll(IReadOnlyList<int> words)
  {
    var bytes = new byte[words.Count * BytesPerWord];
    for (int index = 0; index < words.Count; index++)
    {
      Encode(words[index], bytes.AsSpan(index * BytesPerWord, BytesPerWord));
    }

    return bytes;
  }

  public static List<int> DecodeAll(ReadOnlySpan<byte> bytes)
  {
    if (bytes.Length % BytesPerWord != 0)
    {
      throw new ArgumentException($"A packed CVM word array's length must be a multiple of {BytesPerWord} bytes, got {bytes.Length}.", nameof(bytes));
    }

    var words = new List<int>(bytes.Length / BytesPerWord);
    for (int offset = 0; offset < bytes.Length; offset += BytesPerWord)
    {
      words.Add(Decode(bytes.Slice(offset, BytesPerWord)));
    }

    return words;
  }
}
