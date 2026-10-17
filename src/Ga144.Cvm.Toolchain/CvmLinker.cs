namespace Ga144.Cvm.Toolchain;

/// <summary>One object file handed to <see cref="CvmLinker.Link"/>, named for diagnostics ("multiple definition of X in both A and B") -- conventionally the input .gaobj's own file name, or "archive(member)" for one pulled in from a <see cref="CvmLibrary"/>.</summary>
public sealed record CvmLinkObjectInput(string DisplayName, CvmObjectFile ObjectFile);

/// <summary>One library search path handed to <see cref="CvmLinker.Link"/>, named for diagnostics the same way <see cref="CvmLinkObjectInput"/> is.</summary>
public sealed record CvmLinkLibraryInput(string DisplayName, CvmLibrary Library);

/// <summary>
/// Options controlling one <see cref="CvmLinker.Link"/> run. The only layout this linker knows how to
/// produce today is the CVM ABI's own program entry/exit convention
/// (<c>claude/cvm-abi.md</c> section 1, dictated 2026-09-08, relaxed 2026-09-08): word 0 holds a
/// synthesized <c>br</c> to <see cref="EntrySymbolName"/>, <see cref="ExitSymbolName"/> is required to
/// resolve to address 1 exactly, and <see cref="EntrySymbolName"/> only needs to resolve to SOME
/// address that single <c>br</c> word can actually reach -- it does not need to sit next to
/// <see cref="ExitSymbolName"/> at all. See <see cref="CvmLinker"/>'s own remarks for exactly what that
/// reachability check is. <see cref="ApplyEntryLayout"/> turns all of that off for a link that doesn't
/// want it (linking a stand-alone test program that never reaches the CVM ABI's own env, say) -- CODE
/// then simply starts at address 0 like any other section, with no synthesized vector word and no
/// __start/__exit checks.
/// </summary>
public sealed class CvmLinkOptions
{
  public const string DefaultEntrySymbolName = "__start";
  public const string DefaultExitSymbolName = "__exit";

  public bool ApplyEntryLayout { get; init; } = true;
  public string EntrySymbolName { get; init; } = DefaultEntrySymbolName;
  public string ExitSymbolName { get; init; } = DefaultExitSymbolName;

  /// <summary>
  /// ADDED 2026-10-17, for the CVM_pipeline C ABI v2's "globals on a dedicated external-memory page"
  /// decision (see <c>claude/cvm-abi.md</c>'s ABI v2 section): maps a SECTION NAME (e.g. "DATA") to the
  /// fixed external-memory PAGE NUMBER (<see cref="CvmLinker.PageWordCount"/> words per page -- see that
  /// constant's own remarks for why this value must stay in lockstep with <c>Ga144.Evb.Ide.Services.
  /// CvmMemoryProtocol</c>'s own <c>Page0WordCount</c>/<c>CombineAddress</c>) that section's words
  /// should be placed at, instead of being concatenated immediately after "CODE" into the single
  /// implicit page-0 word stream the way every section was placed before this option existed.
  ///
  /// A section name with NO entry here (or an explicit entry of 0) keeps the EXACT pre-existing
  /// behavior -- concatenated into the shared page-0 word stream, in the same section-name-first-
  /// encountered, then-by-linked-object order this linker has always used -- so every existing caller
  /// that never sets this property (every link before this date) is completely unaffected. Only a
  /// section explicitly mapped to a page &gt; 0 is pulled out to that page's own fixed base address
  /// (<see cref="CvmLinker.Link"/>'s own "Paged sections" remarks cover exactly how). Empty by default.
  /// </summary>
  public IReadOnlyDictionary<string, int> SectionPages { get; init; } = new Dictionary<string, int>();

  /// <summary>
  /// ADDED 2026-10-17, per Stefan's own ask ("to determine the start of the heap I need to know in the
  /// program the end of the DATA section. my suggestion is to use a 32-bit variable that holds the
  /// address and the linker adds this variable with a specific name to the end of the DATA section"):
  /// when this and <see cref="HeapStartAfterSectionName"/> are both set, <see cref="CvmLinker.Link"/>
  /// synthesizes a brand-new, 2-word, pointer-shaped Global symbol under this name, placed immediately
  /// after <see cref="HeapStartAfterSectionName"/>'s own final word. Its VALUE (not its own address) is
  /// the address of that first free word -- i.e. exactly where a heap allocator's free list can start --
  /// written in the SAME (address-low-word-at-the-lower-address, page-high-word-at-the-higher-address)
  /// shape this ABI already uses for every other pointer (<c>claude/cvm-abi.md</c> section 3.3), so a C
  /// declaration of the matching type (<c>extern void *&lt;this name&gt;;</c>) reads it directly as a
  /// ready-to-use heap-start pointer -- no pointer arithmetic needed on the C side, and nothing for the
  /// compiler itself to hardcode, since the real address depends on how large THIS PARTICULAR program's
  /// own DATA section turned out to be. Null (the default) leaves the linker's output completely
  /// unchanged -- the exact pre-existing behavior for every link that doesn't ask for this.
  /// </summary>
  public string? HeapStartSymbolName { get; init; }

