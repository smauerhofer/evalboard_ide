namespace Ga144.Evb.Ide.Services;

public sealed class ConfigurationPathProvider
{
    public ConfigurationPathProvider(string? explicitPath)
    {
        string applicationFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ga144EvalboardIde");

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

    public string ConfigurationPath { get; }
    public string LegacyJsonPath { get; }
    public string RomLibraryPath { get; }
}
