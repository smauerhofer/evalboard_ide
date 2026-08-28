namespace Ga144.Cvm.Toolchain;

/// <summary>Who else can see a symbol: <see cref="Local"/> stays private to its own object file, <see cref="Global"/> is defined here and importable elsewhere, <see cref="External"/> is used here but defined elsewhere (another object file, a library, or -- for the 4 built-in CVM instructions -- node 607's own interpreter, resolved via a primitive table at link time).</summary>
public enum CvmSymbolBinding
{
  Local,
  Global,
  External,
}

/// <summary>How a relocation's target word is computed once the symbol's final address is known.</summary>
public enum CvmRelocationType
{
  /// <summary>Write the symbol's resolved address into the word as-is (masked to 18 bits) -- used for a label or import referenced as data, e.g. a pushlit operand that names a symbol instead of a literal.</summary>
  AbsoluteAddress,

  /// <summary>Write 0x8000 | (resolved address &amp; 0x3FFFF) into the word -- the CVM's own opcode convention, used for every one of the 4 built-in instruction mnemonics.</summary>
  CvmOpcode,
}

/// <summary>One named region of a CVM program's word space. Only "CODE" and "DATA" exist today (default section is CODE), but any name is accepted -- the linker decides where each section's words ultimately land in the CVM's flat page/address space.</summary>
public sealed class CvmSection(string name)
{
  public string Name { get; } = name;
  public List<int> Words { get; } = [];
}

/// <summary>One entry in an object file's symbol table. <see cref="SectionName"/>/<see cref="Value"/> (a word offset within that section) are meaningful only when <see cref="Binding"/> isn't <see cref="CvmSymbolBinding.External"/>.</summary>
public sealed class CvmSymbol
{
  public required string Name { get; init; }
  public required CvmSymbolBinding Binding { get; init; }
  public string? SectionName { get; init; }
  public int Value { get; init; }
}

/// <summary>One fixup: at <see cref="WordOffset"/> words into <see cref="SectionName"/>, once <see cref="SymbolName"/>'s final address is known, apply it per <see cref="Type"/>.</summary>
public sealed class CvmRelocation
{
  public required string SectionName { get; init; }
  public required int WordOffset { get; init; }
  public required string SymbolName { get; init; }
  public required CvmRelocationType Type { get; init; }
}

/// <summary>
/// A relocatable CVM object file (.gaobj): one or more <see cref="Sections"/> of not-yet-final words,
/// a <see cref="Symbols"/> table describing what this file defines and what it needs from elsewhere,
/// and a <see cref="Relocations"/> list of the fixups a linker must apply once every symbol has a
/// final address. This is <see cref="CvmAssembler"/>'s output and (eventually) the linker's input --
/// nothing in this class knows or cares how the words were produced.
///
/// On-disk shape (a <see cref="GaffDocument"/> of kind <see cref="GaffFileKind.Object"/>):
/// a "STRT" chunk (the shared string table every name below is an offset into), a "SECT" chunk (one
/// entry per section: its name and its packed words), a "SYMT" chunk (one entry per symbol: name,
/// binding, which section index it belongs to -- or -1 for external -- and its word-offset value),
/// and a "RELO" chunk (one entry per relocation: section index, word offset, symbol index, type).
/// </summary>
public sealed class CvmObjectFile
{
  public List<CvmSection> Sections { get; } = [];
  public List<CvmSymbol> Symbols { get; } = [];
  public List<CvmRelocation> Relocations { get; } = [];

  public CvmSection GetOrAddSection(string name)
  {
    CvmSection? existing = Sections.FirstOrDefault(section => section.Name == name);
    if (existing is not null)
    {
      return existing;
    }

    var section = new CvmSection(name);
    Sections.Add(section);
    return section;
  }