  /// <summary>Which section's own final word <see cref="HeapStartSymbolName"/>'s marker is placed immediately after -- see that property's own remarks. Ignored (and no marker is synthesized) when <see cref="HeapStartSymbolName"/> is null.</summary>
  public string? HeapStartAfterSectionName { get; init; }
}

/// <summary>
/// The CVM linker (<c>galink</c>'s own engine): given one or more relocatable <see cref="CvmObjectFile"/>s
/// (plus optional <see cref="CvmLibrary"/> search paths, consulted only for symbols the direct inputs
/// don't already define -- same convention as a traditional linker's <c>-l</c> ordering) and a
/// <see cref="CvmPrimitiveTable"/> (built-in-instruction mnemonic -&gt; that mnemonic's own complete,
/// already-tagged final opcode word, since opcode binding is resolved at link time, not assemble time --
/// see that class's own remarks for why this linker stays completely tag-agnostic), resolves every
/// symbol, applies every relocation now that final addresses are known, and produces a single flat
/// <see cref="CvmImage"/>.
///
/// <b>Layout model</b>: sections are concatenated by NAME, "CODE" first (if any input defines it) then
/// every other name in first-encountered order, and within one name, by input/pulled-in-library-member
/// order -- i.e. exactly a traditional linker's own section-concatenation model, nothing fancier
/// (specifically: this linker never reorders or splits the words WITHIN one input's own section -- the
/// one exception is that, when program entry/exit layout applies, the object DEFINING <c>__exit</c> is
/// moved to the very front of that ordering first, since its own code must land at address 0:1 -- see
/// the entry/exit layout remarks below).
///
/// <b>Known, deliberate gap -- flagged rather than silently worked around</b>: <see cref="CvmObjectFile"/>'s
/// own <see cref="CvmRelocationType.AbsoluteAddress"/> relocation record does not distinguish a plain
/// data reference (a <c>.word</c>/<c>pushlit</c> operand, full 16-bit range) from a <c>call</c>
/// instruction's own target (which the CVM instruction encoding requires to fit in 15 bits, bit 15
/// clear, per <see cref="CvmInstructionSet.CallAddressMask"/>) -- <see cref="CvmAssembler"/> emits the
/// identical relocation shape for both. This linker therefore CANNOT catch "a `call` to an external
/// symbol whose final address doesn't fit in 15 bits" as a link error the way it can for other
/// constraints in this class -- such a call would silently produce a word with bit 15 set (indistinguishable
/// from a tagged/primitive-dispatch word) instead. Fixing this needs a new, narrower relocation type the
/// assembler would have to start emitting for `call` sites specifically; out of scope for this linker
/// alone.
/// </summary>
public static class CvmLinker
{
  /// <summary>
  /// Words per external-memory page, for <see cref="CvmLinkOptions.SectionPages"/> -- ADDED 2026-10-17.
  /// Must stay numerically equal to <c>Ga144.Evb.Ide.Services.CvmMemoryProtocol.Page0WordCount</c>
  /// (0x10000): that class's own <c>CombineAddress(page, addressInPage)</c> is what a running CVM
  /// program's <c>(page, address)</c> pointer ultimately gets turned into (<c>(page &lt;&lt; 16) |
  /// (addressInPage &amp; 0xFFFF)</c>) when it reaches the host's simulated external memory, so a
  /// section this linker places at "page N" must land at exactly <c>N * PageWordCount</c> in the final
  /// flat <see cref="CvmImage.Words"/> stream for that arithmetic to agree with where the words actually
  /// are. Duplicated here rather than referenced directly, because this project
  /// (<c>Ga144.Cvm.Toolchain</c>) is a lower layer that <c>Ga144.Evb.Ide</c> depends on, never the other
  /// way around -- the two constants must be kept in lockstep BY HAND if either ever changes (exactly
  /// the kind of cross-layer coupling this project's own doc comments elsewhere flag rather than hide).
  /// </summary>
  public const int PageWordCount = 0x10000;

  public sealed class Result
  {
    public required bool Success { get; init; }
    public required IReadOnlyList<string> Messages { get; init; }
    public CvmImage? Image { get; init; }
  }

