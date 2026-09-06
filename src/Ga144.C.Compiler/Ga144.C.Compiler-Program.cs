using Ga144.C.Toolchain;

// gacc -- the C compiler command-line front end. All the real work (preprocessing, parsing, code
// generation) lives in Ga144.C.Toolchain.CCompiler; this file is just argument handling and I/O, the
// same split gaasm/gcpp already use, so the IDE can later drive the same engine directly instead of
// shelling out to this executable. gacc's own job stops at CVM assembly TEXT -- turning that into an
// object file is gaasm's job, kept as a separate step exactly like every other stage of this toolchain
// (preprocess, compile, assemble, archive, link are five small tools, not one large one).
if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
  PrintUsage();
  return args.Length == 0 ? 1 : 0;
}

string? inputPath = null;
string? outputPath = null;
string? preprocessedOutputPath = null;
var includeDirectories = new List<string>();
var defines = new List<(string Name, string Value)>();

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

    case "--pre":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("error: --pre requires a file path.");
        return 1;
      }

      preprocessedOutputPath = args[++index];
      break;

    case "-I":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("error: -I requires a directory path.");
        return 1;
      }

      includeDirectories.Add(args[++index]);
      break;

    case "-D":
      if (index + 1 >= args.Length)
      {
        Console.Error.WriteLine("error: -D requires a NAME or NAME=VALUE.");
        return 1;
      }

      defines.Add(ParseDefine(args[++index]));
      break;

    default:
      if (args[index].StartsWith("-I", StringComparison.Ordinal) && args[index].Length > 2)
      {
        includeDirectories.Add(args[index][2..]);
      }
      else if (args[index].StartsWith("-D", StringComparison.Ordinal) && args[index].Length > 2)
      {
        defines.Add(ParseDefine(args[index][2..]));
      }
      else if (inputPath is not null)
      {
        Console.Error.WriteLine($"error: unexpected extra argument \"{args[index]}\" (already have an input file \"{inputPath}\").");
        return 1;
      }
      else
      {
        inputPath = args[index];
      }

      break;
  }
}

if (inputPath is null)
{
  Console.Error.WriteLine("error: no input file given.");
  PrintUsage();
  return 1;
}

outputPath ??= Path.ChangeExtension(inputPath, ".casm");

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

// The including file's own directory always takes part in a quoted #include's search implicitly
// (FileSystemIncludeResolver checks it first); "-I" only adds further directories to search after that.
var resolver = new FileSystemIncludeResolver(includeDirectories);
CCompileResult result = CCompiler.Compile(Path.GetFullPath(inputPath), source, resolver, defines);

foreach (string diagnostic in result.Diagnostics)
{
  Console.Error.WriteLine(diagnostic);
}

if (preprocessedOutputPath is not null && result.PreprocessedText is not null)
{
  try
  {
    File.WriteAllText(preprocessedOutputPath, result.PreprocessedText);
  }
  catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
  {
    Console.Error.WriteLine($"error: could not write \"{preprocessedOutputPath}\": {exception.Message}");
    return 1;
  }
}

if (!result.Success || result.Assembly is null)
{
  return 1;
}

try
{
  File.WriteAllText(outputPath, result.Assembly);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
  Console.Error.WriteLine($"error: could not write \"{outputPath}\": {exception.Message}");
  return 1;
}

Console.WriteLine($"{inputPath} -> {outputPath}");
return 0;

static (string Name, string Value) ParseDefine(string text)
{
  int equals = text.IndexOf('=');
  return equals < 0 ? (text, string.Empty) : (text[..equals], text[(equals + 1)..]);
}

static void PrintUsage()
{
  Console.WriteLine("""
      gacc -- GA144 C compiler

      Usage: gacc <input.c> [-o <output.casm>] [--pre <output.i>] [-I <dir>]... [-D <name>[=<value>]]...

      Compiles a single C source file to CVM assembly text: preprocesses it (exactly like gcpp --
      macro expansion, conditional compilation, "#include"), parses it, and generates code. Turning
      the resulting ".casm" into an object file is a separate step -- run gaasm on gacc's own output,
      exactly like every other stage of this toolchain (preprocess, compile, assemble, archive, link
      are five small tools, not one large one).

      With --pre, also writes the reassembled preprocessed source (what gcpp's own output would be)
      to the given path -- useful for inspecting exactly what the compiler's parser saw, and matching
      a C project's own "pre" directory convention.

      Supported C subset: void/int/unsigned int/char/unsigned char and pointers/1D arrays of them,
      globals and locals (including "static" locals), all control flow (if/while/do/for/switch/
      break/continue/goto/return), full operator precedence, casts, sizeof, string literals, and the
      comma operator. NOT supported: struct/union/enum/typedef, float/double/long/short,
      multi-dimensional arrays, function pointers, and variadic functions -- each of these is a clear
      compiler error, never silently miscompiled. See claude/c-compiler-design.md for the full scope,
      the CVM C ABI this compiler targets, and the runtime library routines ("__mul", "__divs",
      "__divu", "__mods", "__modu") a project's "cvm" library must supply for "*"/"/"/"%" to link.

      This is a standalone diagnostic tool for the compiler itself; the IDE's "Build" step does not
      yet call it.
      """);
}
