namespace Ga144.Cvm.Toolchain;

/// <summary>One symbol in a linked <see cref="CvmImage"/>'s own final symbol table: a name and its final, resolved word address -- everything the CVM Debugger needs to label a linked program's own addresses (not just node 607 interpreter internals, which is all it can label today).</summary>
public sealed class CvmImageSymbol
{
  public required string Name { get; init; }
  public required int Address { get; init; }
}

/// <summary>
/// A linked CVM program image (.gaimg): <see cref="CvmLinker"/>'s own output -- a single flat word
/// stream with every relocation already applied and every symbol at its final address, ready to load
/// directly into the CVM's own memory (<c>CvmSimulatedSram.LoadProgram</c> and the real-hardware
/// installer already consume exactly this shape, a <c>List&lt;int&gt;</c> of words, today) plus the
/// final resolved symbol table alongside it for the CVM Debugger to eventually label addresses with.
///
/// On-disk shape (a <see cref="GaffDocument"/> of kind <see cref="GaffFileKind.Image"/>): a "STRT"
/// chunk (the shared string table), an "IMGH" chunk (entry address, word count), a "WORD" chunk (the
/// final flat word stream, packed the same way a <see cref="CvmObjectFile"/> section is), and a "SYMT"
/// chunk (one entry per resolved symbol: name offset, final address).
/// </summary>
public sealed class CvmImage
{
  public required IReadOnlyList<int> Words { get; init; }

  /// <summary>Where execution begins -- word address 0 for a normal, entry-laid-out program (see <see cref="CvmLinker"/>'s own remarks on the <c>br __start</c> vector), but recorded explicitly rather than assumed, since a link with <see cref="CvmLinkOptions.ApplyEntryLayout"/> off has no such fixed convention.</summary>
  public required int EntryAddress { get; init; }

  public required IReadOnlyList<CvmImageSymbol> Symbols { get; init; }

  public void Save(Stream stream)
  {
    var strings = new GaffStringTableBuilder();
    var document = new GaffDocument(GaffFileKind.Image);

    using (var headerPayload = new MemoryStream())
    using (var headerWriter = new BinaryWriter(headerPayload))
    {
      headerWriter.Write((uint)EntryAddress);
      headerWriter.Write((uint)Words.Count);
      document.AddChunk("IMGH", headerPayload.ToArray());
    }

    document.AddChunk("WORD", CvmWordCodec.EncodeAll(Words));

    using (var symbolPayload = new MemoryStream())
    using (var symbolWriter = new BinaryWriter(symbolPayload))
    {
      foreach (CvmImageSymbol symbol in Symbols)
      {
        symbolWriter.Write((uint)strings.Intern(symbol.Name));
        symbolWriter.Write((uint)symbol.Address);
      }

      document.AddChunk("SYMT", symbolPayload.ToArray());
    }

    document.Chunks.Insert(0, new GaffChunk("STRT", strings.ToBytes()));
    document.Save(stream);
  }

  public static CvmImage Load(Stream stream)
  {
    GaffDocument document = GaffDocument.Load(stream);
    if (document.FileKind != GaffFileKind.Image)
    {
      throw new InvalidDataException($"Expected a CVM image file, but this GAFF file's kind is {document.FileKind}.");
    }

    var strings = new GaffStringTableReader(document.GetRequiredChunk("STRT").Payload);

    int entryAddress;
    int wordCount;
    using (var headerReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("IMGH").Payload)))
    {
      entryAddress = (int)headerReader.ReadUInt32();
      wordCount = (int)headerReader.ReadUInt32();
    }

    List<int> words = CvmWordCodec.DecodeAll(document.GetRequiredChunk("WORD").Payload);
    if (words.Count != wordCount)
    {
      throw new InvalidDataException($"This image's \"IMGH\" chunk claims {wordCount} word(s), but its \"WORD\" chunk holds {words.Count}.");
    }

    var symbols = new List<CvmImageSymbol>();
    using (var symbolReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("SYMT").Payload)))
    {
      while (symbolReader.BaseStream.Position < symbolReader.BaseStream.Length)
      {
        string name = strings.ReadAt((int)symbolReader.ReadUInt32());
        int address = (int)symbolReader.ReadUInt32();
        symbols.Add(new CvmImageSymbol { Name = name, Address = address });
      }
    }

    return new CvmImage { Words = words, EntryAddress = entryAddress, Symbols = symbols };
  }
}