  public static Result Link(
      IReadOnlyList<CvmLinkObjectInput> objects,
      IReadOnlyList<CvmLinkLibraryInput> libraries,
      CvmPrimitiveTable primitives,
      CvmLinkOptions options)
  {
    var messages = new List<string>();
    bool success = true;

    // (objectName, sectionName) -> the offset within that section a Global symbol lives at, PLUS
    // which object defined it -- built incrementally as library members get pulled in below, since
    // pulling one member in can itself introduce further, previously-unseen external references.
    var globalDefiningObject = new Dictionary<string, string>(StringComparer.Ordinal);
    var globalSymbol = new Dictionary<string, CvmSymbol>(StringComparer.Ordinal);
    var linkedObjects = new List<CvmLinkObjectInput>(objects);

    void RegisterObject(CvmLinkObjectInput input)
    {
      foreach (CvmSymbol symbol in input.ObjectFile.Symbols.Where(s => s.Binding == CvmSymbolBinding.Global))
      {
        if (globalDefiningObject.TryGetValue(symbol.Name, out string? existingObjectName))
        {
          messages.Add($"multiple definition of \"{symbol.Name}\": defined in both \"{existingObjectName}\" and \"{input.DisplayName}\".");
          success = false;
          continue;
        }

        globalDefiningObject[symbol.Name] = input.DisplayName;
        globalSymbol[symbol.Name] = symbol;
      }
    }

    foreach (CvmLinkObjectInput input in objects)
    {
      RegisterObject(input);
    }

    // Pull in library members to resolve external references, to a fixed point: a pulled-in member
    // can itself reference something only a LATER library provides. Each member is pulled in at most
    // once even if several remaining externals resolve to it.
    //
    // Entry/exit layout needs __start/__exit seeded in here explicitly, not just picked up as an
    // ordinary External reference: unlike every other external symbol, nothing in a normal program's
    // own object files ever references __start/__exit BY NAME -- they're the crt0-style entry/exit
    // code itself, not something main.c calls -- so without this, a checked library that DOES define
    // them (e.g. a "cvm" runtime library) would never actually get pulled into the link, and the
    // program entry/exit layout check further down would then -- correctly, but confusingly -- report
    // "requires a __start/__exit symbol, but no linked object defines one" even with the right library
    // referenced.
    var pulledMembers = new HashSet<(string LibraryName, string MemberName)>();
    bool changed = true;
    while (changed)
    {
      changed = false;
      IEnumerable<string> externallyReferenced = linkedObjects
          .SelectMany(input => input.ObjectFile.Symbols)
          .Where(symbol => symbol.Binding == CvmSymbolBinding.External)
          .Select(symbol => symbol.Name);

      IEnumerable<string> requiredForEntryLayout = options.ApplyEntryLayout
          ? [options.EntrySymbolName, options.ExitSymbolName]
          : [];

      List<string> unresolvedNow = [.. externallyReferenced.Concat(requiredForEntryLayout)
          .Distinct(StringComparer.Ordinal)
          .Where(name => !globalDefiningObject.ContainsKey(name) && !primitives.TryGetOpcode(name, out _) &&
              !string.Equals(name, options.HeapStartSymbolName, StringComparison.Ordinal))];

      foreach (string name in unresolvedNow)
      {
        foreach (CvmLinkLibraryInput library in libraries)
        {
          if (!library.Library.SymbolIndex.TryGetValue(name, out string? memberName))
          {
            continue;
          }

          if (!pulledMembers.Add((library.DisplayName, memberName)))
          {
            break; // already pulled in (by this or an earlier still-unresolved name); nothing new to do.
          }

          CvmLibraryMember member = library.Library.Members.First(m => m.Name == memberName);
          CvmObjectFile memberFile;
          try
          {
            memberFile = CvmObjectFile.Load(new MemoryStream(member.ObjectBytes));
          }
          // WIDENED, 2026-09-26: this used to only catch InvalidDataException/EndOfStreamException --
          // the two failure shapes a genuinely truncated or wrong-file-kind GAFF stream produces -- but
          // not the third, more insidious shape a STALE member produces: one saved before a breaking
          // .gaobj chunk-shape change (e.g. CvmRelocation.EmbeddedValue, added 2026-09-11 to the "RELO"
          // chunk -- see that field's own remarks) has one fewer field per RELO entry than the CURRENT
          // reader expects, so every read after the first misaligns; the reader doesn't fail cleanly, it
          // decodes GARBAGE section/symbol indices from what used to be the next entry's own bytes, which
          // then throws a bare, unhelpful ArgumentOutOfRangeException ("Index was out of range...") from
          // deep inside List<T>'s own indexer once one of those garbage indices happens to exceed this
          // member's own Sections/Symbols count -- caught for real, 2026-09-26, the first time this C
          // Debugger's own "auto-include every library, unconditionally" default pulled in a library that
          // hadn't been rebuilt since an old .gaobj format change. OverflowException is included for the
          // same reason: a garbage word/byte COUNT read as if it were still valid can also overflow a
          // buffer-size computation before ever reaching an indexer. This is still a real, actionable
          // problem (a stale build artifact), not something to paper over -- it stays a link failure
          // message, exactly like the two pre-existing cases, just with a message that actually points at
          // the fix instead of leaking a raw .NET exception with no context.
          catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException or OverflowException)
          {
            messages.Add(
                $"\"{library.DisplayName}\" member \"{memberName}\" could not be read as a valid CVM object file " +
                $"({exception.GetType().Name}: {exception.Message}). This usually means the library was built " +
                "with an older, incompatible version of this toolchain's .gaobj format -- rebuild that library " +
                "project (its own \"Build\" button) and try again.");
            success = false;
            break;
          }

          var pulledInput = new CvmLinkObjectInput($"{library.DisplayName}({memberName})", memberFile);
          linkedObjects.Add(pulledInput);
          RegisterObject(pulledInput);
          changed = true;
          break; // first library that defines it wins, same as a traditional linker's -l search order.
        }
      }
    }

