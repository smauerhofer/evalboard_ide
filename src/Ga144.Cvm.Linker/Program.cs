using Ga144.Cvm.Toolchain;

// galink -- the CVM linker command-line front end. All the real work (symbol resolution, section
// layout, relocation application, the program entry/exit vector) lives in
// Ga144.Cvm.Toolchain.CvmLinker; this file is just argument handling and I/O, same split as
// gaasm/galib.
//
//   galink <input1.gaobj> [input2.gaobj ...] [-l <archive.galib> ...] --primitives <table.gaprim>
//          [--primitives <table2.gaprim> ...] -o <output.gaimg> [--no-entry-layout]
//
// See Ga144.Cvm.Toolchain.CvmLinker's own remarks for the layout model (section concatenation, CODE
// first) and the program entry/exit convention (--no-entry-layout turns it off); CvmPrimitiveTable's
// own remarks for the ".gaprim" file format and where its data is meant to come from (an IDE export
// mechanism that doesn't exist yet -- a hand-written table is the only way to exercise this today).
if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
  PrintUsage();
  return args.Length == 0 ? 1 : 0;
}

var objectPaths = new List<string>();
var libraryPaths = new List<string>();
var primitivePaths = new List<string>();
string? outputPath = null;
bool applyEntryLayout = true;

for (int index = 0; index < args.Length; index++)
{
  switch (args[index])
  {
    case "-o":
    case "--output":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("galink: -o/--output requires a file path.");
        return 1;
      }

      outputPath = args[++index];
      break;

    case "-l":
    case "--library":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("galink: -l/--library requires a file path.");
        return 1;
      }

      libraryPaths.Add(args[++index]);
      break;

    case "--primitives":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("galink: --primitives requires a file path.");
        return 1;
      }

      primitivePaths.Add(args[++index]);
      break;

    case "--no-entry-layout":
      applyEntryLayout = false;
      break;

    default:
      objectPaths.Add(args[index]);
      break;
  }
}

if (objectPaths.Count == 0)
{
  Console.Error.WriteLine("galink: no input .gaobj file given.");
  PrintUsage();
  return 1;
}

outputPath ??= Path.ChangeExtension(objectPaths[0], ".gaimg");

var objectInputs = new List<CvmLinkObjectInput>();
foreach (string path in objectPaths)
{
  CvmObjectFile objectFile;
  try
  {
    using FileStream stream = File.OpenRead(path);
    objectFile = CvmObjectFile.Load(stream);
  }
  catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
  {
    Console.Error.WriteLine($"galink: \"{path}\" is not a valid CVM object file: {exception.Message}");
    return 1;
  }

  objectInputs.Add(new CvmLinkObjectInput(Path.GetFileName(path), objectFile));
}

var libraryInputs = new List<CvmLinkLibraryInput>();
foreach (string path in libraryPaths)
{
  CvmLibrary library;
  try
  {
    using FileStream stream = File.OpenRead(path);
    library = CvmLibrary.Load(stream);
  }
  catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
  {
    Console.Error.WriteLine($"galink: \"{path}\" is not a valid CVM library: {exception.Message}");
    return 1;
  }

  libraryInputs.Add(new CvmLinkLibraryInput(Path.GetFileName(path), library));
}

CvmPrimitiveTable primitives = CvmPrimitiveTable.Empty;
foreach (string path in primitivePaths)
{
  string text;
  try
  {
    text = File.ReadAllText(path);
  }
  catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
  {
    Console.Error.WriteLine($"galink: could not read \"{path}\": {exception.Message}");
    return 1;
  }

  (CvmPrimitiveTable? table, IReadOnlyList<string> errors) = CvmPrimitiveTable.Parse(text);
  if (table is null)
  {
    foreach (string error in errors)
    {
      Console.Error.WriteLine($"{path}: error: {error}");
    }

    return 1;
  }

  primitives = primitives.MergedWith(table);
}

CvmLinker.Result result = CvmLinker.Link(
    objectInputs,
    libraryInputs,
    primitives,
    new CvmLinkOptions { ApplyEntryLayout = applyEntryLayout });

foreach (string message in result.Messages)
{
  (result.Success ? Console.Out : Console.Error).WriteLine($"galink: {message}");
}

if (!result.Success || result.Image is null)
{
  return 1;
}

try
{
  using FileStream stream = File.Create(outputPath);
  result.Image.Save(stream);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
  Console.Error.WriteLine($"galink: could not write \"{outputPath}\": {exception.Message}");
  return 1;
}

Console.WriteLine($"-> {outputPath} ({result.Image.Words.Count} word(s), entry 0:{result.Image.EntryAddress}, {result.Image.Symbols.Count} symbol(s))");
return 0;

static void PrintUsage()
{
  Console.WriteLine("""
      galink -- CVM linker

      Usage:
        galink <input1.gaobj> [input2.gaobj ...] [-l <archive.galib> ...]
               --primitives <table.gaprim> [--primitives <table2.gaprim> ...]
               -o <output.gaimg> [--no-entry-layout]

      Resolves every symbol across the given object files (pulling in only the library members a
      program actually needs), applies every relocation, and writes a single flat .gaimg. By default
      the program entry/exit convention applies: word 0:0 becomes a synthesized "br __start", "__exit"
      must resolve to address 0:1, and "__start" must immediately follow it -- pass --no-entry-layout
      to link without any of that (CODE then simply starts at 0:0).

      --primitives points at a ".gaprim" file (mnemonic = address, one per line) giving the real node
      address every built-in CVM instruction (nop/pushlit/push/pop/...) dispatches to -- opcode binding
      is resolved at link time, not assemble time. There is no automated way to produce a real one yet
      (see Ga144.Cvm.Toolchain.CvmPrimitiveTable's own remarks); write one by hand to exercise this tool.
      """);
}