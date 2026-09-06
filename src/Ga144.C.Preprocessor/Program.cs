using Ga144.C.Preprocessor;
using Ga144.C.Toolchain;

// gcpp -- the C preprocessor command-line front end. All the real work (macro expansion, conditional
// compilation, #include resolution) lives in Ga144.C.Toolchain.CPreprocessor; this file is just
// argument handling and I/O, the same split gaasm/Ga144.Cvm.Toolchain already uses, so the IDE can
// later drive the same engine directly instead of shelling out to this executable.
if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
{
  PrintUsage();
  return args.Length == 0 ? 1 : 0;
}

string? inputPath = null;
string? outputPath = null;
var includeDirectories = new List<string>();
var defines = new List<(string Name, string Value)>();
bool dumpTokens = false;

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

    case "--tokens":
      dumpTokens = true;
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

outputPath ??= Path.ChangeExtension(inputPath, ".i");

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
var preprocessor = new CPreprocessor(resolver);
foreach ((string name, string value) in defines)
{
  preprocessor.DefineFromCommandLine(name, value);
}

CPreprocessResult result = preprocessor.Preprocess(Path.GetFullPath(inputPath), source);

foreach (string diagnostic in result.Diagnostics)
{
  Console.Error.WriteLine(diagnostic);
}

if (!result.Success)
{
  return 1;
}

try
{
  string outputText = dumpTokens ? DumpTokens(result.Tokens) : CTokenWriter.Write(result.Tokens);
  File.WriteAllText(outputPath, outputText);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
  Console.Error.WriteLine($"error: could not write \"{outputPath}\": {exception.Message}");
  return 1;
}

Console.WriteLine($"{inputPath} -> {outputPath} ({result.IncludedFiles.Count} file(s) included)");
return 0;

static (string Name, string Value) ParseDefine(string text)
{
  int equals = text.IndexOf('=');
  return equals < 0 ? (text, string.Empty) : (text[..equals], text[(equals + 1)..]);
}

static string DumpTokens(IReadOnlyList<CToken> tokens)
{
  var builder = new System.Text.StringBuilder();
  foreach (CToken token in tokens)
  {
    if (token.Kind == CTokenKind.EndOfFile)
    {
      break;
    }

    if (token.Kind == CTokenKind.NewLine)
    {
      continue;
    }

    builder.AppendLine($"{token.Location} {token.Kind,-13} {token.Text}");
  }

  return builder.ToString();
}

static void PrintUsage()
{
  Console.WriteLine("""
      gcpp -- C preprocessor

      Usage: gcpp <input.c> [-o <output.i>] [-I <dir>]... [-D <name>[=<value>]]... [--tokens]

      Preprocesses a single C source file: macro expansion (object-like and function-like, including
      '#'/'##'), "#if"/"#ifdef"/"#ifndef"/"#elif"/"#else"/"#endif", "#include" (searched relative to
      the including file first, then each "-I" directory, in order given), "#pragma once", "#error",
      and the predefined macros __CVM__, __DATE__, __TIME__, __LINE__, and __FILE__.

      With --tokens, writes one lexed token per line ("file(line,col) Kind Text") instead of
      reassembled source text -- useful for inspecting exactly what a later compiler phase would see.

      This is a standalone diagnostic tool for the preprocessor itself; the IDE's "Build" step does
      not yet call it.
      """);
}