    // Every External symbol that is STILL neither Global-defined by a linked object, a known primitive,
    // nor the synthesized heap-start marker's own name (CvmLinkOptions.HeapStartSymbolName -- not yet
    // placed at this point, but guaranteed to exist by the time relocations are applied, below) is a
    // genuine link error -- reported once per (referencing object, name) pair.
    foreach ((string objectName, string name) in linkedObjects
        .SelectMany(input => input.ObjectFile.Symbols
            .Where(symbol => symbol.Binding == CvmSymbolBinding.External)
            .Select(symbol => (input.DisplayName, symbol.Name)))
        .Distinct())
    {
      if (!globalDefiningObject.ContainsKey(name) && !primitives.TryGetOpcode(name, out _) &&
          !string.Equals(name, options.HeapStartSymbolName, StringComparison.Ordinal))
      {
        messages.Add($"undefined reference to \"{name}\" (referenced by \"{objectName}\").");
        success = false;
      }
    }

    if (!success)
    {
      return new Result { Success = false, Messages = messages };
    }

    // Program entry/exit layout requires __exit's own code to land at address 0:1 exactly -- i.e. to
    // be the very first thing in the final CODE section. That can't depend on which order the caller
    // happened to list its own objects in, or on the incidental order the library pull-in loop above
    // discovered things in (a program's own main.gaobj is ordinarily linked before any library it
    // references, which would otherwise bury __exit's code after the whole program instead of before
    // it). So, still WITHOUT reordering or splitting the words WITHIN any one object's own section
    // (same rule as everywhere else in this linker), pin whichever object defines __exit to the very
    // front of linkedObjects -- every other object (including whichever one defines __start, wherever
    // it ends up) keeps its existing relative order. __start does NOT need any special placement of its
    // own: per Stefan ("There is no need to place __start directly after __exit, it is sufficient that
    // it is close enough so the trampoline at 0:0 can reach __start" -- RELAXED, 2026-09-08, from the
    // original "__start immediately follows __exit" reading of the 8th round of ABI dictation), __start
    // only has to resolve to an address the entry vector's own single `br` word can reach, checked once
    // that word is synthesized further down -- nothing about __start's position relative to __exit is
    // checked here at all anymore.
    if (options.ApplyEntryLayout)
    {
      linkedObjects = ReorderForEntryLayout(linkedObjects, globalDefiningObject, options.ExitSymbolName);
    }

    // Layout: concatenate sections by name ("CODE" first if present, then every other name in
    // first-encountered order), and within one name, by linkedObjects order (which already reflects
    // pull-in order for library members, now adjusted above so __exit comes first when entry layout is
    // on). Word 0 is reserved for the synthesized entry vector when ApplyEntryLayout is set, so CODE's
    // own first section starts at address 1, not 0.
    List<string> sectionNamesInOrder = [];
    if (linkedObjects.Any(input => input.ObjectFile.Sections.Any(section => section.Name == "CODE")))
    {
      sectionNamesInOrder.Add("CODE");
    }

    foreach (CvmLinkObjectInput input in linkedObjects)
    {
      foreach (CvmSection section in input.ObjectFile.Sections)
      {
        if (!sectionNamesInOrder.Contains(section.Name))
        {
          sectionNamesInOrder.Add(section.Name);
        }
      }
    }

    var sectionBaseAddress = new Dictionary<(string ObjectName, string SectionName), int>();

    // ADDED 2026-10-17, per Stefan's own ask ("is there a possibility to know after the linker builds
    // the executable, where are the code, data and bss blocks and how long they are?"): one entry per
    // DISTINCT section name, not one per (object, section) pair like sectionBaseAddress above -- every
    // object contributing to the same section name is placed contiguously by PlaceSection below (that's
    // this linker's whole section-concatenation layout model -- see this class's own "Layout model"
    // remarks), so a single (base address, word count) pair per name already answers "where does CODE
    // start and how long is it" without needing a per-object breakdown. Carried through to the final
    // image as CvmImageSection (see that record's own remarks, including why there is no "bss" entry).
    var sectionLayout = new Dictionary<string, (int BaseAddress, int WordCount)>(StringComparer.Ordinal);

    var words = new List<int>();
    if (options.ApplyEntryLayout)
    {
      words.Add(0); // placeholder for the synthesized "br __start" vector, patched in once __start's final address is known.
    }

    int PageOf(string sectionName) => options.SectionPages.TryGetValue(sectionName, out int page) ? page : 0;

    void PlaceSection(string sectionName)
    {
      int sectionBase = words.Count;
      foreach (CvmLinkObjectInput input in linkedObjects)
      {
        CvmSection? section = input.ObjectFile.Sections.FirstOrDefault(s => s.Name == sectionName);
        if (section is null)
        {
          continue;
        }

        sectionBaseAddress[(input.DisplayName, sectionName)] = words.Count;
        words.AddRange(section.Words);
      }

      sectionLayout[sectionName] = (sectionBase, words.Count - sectionBase);
    }

    // Page 0 -- the pre-existing, unconditional behavior: every section with no CvmLinkOptions.
    // SectionPages entry (or an explicit entry of 0, same thing) is concatenated into the one shared
    // word stream CODE itself starts, in the exact order/grouping this linker has always used. A link
    // that never sets SectionPages (every link before 2026-10-17) runs this loop over every section
    // name there is and nothing else, so its own result is byte-for-byte unchanged.
    foreach (string sectionName in sectionNamesInOrder.Where(name => PageOf(name) == 0))
    {
      PlaceSection(sectionName);
    }

