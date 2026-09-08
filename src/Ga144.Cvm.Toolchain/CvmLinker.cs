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
          .Where(name => !globalDefiningObject.ContainsKey(name) && !primitives.TryGetOpcode(name, out _))];

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
          catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
          {
            messages.Add($"\"{library.DisplayName}\" member \"{memberName}\" is not a valid CVM object file: {exception.Message}");
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

    // Every External symbol that is STILL neither Global-defined by a linked object nor a known
    // primitive is a genuine link error -- reported once per (referencing object, name) pair.
    foreach ((string objectName, string name) in linkedObjects
        .SelectMany(input => input.ObjectFile.Symbols
            .Where(symbol => symbol.Binding == CvmSymbolBinding.External)
            .Select(symbol => (input.DisplayName, symbol.Name)))
        .Distinct())
    {
      if (!globalDefiningObject.ContainsKey(name) && !primitives.TryGetOpcode(name, out _))
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
    var words = new List<int>();
    if (options.ApplyEntryLayout)
    {
      words.Add(0); // placeholder for the synthesized "br __start" vector, patched in once __start's final address is known.
    }

    foreach (string sectionName in sectionNamesInOrder)
    {
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
    foreach (CvmLinkObjectInput input in linkedObjects)
    {
      foreach (CvmRelocation relocation in input.ObjectFile.Relocations)
      {
        int baseAddress = sectionBaseAddress[(input.DisplayName, relocation.SectionName)];
        int wordIndex = baseAddress + relocation.WordOffset;
        int resolved = finalAddress[relocation.SymbolName];

        words[wordIndex] = relocation.Type switch
        {
          // The full 16-bit range a plain data/address reference can use -- see this class's own
          // remarks for why a call-site's narrower 15-bit constraint can't be enforced here.
          CvmRelocationType.AbsoluteAddress => resolved & CvmWordCodec.WordMask,

          // CORRECTED (2026-09-08): this used to re-tag the resolved value as 0x8000 | (address &
          // CallAddressMask), on the assumption every primitive shares call's own single fixed tag
          // and 15-bit address range. That doesn't hold for CVM2 -- different primitive families tag
          // completely differently (node 507's local-execute family at 0x8800, node 506's frame ops at
          // 0x9xxx, node 508's ldg/stg at 0xA000, node 509's unary-arithmetic family at 0xB000, node
          // 407's long-call family at 0xC000, node 406's binary-arithmetic family at 0xE000/0xE400,
          // node 408's comparison family at 0xF000/0xF400, and so on -- see CvmPrimitiveTable's own
          // remarks). So there is no single tag this linker could re-apply: CvmPrimitiveTable now
          // supplies each primitive's own COMPLETE, already-tagged final word directly (see how
          // `resolved` was populated from primitives.Entries above), and this linker stays entirely
          // tag-agnostic -- it just writes that word through, masked to 16 bits.
          CvmRelocationType.CvmOpcode => resolved & CvmWordCodec.WordMask,

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

    List<CvmImageSymbol> imageSymbols = [.. finalAddress
        .Select(pair => new CvmImageSymbol { Name = pair.Key, Address = pair.Value })
        .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)];

    var image = new CvmImage
    {
      Words = words,
      EntryAddress = options.ApplyEntryLayout ? 0 : (finalAddress.TryGetValue(options.EntrySymbolName, out int addr) ? addr : 0),
      Symbols = imageSymbols,
    };

    messages.Add($"linked {linkedObjects.Count} object(s) ({string.Join(", ", linkedObjects.Select(o => o.DisplayName))}) into {words.Count} word(s).");
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