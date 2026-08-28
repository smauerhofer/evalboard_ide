using System.Text;

namespace Ga144.Cvm.Toolchain;

/// <summary>Which of the three CVM toolchain file kinds a <see cref="GaffDocument"/> holds.</summary>
public enum GaffFileKind : ushort
{
  Object = 1,
  Library = 2,
  Image = 3,
}

/// <summary>One RIFF-style chunk: a 4-character tag plus its raw payload bytes.</summary>
public sealed class GaffChunk
{
  public string FourCC { get; }
  public byte[] Payload { get; }

  public GaffChunk(string fourCC, byte[] payload)
  {
    if (fourCC.Length != 4)
    {
      throw new ArgumentException($"A GAFF chunk tag must be exactly 4 characters, got \"{fourCC}\".", nameof(fourCC));
    }

    FourCC = fourCC;
    Payload = payload;
  }
}

/// <summary>
/// The common on-disk envelope shared by every CVM toolchain file (.gaobj/.galib/.gaimg): a 4-byte
/// "GAFF" magic, a format version, a file-kind tag, then a flat sequence of RIFF-style chunks -- a
/// 4-character tag plus a byte length plus that many payload bytes. A reader that doesn't recognize a
/// chunk's tag can always skip it using its length, which is the whole point of this shape: new chunk
/// kinds (a future debug/line-number chunk, say) can be added later without breaking any tool built
/// against an earlier version of a format.
///
/// This class only knows about the envelope. What chunks a valid .gaobj/.galib/.gaimg must contain,
/// and what each chunk's payload means, belongs to <see cref="CvmObjectFile"/> and its future library
/// and image counterparts.
/// </summary>
public sealed class GaffDocument
{
  private const string Magic = "GAFF";
  private const ushort Version = 1;

  public GaffFileKind FileKind { get; }
  public List<GaffChunk> Chunks { get; } = [];

  public GaffDocument(GaffFileKind fileKind) => FileKind = fileKind;

  public GaffChunk? TryGetChunk(string fourCC) => Chunks.FirstOrDefault(chunk => chunk.FourCC == fourCC);

  public GaffChunk GetRequiredChunk(string fourCC) =>
      TryGetChunk(fourCC) ?? throw new InvalidDataException($"Missing required \"{fourCC}\" chunk.");

  public void AddChunk(string fourCC, byte[] payload) => Chunks.Add(new GaffChunk(fourCC, payload));

  public void Save(Stream stream)
  {
    using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
    writer.Write(Encoding.ASCII.GetBytes(Magic));
    writer.Write(Version);
    writer.Write((ushort)FileKind);
    foreach (GaffChunk chunk in Chunks)
    {
      writer.Write(Encoding.ASCII.GetBytes(chunk.FourCC));
      writer.Write((uint)chunk.Payload.Length);
      writer.Write(chunk.Payload);
    }
  }

  public static GaffDocument Load(Stream stream)
  {
    using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
    string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
    if (magic != Magic)
    {
      throw new InvalidDataException($"Not a GAFF file (expected magic \"{Magic}\", got \"{magic}\").");
    }

    ushort version = reader.ReadUInt16();
    if (version != Version)
    {
      throw new InvalidDataException($"Unsupported GAFF format version {version} (this tool understands version {Version}).");
    }

    var fileKind = (GaffFileKind)reader.ReadUInt16();
    var document = new GaffDocument(fileKind);
    while (stream.Position < stream.Length)
    {
      string fourCC = Encoding.ASCII.GetString(reader.ReadBytes(4));
      uint length = reader.ReadUInt32();
      byte[] payload = reader.ReadBytes(checked((int)length));
      document.Chunks.Add(new GaffChunk(fourCC, payload));
    }

    return document;
  }
}

/// <summary>
/// A simple write-once string table: interns strings into one NUL-terminated, UTF-8 blob and hands
/// back each string's byte offset into that blob, for chunks (SECT/SYMT names) to reference instead
/// of repeating string bytes inline.
/// </summary>
public sealed class GaffStringTableBuilder
{
  private readonly List<byte> _bytes = [];
  private readonly Dictionary<string, int> _offsets = [];

  public int Intern(string value)
  {
    if (_offsets.TryGetValue(value, out int existingOffset))
    {
      return existingOffset;
    }

    int offset = _bytes.Count;
    _bytes.AddRange(Encoding.UTF8.GetBytes(value));
    _bytes.Add(0);
    _offsets[value] = offset;
    return offset;
  }

  public byte[] ToBytes() => [.. _bytes];
}

/// <summary>Reads strings back out of a <see cref="GaffStringTableBuilder"/>'s serialized "STRT" chunk payload.</summary>
public sealed class GaffStringTableReader(byte[] bytes)
{
  public string ReadAt(int offset)
  {
    int end = Array.IndexOf(bytes, (byte)0, offset);
    if (end < 0)
    {
      throw new InvalidDataException($"String table entry at offset {offset} is not NUL-terminated.");
    }

    return Encoding.UTF8.GetString(bytes, offset, end - offset);
  }
}