    // Paged sections -- ADDED 2026-10-17, for the CVM_pipeline C ABI v2's "globals on a dedicated
    // external-memory page" decision (see CvmLinkOptions.SectionPages's own remarks for the full
    // rationale and the cross-layer PageWordCount/CombineAddress coupling this relies on). Every section
    // explicitly assigned a page > 0 is placed at that page's own fixed base address (PageWordCount
    // words per page) instead of flowing into the page-0 stream above, visited in ascending page order
    // and, within one page, grouped/ordered the exact same "by section name then by linkedObjects
    // order" way page-0 sections always have been (PlaceSection above is the identical placement
    // routine either way -- only WHERE `words` stands when it runs differs).
    //
    // The shared `words` list is zero-padded out to each page's own base address first -- this creates a
    // real gap in the final flat image (e.g. all of page 1, the VM's own stack, which nothing here ever
    // statically initializes anyway) rather than a bug: this project's CURRENT external-memory model
    // (Ga144.Evb.Ide.Services.CvmSimulatedSram, a plain host-side in-memory array standing in for a real
    // SRAM chip that isn't wired up yet) loads this whole flat word list directly into its own backing
    // array with one bulk copy, so padding a gap costs nothing -- it does NOT trickle down into however
    // many slow one-word-at-a-time wire transactions a real external SRAM chip would eventually need;
    // revisit this cost assumption if/when this project moves to real SRAM hardware.
    foreach (int page in sectionNamesInOrder.Select(PageOf).Where(page => page > 0).Distinct().OrderBy(page => page))
    {
      int pageBaseAddress = page * PageWordCount;
      if (words.Count > pageBaseAddress)
      {
        messages.Add(
            $"page {page}'s own content (base address 0x{pageBaseAddress:X6}) overlaps the {words.Count} word(s) " +
            $"already placed below it -- the program's page-0 (and/or lower-paged) content is too large to fit " +
            $"before page {page} begins.");
        success = false;
        continue;
      }

      while (words.Count < pageBaseAddress)
      {
        words.Add(0);
      }

      foreach (string sectionName in sectionNamesInOrder.Where(name => PageOf(name) == page))
      {
        PlaceSection(sectionName);
      }
    }

    if (!success)
    {
      return new Result { Success = false, Messages = messages };
    }

    // Heap-start marker (ADDED 2026-10-17, see CvmLinkOptions.HeapStartSymbolName's own remarks for the
    // full design). sectionLayout already has HeapStartAfterSectionName's own (base address, word count)
    // from the placement loops above -- its end (markerAddress below) is exactly where this marker's OWN
    // 2 words get placed. Deliberately requires that end to be the current tail of the whole `words` list
    // (not merely "some address within an earlier page"): this linker never reorders or splits sections
    // (see this class's own "Layout model" remarks), so if anything else already landed after
    // HeapStartAfterSectionName -- today that can only happen if a second section is also mapped to that
    // same page, or to a page numerically between it and some other paged section -- there is no single
    // contiguous "end" to anchor the marker on, and growing `words` right here would silently overlap
    // that other content instead. Flagged as a link error rather than silently guessed at.
    //
    // The marker's own VALUE is deliberately markerAddress + 2, not markerAddress itself: markerAddress
    // is where the marker's own 2 words live, so a heap allocator that started there would immediately
    // overwrite the very value it just read. +2 is the first word that belongs to nothing this linker
    // placed at all -- genuinely free, no caveats about reclaiming the marker's own storage.
    int? heapStartAddress = null;
    if (options.HeapStartSymbolName is not null)
    {
      if (options.HeapStartAfterSectionName is null ||
          !sectionLayout.TryGetValue(options.HeapStartAfterSectionName, out (int BaseAddress, int WordCount) afterSection))
      {
        messages.Add(options.HeapStartAfterSectionName is null
            ? $"\"{options.HeapStartSymbolName}\" requires CvmLinkOptions.HeapStartAfterSectionName to be set."
            : $"\"{options.HeapStartSymbolName}\" could not be placed -- no linked object defines a " +
              $"\"{options.HeapStartAfterSectionName}\" section.");
        success = false;
      }
      else
      {
        int markerAddress = afterSection.BaseAddress + afterSection.WordCount;
        if (markerAddress != words.Count)
        {
          messages.Add(
              $"\"{options.HeapStartSymbolName}\" could not be placed immediately after \"{options.HeapStartAfterSectionName}\" " +
              $"(ending at word {markerAddress}) -- {words.Count - markerAddress} word(s) of some other section's " +
              "content already follow it there. This linker only supports placing the heap-start marker after the very " +
              "last section placed in the image.");
          success = false;
        }
        else
        {
          heapStartAddress = markerAddress;
          int heapStartValue = markerAddress + 2; // past the marker's own 2 words -- see remarks above.
          words.Add(heapStartValue & CvmWordCodec.WordMask);          // address-in-page -- low word, lower address.
          words.Add((heapStartValue >> 16) & CvmWordCodec.WordMask);  // page -- high word, higher address.
        }
      }
    }

