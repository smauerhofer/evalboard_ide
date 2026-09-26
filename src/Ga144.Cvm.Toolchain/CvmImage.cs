namespace Ga144.Cvm.Toolchain;

/// <summary>One symbol in a linked <see cref="CvmImage"/>'s own final symbol table: a name and its final, resolved word address -- everything the CVM Debugger needs to label a linked program's own addresses (not just node 607 interpreter internals, which is all it can label today).</summary>
public sealed class CvmImageSymbol
{
  public required string Name { get; init; }
  public required int Address { get; init; }

  /// <summary>
  /// Added 2026-09-26, alongside the C Debugger's own source-line annotation (see <c>Ga144.Evb.Ide.
  /// ViewModels.CDebuggerViewModel</c>'s remarks): which linked object (<see cref="CvmLinkObjectInput.DisplayName"/>)
  /// this symbol came from, or null for one derived from a primitive-table entry rather than any linked
  /// object's own symbol table. Every entry gets this now (see <see cref="CvmLinker"/>'s own remarks on
  /// <c>imageSymbolList</c>), including the pre-existing GLOBAL/exported symbols -- purely additive, so
  /// nothing that only ever read <see cref="Name"/>/<see cref="Address"/> is affected. It exists because,
  /// as of the same date, <see cref="CvmLinker"/> ALSO starts including every LOCAL (non-exported) symbol
  /// in the final image (previously dropped entirely -- see that class's own remarks), and a compiler
  /// that emits a same-named local symbol in more than one linked object (the C compiler's own
  /// per-statement source-comment labels, "__srcline_0", "__srcline_1", ... -- deliberately NOT globally
  /// unique, since they only ever need to be unique within their own object) would otherwise be
  /// impossible to tell apart by name alone once several objects are linked together. A consumer that
  /// only cares about ONE specific object's own local symbols (exactly what the C Debugger needs, to
  /// resolve its own source-comment labels and no one else's) filters on this field; a consumer that
  /// only ever dealt with exported/global names (every existing caller before this date) can keep
  /// ignoring it, since a Global symbol's name was already unique across the whole link by construction.
  /// <b>Breaking on-disk change</b> to the "SYMT" chunk below, exactly like every other <c>CvmImage</c>/
  /// <c>CvmObjectFile</c> shape change in this project's own history (see e.g. <see
  /// cref="CvmRelocation.EmbeddedValue"/>'s own remarks) -- a <c>.gaimg</c> saved before this field
  /// existed has no word for it; re-link from source rather than loading an old image.
  /// </summary>
  public string? DefiningObjectName { get; init; }

  /// <summary>
  /// Added 2026-09-26, alongside <see cref="DefiningObjectName"/> and for the same reason: once
  /// <see cref="CvmLinker"/> started including every LOCAL symbol in the final image (not just
  /// Global/exported ones and primitive-table entries), a consumer that labels addresses for a human to
  /// read -- the CVM Debugger's own pre-existing "&lt;name&gt;" memory-view annotation -- needed a way to
  /// tell the two apart again: every Local symbol a real C or assembly program defines is a
  /// compiler-internal implementation detail (a loop/branch label, a per-statement source-comment label,
  /// a string-literal or "static" local's own mangled name -- see <see cref="CCodeGenerator"/>'s own
  /// label-naming remarks), never something a person watching that view would want to see listed
  /// alongside the handful of real Global names (functions, exported globals) that annotation was
  /// designed for. <c>true</c> for every entry <see cref="CvmLinker"/> resolved from its own
  /// <c>finalAddress</c> table (a Global symbol or a primitive-table mnemonic -- exactly what this field
  /// would always have been had it existed before Local symbols were added at all); <c>false</c> for
  /// every Local symbol added alongside it. A consumer that wants every symbol regardless (the C
  /// Debugger's own <see cref="DefiningObjectName"/>-filtered lookup, which needs its own Local
  /// source-comment labels specifically) simply ignores this field.
  /// </summary>
  public required bool IsExported { get; init; }
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
        // DefiningObjectName (added 2026-09-26): interned as an empty string for null, exactly like
        // every other "no name here" case a GaffStringTableBuilder round-trips in this project --
        // there is no legitimate linked object whose own DisplayName is the empty string, so this is
        // unambiguous on the way back in.
        symbolWriter.Write((uint)strings.Intern(symbol.DefiningObjectName ?? string.Empty));
        // IsExported (added 2026-09-26, same date/reason as DefiningObjectName above): a single byte is
        // plenty for a bool, but every other field in this chunk is a fixed-width uint -- keeping this
        // one the same width avoids adding a second, oddly-sized field to an otherwise uniform
        // fixed-record layout for no real space saving (one linked image's symbol table is never large
        // enough for 3 bytes/entry to matter).
        symbolWriter.Write((uint)(symbol.IsExported ? 1 : 0));
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
        string definingObjectName = strings.ReadAt((int)symbolReader.ReadUInt32());
        bool isExported = symbolReader.ReadUInt32() != 0;
        symbols.Add(new CvmImageSymbol
        {
          Name = name,
          Address = address,
          DefiningObjectName = definingObjectName.Length == 0 ? null : definingObjectName,
          IsExported = isExported
        });
      }
    }

    return new CvmImage { Words = words, EntryAddress = entryAddress, Symbols = symbols };
  }
}