  public void Save(Stream stream)
  {
    var strings = new GaffStringTableBuilder();
    var document = new GaffDocument(GaffFileKind.Object);

    using (var sectionPayload = new MemoryStream())
    using (var sectionWriter = new BinaryWriter(sectionPayload))
    {
      foreach (CvmSection section in Sections)
      {
        sectionWriter.Write((uint)strings.Intern(section.Name));
        sectionWriter.Write((uint)section.Words.Count);
        sectionWriter.Write(CvmWordCodec.EncodeAll(section.Words));
      }

      document.AddChunk("SECT", sectionPayload.ToArray());
    }

    using (var symbolPayload = new MemoryStream())
    using (var symbolWriter = new BinaryWriter(symbolPayload))
    {
      foreach (CvmSymbol symbol in Symbols)
      {
        symbolWriter.Write((uint)strings.Intern(symbol.Name));
        symbolWriter.Write((byte)symbol.Binding);
        symbolWriter.Write(symbol.SectionName is null ? -1 : IndexOfSection(symbol.SectionName));
        symbolWriter.Write(symbol.Value);
      }

      document.AddChunk("SYMT", symbolPayload.ToArray());
    }

    using (var relocationPayload = new MemoryStream())
    using (var relocationWriter = new BinaryWriter(relocationPayload))
    {
      foreach (CvmRelocation relocation in Relocations)
      {
        relocationWriter.Write(IndexOfSection(relocation.SectionName));
        relocationWriter.Write(relocation.WordOffset);
        relocationWriter.Write(IndexOfSymbol(relocation.SymbolName));
        relocationWriter.Write((byte)relocation.Type);
      }

      document.AddChunk("RELO", relocationPayload.ToArray());
    }

    // STRT is emitted last (its final contents aren't known until every name above has been
    // interned) but inserted first in the chunk list, since a reader naturally wants the string
    // table available before it decodes anything that references it.
    document.Chunks.Insert(0, new GaffChunk("STRT", strings.ToBytes()));
    document.Save(stream);
  }

  public static CvmObjectFile Load(Stream stream)
  {
    GaffDocument document = GaffDocument.Load(stream);
    if (document.FileKind != GaffFileKind.Object)
    {
      throw new InvalidDataException($"Expected a CVM object file, but this GAFF file's kind is {document.FileKind}.");
    }

    var strings = new GaffStringTableReader(document.GetRequiredChunk("STRT").Payload);
    var objectFile = new CvmObjectFile();

    using (var sectionReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("SECT").Payload)))
    {
      while (sectionReader.BaseStream.Position < sectionReader.BaseStream.Length)
      {
        string name = strings.ReadAt((int)sectionReader.ReadUInt32());
        int wordCount = (int)sectionReader.ReadUInt32();
        byte[] packedWords = sectionReader.ReadBytes(wordCount * CvmWordCodec.BytesPerWord);
        objectFile.GetOrAddSection(name).Words.AddRange(CvmWordCodec.DecodeAll(packedWords));
      }
    }

    using (var symbolReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("SYMT").Payload)))
    {
      while (symbolReader.BaseStream.Position < symbolReader.BaseStream.Length)
      {
        string name = strings.ReadAt((int)symbolReader.ReadUInt32());
        var binding = (CvmSymbolBinding)symbolReader.ReadByte();
        int sectionIndex = symbolReader.ReadInt32();
        int value = symbolReader.ReadInt32();
        objectFile.Symbols.Add(new CvmSymbol
        {
          Name = name,
          Binding = binding,
          SectionName = sectionIndex < 0 ? null : objectFile.Sections[sectionIndex].Name,
          Value = value,
        });
      }
    }

    using (var relocationReader = new BinaryReader(new MemoryStream(document.GetRequiredChunk("RELO").Payload)))
    {
      while (relocationReader.BaseStream.Position < relocationReader.BaseStream.Length)
      {
        int sectionIndex = relocationReader.ReadInt32();
        int wordOffset = relocationReader.ReadInt32();
        int symbolIndex = relocationReader.ReadInt32();
        var type = (CvmRelocationType)relocationReader.ReadByte();
        objectFile.Relocations.Add(new CvmRelocation
        {
          SectionName = objectFile.Sections[sectionIndex].Name,
          WordOffset = wordOffset,
          SymbolName = objectFile.Symbols[symbolIndex].Name,
          Type = type,
        });
      }
    }

    return objectFile;
  }

  private int IndexOfSection(string name)
  {
    int index = Sections.FindIndex(section => section.Name == name);
    if (index < 0)
    {
      throw new InvalidOperationException($"Section \"{name}\" was referenced but never added to this object file.");
    }

    return index;
  }

  private int IndexOfSymbol(string name)
  {
    int index = Symbols.FindIndex(symbol => symbol.Name == name);
    if (index < 0)
    {
      throw new InvalidOperationException($"Symbol \"{name}\" was referenced by a relocation but never added to this object file's symbol table.");
    }

    return index;
  }
}