    if (!success)
    {
      return new Result { Success = false, Messages = messages };
    }

    int FinalAddressOf(CvmSymbol symbol, string objectName) =>
        sectionBaseAddress[(objectName, symbol.SectionName!)] + symbol.Value;

    var finalAddress = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach ((string name, CvmSymbol symbol) in globalSymbol)
    {
      finalAddress[name] = FinalAddressOf(symbol, globalDefiningObject[name]);
    }

    foreach ((string name, int opcode) in primitives.Entries)
    {
      // A linked object's own Global definition (if it happens to reuse a primitive's name) wins over
      // the primitive table, same resolution order used above when deciding what still needed a
      // library pulled in for it. Note that for a primitive, what lands in finalAddress isn't really
      // an "address" at all -- it's the mnemonic's complete, already-tagged final opcode word (see
      // CvmPrimitiveTable's own remarks) -- finalAddress is reused as-is below purely because the
      // CvmOpcode relocation case just writes this value straight through.
      finalAddress.TryAdd(name, opcode);
    }

    // Heap-start marker, continued: register its synthesized address under its own name, same TryAdd
    // precedence rule as primitives above (an actual linked object's own same-named Global, however
    // unlikely, would still win -- this never overwrites one).
    if (heapStartAddress is not null)
    {
      finalAddress.TryAdd(options.HeapStartSymbolName!, heapStartAddress.Value);
    }

    // Program entry/exit layout (claude/cvm-abi.md section 1, 8th round, 2026-09-08; relaxed
    // 2026-09-08 -- see ReorderForEntryLayout's own remarks above): validated here, not engineered
    // around -- this linker never reorders or splits a section's own words to force __exit into place,
    // it only checks the ABI-mandated layout came out of the ORDINARY section-concatenation above and
    // reports a precise, actionable error if it didn't. __start carries no positional requirement of
    // its own beyond being reachable from the entry vector -- checked once that vector word is
    // synthesized, further down, not here.
    int entryVectorTarget = -1;
    if (options.ApplyEntryLayout)
    {
      if (!finalAddress.TryGetValue(options.ExitSymbolName, out int exitAddress))
      {
        messages.Add($"program entry/exit layout requires a \"{options.ExitSymbolName}\" symbol, but no linked object defines one.");
        success = false;
      }

      if (!finalAddress.TryGetValue(options.EntrySymbolName, out int entryAddress))
      {
        messages.Add($"program entry/exit layout requires a \"{options.EntrySymbolName}\" symbol, but no linked object defines one.");
        success = false;
      }

      if (success)
      {
        exitAddress = finalAddress[options.ExitSymbolName];
        entryAddress = finalAddress[options.EntrySymbolName];

        if (exitAddress != 1)
        {
          messages.Add(
              $"\"{options.ExitSymbolName}\" must resolve to address 0:1 (immediately after the entry vector at 0:0), " +
              $"but it resolved to 0:{exitAddress}. Link the object that defines \"{options.ExitSymbolName}\" first, " +
              $"and make sure nothing else in that object's own CODE section comes before it.");
          success = false;
        }

        if (success)
        {
          entryVectorTarget = entryAddress;
        }
      }
    }

    if (!success)
    {
      return new Result { Success = false, Messages = messages };
    }

