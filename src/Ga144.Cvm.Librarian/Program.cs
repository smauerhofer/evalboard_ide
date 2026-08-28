using Ga144.Cvm.Toolchain;

// galib -- the CVM librarian/archiver command-line front end. All the real work (the .galib format,
// the Global-symbol index, member validation) lives in Ga144.Cvm.Toolchain.CvmLibrary; this file is
// just argument handling and I/O, same split as gaasm.
if (args.Length == 0 || args[0] is "-h" or "--help")
{
  PrintUsage();
  return args.Length == 0 ? 1 : 0;
}

string command = args[0];
string[] rest = args[1..];
try
{
  return command switch
  {
    "create" => Create(rest),
    "add" => Add(rest),
    "list" => List(rest),
    "extract" => Extract(rest),
    _ => Unknown(command),
  };
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
{
  Console.Error.WriteLine($"galib: {exception.Message}");
  return 1;
}

static int Create(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("galib create: usage: galib create <archive.galib> <member1.gaobj> [member2.gaobj ...]");
    return 1;
  }

  string archivePath = args[0];
  var library = new CvmLibrary();
  foreach (string memberPath in args[1..])
  {
    library.Members.Add(ReadMember(memberPath));
  }

  return SaveAndReport(library, archivePath, verb: "created");
}

static int Add(string[] args)
{
  if (args.Length < 2)
  {
    Console.Error.WriteLine("galib add: usage: galib add <archive.galib> <member1.gaobj> [member2.gaobj ...]");
    return 1;
  }

  string archivePath = args[0];
  if (!File.Exists(archivePath))
  {
    Console.Error.WriteLine($"galib add: \"{archivePath}\" does not exist -- use \"galib create\" to make a new archive.");
    return 1;
  }

  CvmLibrary library;
  try
  {
    using FileStream stream = File.OpenRead(archivePath);
    library = CvmLibrary.Load(stream);
  }
  catch (InvalidDataException exception)
  {
    Console.Error.WriteLine($"galib add: \"{archivePath}\" is not a valid CVM library: {exception.Message}");
    return 1;
  }

  foreach (string memberPath in args[1..])
  {
    library.Members.Add(ReadMember(memberPath));
  }

  return SaveAndReport(library, archivePath, verb: "updated");
}

static int List(string[] args)
{
  if (args.Length != 1)
  {
    Console.Error.WriteLine("galib list: usage: galib list <archive.galib>");
    return 1;
  }

  CvmLibrary? library = LoadArchiveOrExit(args[0], out int failureCode);
  if (library is null)
  {
    return failureCode;
  }

  Console.WriteLine($"{args[0]}: {library.Members.Count} member(s), {library.SymbolIndex.Count} exported symbol(s)");
  foreach (CvmLibraryMember member in library.Members)
  {
    List<string> exported = [];
    try
    {
      CvmObjectFile objectFile = CvmObjectFile.Load(new MemoryStream(member.ObjectBytes));
      exported = [.. objectFile.Symbols.Where(s => s.Binding == CvmSymbolBinding.Global).Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal)];
    }
    catch (InvalidDataException)
    {
      exported = ["<not a valid object file>"];
    }

    Console.WriteLine($"  {member.Name}  ({member.ObjectBytes.Length} bytes)  exports: {(exported.Count == 0 ? "(none)" : string.Join(", ", exported))}");
  }

  return 0;
}

static int Extract(string[] args)
{
  if (args.Length is not (2 or 4))
  {
    Console.Error.WriteLine("galib extract: usage: galib extract <archive.galib> <memberName> [-o <output.gaobj>]");
    return 1;
  }

  CvmLibrary? library = LoadArchiveOrExit(args[0], out int failureCode);
  if (library is null)
  {
    return failureCode;
  }

  string memberName = args[1];
  CvmLibraryMember? member = library.Members.FirstOrDefault(m => m.Name == memberName);
  if (member is null)
  {
    string available = library.Members.Count == 0 ? "(the archive has no members)" : string.Join(", ", library.Members.Select(m => m.Name));
    Console.Error.WriteLine($"galib extract: \"{memberName}\" is not a member of \"{args[0]}\". Available members: {available}");
    return 1;
  }

  string outputPath = memberName;
  if (args.Length == 4)
  {
    if (args[2] != "-o")
    {
      Console.Error.WriteLine("galib extract: usage: galib extract <archive.galib> <memberName> [-o <output.gaobj>]");
      return 1;
    }

    outputPath = args[3];
  }

  File.WriteAllBytes(outputPath, member.ObjectBytes);
  Console.WriteLine($"{args[0]}({memberName}) -> {outputPath} ({member.ObjectBytes.Length} bytes)");
  return 0;
}

static int Unknown(string command)
{
  Console.Error.WriteLine($"galib: \"{command}\" is not a known command.");
  PrintUsage();
  return 1;
}

static CvmLibraryMember ReadMember(string path)
{
  byte[] bytes = File.ReadAllBytes(path);
  return new CvmLibraryMember { Name = Path.GetFileName(path), ObjectBytes = bytes };
}

static CvmLibrary? LoadArchiveOrExit(string path, out int failureCode)
{
  failureCode = 0;
  try
  {
    using FileStream stream = File.OpenRead(path);
    return CvmLibrary.Load(stream);
  }
  catch (FileNotFoundException)
  {
    Console.Error.WriteLine($"galib: \"{path}\" does not exist.");
    failureCode = 1;
    return null;
  }
  catch (InvalidDataException exception)
  {
    Console.Error.WriteLine($"galib: \"{path}\" is not a valid CVM library: {exception.Message}");
    failureCode = 1;
    return null;
  }
}

// Writes to a temporary file first and only replaces the real archive path once Save has fully
// succeeded, so a validation failure (or a mid-write I/O error) can never leave a half-written,
// corrupt .galib behind in place of a previously-good one -- this matters most for "add", which
// overwrites an archive that may already have other members someone cares about.
static int SaveAndReport(CvmLibrary library, string archivePath, string verb)
{
  string tempPath = archivePath + ".tmp";
  bool success;
  IReadOnlyList<string> errors;
  using (FileStream stream = File.Create(tempPath))
  {
    (success, errors) = library.Save(stream);
  }

  if (!success)
  {
    File.Delete(tempPath);
    foreach (string error in errors)
    {
      Console.Error.WriteLine($"galib: error: {error}");
    }

    return 1;
  }

  File.Move(tempPath, archivePath, overwrite: true);
  Console.WriteLine($"{archivePath}: {verb} ({library.Members.Count} member(s), {library.SymbolIndex.Count} exported symbol(s))");
  return 0;
}

static void PrintUsage()
{
  Console.WriteLine("""
      galib -- CVM librarian

      Usage:
        galib create <archive.galib> <member1.gaobj> [member2.gaobj ...]
        galib add <archive.galib> <member1.gaobj> [member2.gaobj ...]
        galib list <archive.galib>
        galib extract <archive.galib> <memberName> [-o <output.gaobj>]

      An archive is a bundle of complete .gaobj files plus an index of every member's exported
      (.export'ed) symbols, so a future linker can pull in only the members a program actually needs.
      """);
}