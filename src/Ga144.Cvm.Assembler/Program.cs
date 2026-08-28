using Ga144.Cvm.Toolchain;

// gaasm -- the CVM assembler command-line front end. All the real work (parsing, layout, relocation
// emission) lives in Ga144.Cvm.Toolchain.CvmAssembler; this file is just argument handling and I/O so
// the engine itself stays usable as a library too (the IDE project references the same one).
if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
  PrintUsage();
  return args.Length == 0 ? 1 : 0;
}

string? inputPath = null;
string? outputPath = null;
for (int index = 0; index < args.Length; index++)
{
  switch (args[index])
  {
    case "-o":
    case "--output":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("error: -o/--output requires a file path.");
        return 1;
      }

      outputPath = args[++index];
      break;

    default:
      if (inputPath is not null)
      {
        Console.Error.WriteLine($"error: unexpected extra argument \"{args[index]}\" (already have an input file \"{inputPath}\").");
        return 1;
      }

      inputPath = args[index];
      break;
  }
}

if (inputPath is null)
{
  Console.Error.WriteLine("error: no input file given.");
  PrintUsage();
  return 1;
}

outputPath ??= Path.ChangeExtension(inputPath, ".gaobj");

string source;
try
{
  source = File.ReadAllText(inputPath);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
  Console.Error.WriteLine($"error: could not read \"{inputPath}\": {exception.Message}");
  return 1;
}

(CvmObjectFile? objectFile, IReadOnlyList<string> errors) = CvmAssembler.Assemble(source);
if (objectFile is null)
{
  foreach (string error in errors)
  {
    Console.Error.WriteLine($"{inputPath}: error: {error}");
  }

  return 1;
}

try
{
  using FileStream stream = File.Create(outputPath);
  objectFile.Save(stream);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
  Console.Error.WriteLine($"error: could not write \"{outputPath}\": {exception.Message}");
  return 1;
}

int exportedCount = objectFile.Symbols.Count(symbol => symbol.Binding == CvmSymbolBinding.Global);
int externalCount = objectFile.Symbols.Count(symbol => symbol.Binding == CvmSymbolBinding.External);
Console.WriteLine($"{inputPath} -> {outputPath} " +
    $"({objectFile.Sections.Sum(section => section.Words.Count)} words, " +
    $"{exportedCount} exported, {externalCount} external, {objectFile.Relocations.Count} relocations)");
return 0;

static void PrintUsage()
{
  Console.WriteLine("""
      gaasm -- CVM assembler

      Usage: gaasm <input.casm> [-o <output.gaobj>]

      Assembles CVM assembly language source (nop / pushlit <data> / push / pop, labels, and
      .section / .export / .import / .word directives) into a relocatable CVM object file. Every
      built-in instruction and every ".import"ed name is left as an external symbol for a future
      linker to resolve -- this tool never bakes in a numeric opcode.
      """);
}
