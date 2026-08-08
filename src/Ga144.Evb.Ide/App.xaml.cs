using Ga144.Evb.Ide.Services;
using Ga144.Evb.Ide.ViewModels;
using System.Windows;

namespace Ga144.Evb.Ide;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    string? explicitConfigurationPath = GetOption(e.Args, "--config");
    var pathProvider = new ConfigurationPathProvider(explicitConfigurationPath);
    var configurationStore = new YamlConfigurationStore(pathProvider.ConfigurationPath, pathProvider.LegacyJsonPath);
    var romLibraryStore = new Ga144RomLibraryStore(pathProvider.RomLibraryPath);
    var discovery = new SerialPortDiscoveryService();
    var probe = new Ga144Node708Probe();

    var viewModel = new MainWindowViewModel(
        configurationStore,
        romLibraryStore,
        discovery,
        probe,
        pathProvider.ConfigurationPath,
        pathProvider.RomLibraryPath);

    var window = new MainWindow(viewModel);
    MainWindow = window;
    window.Show();
  }

  private static string? GetOption(IReadOnlyList<string> args, string option)
  {
    for (int i = 0; i < args.Count - 1; i++)
    {
      if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
      {
        return args[i + 1];
      }
    }

    return null;
  }
}
