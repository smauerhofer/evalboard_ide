namespace Ga144.Evb.Ide.Services;

public sealed class ConfigurationPathProvider
{
  public ConfigurationPathProvider(string? explicitPath)
  {
    string applicationFolder = ResolveApplicationFolder();

    RomLibraryPath = Path.Combine(applicationFolder, "ga144-rom.yaml");

    if (string.IsNullOrWhiteSpace(explicitPath))
    {
      ConfigurationPath = Path.Combine(applicationFolder, "workspace.yaml");
      LegacyJsonPath = Path.Combine(applicationFolder, "evalboards.json");
      return;
    }

    string expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
    if (string.Equals(Path.GetExtension(expanded), ".json", StringComparison.OrdinalIgnoreCase))
    {
      LegacyJsonPath = expanded;
      ConfigurationPath = Path.ChangeExtension(expanded, ".yaml");
    }
    else
    {
      ConfigurationPath = expanded;
      LegacyJsonPath = Path.Combine(
          Path.GetDirectoryName(expanded) ?? applicationFolder,
          "evalboards.json");
    }
  }

  // The data directory holds the ROM library and (unless an explicit path is given)
  // the workspace. It is taken from the GA144IDE_DATA environment variable when that
  // is set to a non-empty value; otherwise it falls back to the per-user
  // LocalApplicationData location. Environment variables inside GA144IDE_DATA are
  // expanded and the result is made absolute.
  private static string ResolveApplicationFolder()
  {
    string? dataDirectory = Environment.GetEnvironmentVariable("GA144IDE_DATA");
    if (!string.IsNullOrWhiteSpace(dataDirectory))
    {
      return Path.GetFullPath(Environment.ExpandEnvironmentVariables(dataDirectory));
    }

    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ga144EvalboardIde");
  }

  public string ConfigurationPath { get; }
  public string LegacyJsonPath { get; }
  public string RomLibraryPath { get; }
}