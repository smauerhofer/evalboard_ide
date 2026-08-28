namespace Ga144.Cvm.Toolchain;

/// <summary>One member of a <see cref="CvmLibrary"/>: a name (conventionally the original .gaobj's file name) plus that object file's own complete, still-GAFF-encoded bytes, unmodified.</summary>
public sealed class CvmLibraryMember
{
  public required string Name { get; init; }
  public required byte[] ObjectBytes { get; init; }
}

/// <summary>
/// A CVM library (.galib): an archive of complete <see cref="CvmObjectFile"/> members plus a symbol
/// index -- the same idea as a Unix "ar" archive's own symbol table ("ranlib"): a map from every
/// member's exported (Global) symbol name straight to which member defines it, so a linker can pull
/// in only the object files a program actually references without opening and parsing every member
/// up front.
///
/// A member's bytes are stored exactly as its own .gaobj file already serializes itself -- a library
/// is deliberately just "several object files, concatenated, plus an index," never a different
/// encoding of an object file's own content, which keeps <see cref="CvmObjectFile"/> the one place
/// that format needs to be understood.
///
/// On-disk shape (a <see cref="GaffDocument"/> of kind <see cref="GaffFileKind.Library"/>): a "STRT"
/// chunk (member names and indexed symbol names), a "MEMB" chunk (one entry per member: its name and
/// where its bytes sit in "BLOB"), a "BLOB" chunk (every member's raw bytes, concatenated), and a
/// "SYMX" chunk (one entry per indexed Global symbol: its name and which member defines it).
/// </summary>
public sealed class CvmLibrary
{
  public List<CvmLibraryMember> Members { get; } = [];

  /// <summary>
  /// The archive's Global-symbol index (symbol name -&gt; defining member's name). Empty until
  /// <see cref="Save"/> successfully computes it or <see cref="Load"/> reads a previously-computed
  /// one back in; mutating <see cref="Members"/> after that does not update it until the next Save.
  /// </summary>
  public IReadOnlyDictionary<string, string> SymbolIndex { get; private set; } = new Dictionary<string, string>();

  /// <summary>
  /// Parses every current <see cref="Members"/> entry as a <see cref="CvmObjectFile"/> and builds
  /// the symbol name -&gt; member name index those Global symbols will need for linking. Returns a
  /// null index with errors (never throws) when a member's bytes aren't a valid CVM object file, two
  /// members share a name, or two members both define the same Global symbol -- the last of which
  /// would leave a linker unable to tell which member it should actually pull in.
  /// </summary>
  public (IReadOnlyDictionary<string, string>? SymbolIndex, IReadOnlyList<string> Errors) BuildSymbolIndex()
  {
    var errors = new List<string>();
    var seenMemberNames = new HashSet<string>(StringComparer.Ordinal);
    var index = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (CvmLibraryMember member in Members)
    {
      if (!seenMemberNames.Add(member.Name))
      {
        errors.Add($"member \"{member.Name}\" appears more than once in this archive.");
        continue;
      }

      CvmObjectFile objectFile;
      try
      {
        objectFile = CvmObjectFile.Load(new MemoryStream(member.ObjectBytes));
      }
      catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
      {
        errors.Add($"member \"{member.Name}\" is not a valid CVM object file: {exception.Message}");
        continue;
      }

      foreach (CvmSymbol symbol in objectFile.Symbols.Where(symbol => symbol.Binding == CvmSymbolBinding.Global))
      {
        if (index.TryGetValue(symbol.Name, out string? existingMember))
        {
          errors.Add($"symbol \"{symbol.Name}\" is exported by both \"{existingMember}\" and \"{member.Name}\" -- a linker couldn't tell which one to use.");
          continue;
        }

        index[symbol.Name] = member.Name;
      }
    }

    return errors.Count > 0 ? (null, errors) : (index, errors);
  }

  /// <summary>Recomputes and validates <see cref="SymbolIndex"/> via <see cref="BuildSymbolIndex"/>, then writes the archive if that succeeds. Leaves the stream untouched on failure.</summary>
  public (bool Success, IReadOnlyList<string> Errors) Save(Stream stream)
  {
    (IReadOnlyDictionary<string, string>? index, IReadOnlyList<string> errors) = BuildSymbolIndex();
    if (index is null)
    {
      return (false, errors);
    }

    SymbolIndex = index;

    var strings = new GaffStringTableBuilder();
    var document = new GaffDocument(GaffFileKind.Library);

    using (var blobPayload = new MemoryStream())
    using (var memberPayload = new MemoryStream())
    using (var memberWriter = new BinaryWriter(memberPayload))
    {
      foreach (CvmLibraryMember member in Members)
      {
        memberWriter.Write((uint)strings.Intern(member.Name));
        memberWriter.Write((uint)blobPayload.Position);
        memberWriter.Write((uint)member.ObjectBytes.Length);
        blobPayload.Write(member.ObjectBytes);
      }

      document.AddChunk("MEMB", memberPayload.ToArray());
      document.AddChunk("BLOB", blobPayload.ToArray());
    }

    using (var symbolPayload = new MemoryStream())
    using (var symbolWriter = new BinaryWriter(symbolPayload))
    {
      foreach ((string symbolName, string memberName) in SymbolIndex)
      {
        symbolWriter.Write((uint)strings.Intern(symbolName));
        symbolWriter.Write((uint)strings.Intern(memberName));
      }

      document.AddChunk("SYMX", symbolPayload.ToArray());
    }

    document.Chunks.Insert(0, new GaffChunk("STRT", strings.ToBytes()));
    document.Save(stream);
    return (true, []);
  }

  public static CvmLibrary Load(Stream stream)
  {
    GaffDocument document = GaffDocument.Load(stream);
    if (document.FileKind != GaffFileKind.Library)
    {
      throw new InvalidDataException($"Expected a CVM library, but this GAFF file's kind is {document.FileKind}.");
    }

    var strings = new GaffStringTableReader(document.GetRequiredChunk("STRT").Payload);
    byte[] blob = document.GetRequiredChunk("BLOB").Payload;
    var library = new CvmLibrary();

    using (var memberReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("MEMB").Payload)))
    {
      while (memberReader.BaseStream.Position < memberReader.BaseStream.Length)
      {
        string name = strings.ReadAt((int)memberReader.ReadUInt32());
        int dataOffset = (int)memberReader.ReadUInt32();
        int dataLength = (int)memberReader.ReadUInt32();
        var objectBytes = new byte[dataLength];
        Array.Copy(blob, dataOffset, objectBytes, 0, dataLength);
        library.Members.Add(new CvmLibraryMember { Name = name, ObjectBytes = objectBytes });
      }
    }

    var symbolIndex = new Dictionary<string, string>(StringComparer.Ordinal);
    using (var symbolReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("SYMX").Payload)))
    {
      while (symbolReader.BaseStream.Position < symbolReader.BaseStream.Length)
      {
        string symbolName = strings.ReadAt((int)symbolReader.ReadUInt32());
        string memberName = strings.ReadAt((int)symbolReader.ReadUInt32());
        symbolIndex[symbolName] = memberName;
      }
    }

    library.SymbolIndex = symbolIndex;
    return library;
  }
}
