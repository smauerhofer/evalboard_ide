namespace Ga144.C.Toolchain;

/// <summary>
/// A plain filesystem <see cref="ICIncludeResolver"/>, exactly like a C compiler's command line -- a
/// quoted "#include \"name\"" first looks alongside the including file, then falls back to the same
/// "-I" search path list an angle-bracketed "#include &lt;name&gt;" uses from the start. Shared by both
/// CLI front ends (<c>gcpp</c>, the standalone preprocessor, and <c>gcc144</c>, the C compiler) so
/// neither needs its own copy; the IDE will eventually supply its own resolver built from a C project's
/// own include directory and its library references' include directories instead of this one -- neither
/// resolver depends on the other, which is exactly why <see cref="CPreprocessor"/> only ever talks to
/// the <see cref="ICIncludeResolver"/> interface.
/// </summary>
public sealed class FileSystemIncludeResolver(IReadOnlyList<string> searchDirectories) : ICIncludeResolver
{
  public bool TryResolve(string includeName, bool isAngled, string includingFileName, out CResolvedInclude? resolved)
  {
    if (!isAngled)
    {
      string? includingDirectory = Path.GetDirectoryName(Path.GetFullPath(includingFileName));
      if (includingDirectory is not null && TryReadFrom(includingDirectory, includeName, out resolved))
      {
        return true;
      }
    }

    foreach (string directory in searchDirectories)
    {
      if (TryReadFrom(directory, includeName, out resolved))
      {
        return true;
      }
    }

    resolved = null;
    return false;
  }

  private static bool TryReadFrom(string directory, string includeName, out CResolvedInclude? resolved)
  {
    string candidate = Path.GetFullPath(Path.Combine(directory, includeName));
    if (File.Exists(candidate))
    {
      resolved = new CResolvedInclude(candidate, File.ReadAllText(candidate));
      return true;
    }

    resolved = null;
    return false;
  }
}