    // Apply every relocation now that every symbol (object-defined, library-pulled, or primitive) has
    // a final address.
    //
    // FIXED, 2026-09-10: a relocation against a symbol THIS object defines for itself with Local
    // binding (a "static" file-scope global's own mangled label, e.g. "_a" -- never exported, so
    // RegisterObject above never adds it to globalSymbol/finalAddress at all) used to fall straight
    // through to "finalAddress[relocation.SymbolName]" below and throw a raw, unhandled
    // KeyNotFoundException the instant any program actually referenced one -- caught when Stefan's own
    // test program ("static int a = 0; ... d[0] = a;") hit it. A Local-bound symbol is, by construction,
    // only ever referenced from within its OWN object file (CvmAssembler itself rejects a name that is
    // both a local label and ".import"ed in the same file -- see its own "is both a local label and
    // .import'ed" check), so it must resolve against THAT object's own local Symbols table, never the
    // cross-object global one. Checked first, for every relocation, before falling back to the
    // (Global-or-primitive) finalAddress table for anything this object doesn't define for itself --
    // for a Global symbol this object happens to define and reference itself, both paths agree (the
    // object's own entry and finalAddress's cross-object entry resolve to the exact same address), so
    // this changes behavior only for the previously-crashing Local case.
    foreach (CvmLinkObjectInput input in linkedObjects)
    {
      foreach (CvmRelocation relocation in input.ObjectFile.Relocations)
      {
        int baseAddress = sectionBaseAddress[(input.DisplayName, relocation.SectionName)];
        int wordIndex = baseAddress + relocation.WordOffset;

        CvmSymbol? ownSymbol = input.ObjectFile.Symbols.FirstOrDefault(
            s => s.Name == relocation.SymbolName && s.Binding != CvmSymbolBinding.External);
        int resolved = ownSymbol is not null
            ? FinalAddressOf(ownSymbol, input.DisplayName)
            : finalAddress[relocation.SymbolName];

        words[wordIndex] = relocation.Type switch
        {
          // The full 16-bit range a plain data/address reference can use -- see this class's own
          // remarks for why a call-site's narrower 15-bit constraint can't be enforced here.
          CvmRelocationType.AbsoluteAddress => resolved & CvmWordCodec.WordMask,

          // CORRECTED (2026-09-08): this used to re-tag the resolved value as 0x8000 | (address &
          // CallAddressMask), on the assumption every primitive shares call's own single fixed tag
          // and 15-bit address range. That doesn't hold for CVM2 -- different primitive families tag
          // completely differently (node 507's local-execute family at 0x8800, node 506's frame ops at
          // 0x9xxx, node 508's gld/gst at 0xA000, node 509's unary-arithmetic family at 0xB000, node
          // 407's long-call family at 0xC000, node 406's binary-arithmetic family at 0xE000/0xE400,
          // node 408's comparison family at 0xF000/0xF400, and so on -- see CvmPrimitiveTable's own
          // remarks). So there is no single tag this linker could re-apply: CvmPrimitiveTable now
          // supplies each primitive's own COMPLETE, already-tagged final word directly (see how
          // `resolved` was populated from primitives.Entries above), and this linker stays entirely
          // tag-agnostic -- it just writes that word through, masked to 16 bits.
          //
          // ADDED 2026-09-11: OR in relocation.EmbeddedValue (see that field's own remarks) before
          // masking -- 0 for every relocation except the address-register family's ops
          // (arinc/ardec/arinc2/ardec2/arld/arst/lda/sta -- node 308 as of 2026-09-15's renumbering,
          // previously node 306; see Node308Program's own remarks), where it carries the register-index
          // operand CvmAssembler
          // already validated and positioned at assemble time. `resolved` here is only the live-resolved
          // BASE word (tag | function-select field); this is the one place that base and the
          // already-known register bits are finally combined into the real opcode word.
          CvmRelocationType.CvmOpcode => (resolved | relocation.EmbeddedValue) & CvmWordCodec.WordMask,

          _ => throw new NotSupportedException($"Unknown relocation type {relocation.Type}."),
        };
      }
    }

    if (options.ApplyEntryLayout)
    {
      // Synthesize the "br __start" vector word directly (CvmAssembler itself refuses to assemble a
      // br to a not-yet-resolved import -- see EmitEmbeddedSignedValue's own remarks -- since this
      // word's target isn't known until link time; the linker knows it now, so it can compute the
      // exact same shape.Tag | (value & valueBitMask) encoding EmitEmbeddedSignedValue uses, keyed
      // directly off CvmInstructionSet's own br constants rather than duplicating them).
      int offsetFromVector = entryVectorTarget - (0 + 1);
      int maxValue = CvmInstructionSet.BranchOffsetBitMask >> 1;
      int minValue = -(maxValue + 1);
      if (offsetFromVector < minValue || offsetFromVector > maxValue)
      {
        messages.Add(
            $"\"{options.EntrySymbolName}\" is too far from the entry vector at 0:0 to reach with a single \"br\" " +
            $"({offsetFromVector} words; \"br\"'s signed range is {minValue}..{maxValue}).");
        return new Result { Success = false, Messages = messages };
      }

      words[0] = CvmInstructionSet.BranchTag | (offsetFromVector & CvmInstructionSet.BranchOffsetBitMask);
    }

    // Added 2026-09-26, alongside CvmImageSymbol.DefiningObjectName (see its own remarks): every
    // finalAddress entry (a Global symbol this link resolved, or a primitive-table mnemonic) is still
    // included exactly as before -- this only ADDS entries, never removes or renames one, so every
    // existing caller that only ever looked up a Global/primitive name is unaffected. What's new is the
    // second loop below: every LOCAL (non-exported) symbol each linked object defines for itself is now
    // ALSO included -- previously dropped entirely (see the "FIXED, 2026-09-10" remarks on the
    // relocation loop above for the bug that same omission caused there; this is the equivalent gap in
    // the image's own symbol table). A Local symbol is only ever meaningful within its own object file
    // (CvmAssembler itself refuses to let a name be both a local label and ".import"ed), so its final
    // address is looked up the same way the relocation loop above already does for one: via THIS
    // object's own FinalAddressOf, never the cross-object finalAddress table. This is what lets, e.g.,
    // the C Debugger recover the final address of a per-statement source-comment label the C compiler
    // emits as a Local symbol (see CCodeGenerator's own remarks) -- filtered by DefiningObjectName, since
    // several linked objects may each define a same-named Local symbol (that label-naming scheme is
    // deliberately only unique WITHIN one object, never across a whole link).
    var imageSymbolList = new List<CvmImageSymbol>();
    foreach ((string name, int address) in finalAddress)
    {
      imageSymbolList.Add(new CvmImageSymbol
      {
        Name = name,
        Address = address,
        DefiningObjectName = globalDefiningObject.TryGetValue(name, out string? definingObject) ? definingObject : null,
        // IsExported (added 2026-09-26, see CvmImageSymbol's own remarks): every entry here came from
        // finalAddress, i.e. is a Global symbol or a primitive-table mnemonic -- exactly the set a human
        // reading the CVM Debugger's own "<name>" memory-view annotation actually wants to see.
        IsExported = true
      });
    }

    foreach (CvmLinkObjectInput input in linkedObjects)
    {
      foreach (CvmSymbol symbol in input.ObjectFile.Symbols.Where(s => s.Binding == CvmSymbolBinding.Local))
      {
        imageSymbolList.Add(new CvmImageSymbol
        {
          Name = symbol.Name,
          Address = FinalAddressOf(symbol, input.DisplayName),
          DefiningObjectName = input.DisplayName,
          // Never exported (that's the whole point of Local binding) -- see CvmImageSymbol.IsExported's
          // own remarks for why this is what keeps these out of the ordinary "<name>" annotation.
          IsExported = false
        });
      }
    }

    List<CvmImageSymbol> imageSymbols = [.. imageSymbolList
        .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
        .ThenBy(symbol => symbol.Address)];

    // ADDED 2026-10-17, alongside CvmImageSection (see its own remarks): sectionLayout above already
    // has every distinct section name's own (base address, word count) -- just reshape it into the
    // image's own ordered Sections list, in the same "CODE first, then first-encountered order" this
    // linker has always laid sections out in.
    List<CvmImageSection> imageSections = [.. sectionNamesInOrder
        .Select(name => new CvmImageSection
        {
          Name = name,
          Page = PageOf(name),
          BaseAddress = sectionLayout[name].BaseAddress,
          WordCount = sectionLayout[name].WordCount,
        })];

    var image = new CvmImage
    {
      Words = words,
      EntryAddress = options.ApplyEntryLayout ? 0 : (finalAddress.TryGetValue(options.EntrySymbolName, out int addr) ? addr : 0),
      Symbols = imageSymbols,
      Sections = imageSections,
    };

    // ADDED 2026-10-17, alongside the "paged sections" feature above: `words.Count` alone is the total
    // SPAN of the flat image (page 0's own content plus every zero-padded gap up through the highest
    // page a section was assigned to), not how much of it is actual content -- once a program's globals
    // live on page 2 (CvmLinkOptions.SectionPages), that span balloons to 2 * PageWordCount + a handful
    // of real words, which read as alarming/buggy in the Build dialog even though the .gaimg itself is
    // perfectly correct (see the "paged sections" loop's own remarks on why the padding is intentional
    // and cheap for CvmSimulatedSram's bulk-load model). So the summary now separates "how many words
    // actually came from a linked object's own section" from "how big the final flat image is" -- the
    // two only differ once paging is in play, so a plain (unpaged, page-0-only) link's own message is
    // untouched, byte-for-byte, matching this file's own "zero SectionPages entries -> unchanged result"
    // convention used by the paged-sections loop itself.
    int occupiedWordCount = (options.ApplyEntryLayout ? 1 : 0) + (heapStartAddress is not null ? 2 : 0) +
        linkedObjects.Sum(input => input.ObjectFile.Sections.Sum(section => section.Words.Count));
    int maxPageUsed = sectionNamesInOrder.Select(PageOf).DefaultIfEmpty(0).Max();
    string linkedObjectNames = string.Join(", ", linkedObjects.Select(o => o.DisplayName));
    messages.Add(maxPageUsed > 0
        ? $"linked {linkedObjects.Count} object(s) ({linkedObjectNames}) into {occupiedWordCount} word(s) of actual content, " +
          $"spanning page 0 through page {maxPageUsed} ({words.Count} word(s) total, including the zero-padded gap(s) between pages)."
        : $"linked {linkedObjects.Count} object(s) ({linkedObjectNames}) into {words.Count} word(s).");
    return new Result { Success = true, Messages = messages, Image = image };
  }

  /// <summary>
  /// Returns <paramref name="linkedObjects"/> reordered so the object defining <paramref
  /// name="exitSymbolName"/> ("__exit") comes first -- since its own code must land at address 0:1,
  /// the very first word of the final CODE section, and that can't depend on where the caller's own
  /// objects or its checked libraries happened to place it in the input order. Every other object
  /// (including whichever one defines "__start", wherever that turns out to be -- see this class's own
  /// remarks on why __start's position is no longer separately pinned or validated) keeps its existing
  /// relative order. __exit not resolving at all (a link error to be reported by the caller regardless)
  /// leaves the list untouched.
  /// </summary>
  private static List<CvmLinkObjectInput> ReorderForEntryLayout(
      List<CvmLinkObjectInput> linkedObjects,
      IReadOnlyDictionary<string, string> globalDefiningObject,
      string exitSymbolName)
  {
    if (!globalDefiningObject.TryGetValue(exitSymbolName, out string? exitObjectName))
    {
      return linkedObjects;
    }

    CvmLinkObjectInput exitInput = linkedObjects.First(input => input.DisplayName == exitObjectName);
    List<CvmLinkObjectInput> reordered = [exitInput];
    reordered.AddRange(linkedObjects.Where(input => input.DisplayName != exitObjectName));
    return reordered;
  }
}