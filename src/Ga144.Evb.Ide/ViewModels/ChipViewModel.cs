using System.Collections.ObjectModel;
using System.Windows;
using Ga144.Evb.Ide.Models;
using Ga144.Evb.Ide.Services;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class ChipViewModel : ObservableObject, IAsyncDisposable
{
    private bool _disposed;

    public ChipViewModel(
        ProjectViewModel project,
        Ga144ChipRole role,
        Ga144RomLibrary romLibrary,
        string romLibraryPath,
        Func<Task> saveRomLibraryAsync,
        Func<KrakenEndpointInfo?> krakenEndpointResolver,
        KrakenLiveController krakenController)
    {
        Project = project;
        Role = role;
        Chip = project.GetChip(role);
        RomLibrary = romLibrary;
        RomLibraryPath = romLibraryPath;
        SaveRomLibraryAsync = saveRomLibraryAsync;
        KrakenEndpointResolver = krakenEndpointResolver;
        KrakenController = krakenController ?? throw new ArgumentNullException(nameof(krakenController));
        KrakenController.StateChanged += OnKrakenControllerStateChanged;
        ToggleKrakenCommand = new RelayCommand(ToggleKraken, () => !KrakenController.HardwareErected);
        RebuildNodes();
    }

    public ProjectViewModel Project { get; }
    public Ga144ChipRole Role { get; }
    public Ga144ChipConfiguration Chip { get; }
    public Ga144RomLibrary RomLibrary { get; }
    public string RomLibraryPath { get; }
    public Func<Task> SaveRomLibraryAsync { get; }
    public Func<KrakenEndpointInfo?> KrakenEndpointResolver { get; }
    public KrakenLiveController KrakenController { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public RelayCommand ToggleKrakenCommand { get; }
    public string Title => $"{Project.Name} — {Chip.Name}";
    public bool KrakenInstalled => Chip.Kraken.Enabled;
    public string KrakenButtonText => KrakenInstalled ? "Remove Kraken" : "Install 3-tentacle Kraken";
    public string KrakenStatusText => KrakenInstalled
        ? BuildKrakenStatus() + BuildRuntimeStatus()
        : "No Kraken installed on this chip.";

    public IReadOnlyDictionary<int, KrakenNodeRoute> KrakenRoutes => KrakenTopology.BuildRouteMap(Chip.Kraken);

    public void RefreshNodes() => RebuildNodes();

    public void RefreshKrakenRuntimeStatus()
    {
        OnPropertyChanged(nameof(KrakenStatusText));
        ToggleKrakenCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Chip windows do not own the Kraken controller. It is process-lifetime
    /// state owned by MainWindowViewModel so closing/reopening this window can
    /// never release the resident Kraken endpoint reservation.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            KrakenController.StateChanged -= OnKrakenControllerStateChanged;
        }

        return ValueTask.CompletedTask;
    }

    private void ToggleKraken()
    {
        if (KrakenController.HardwareErected)
        {
            throw new InvalidOperationException(
                "The project-side Kraken topology cannot be installed/removed while the hardware Kraken is running. " +
                "The resident Kraken endpoint must remain reserved and the chip must not be reset/re-erected.");
        }

        if (Chip.Kraken.Enabled)
        {
            Chip.Kraken.Remove();
        }
        else
        {
            Chip.Kraken.InstallDefault();
        }

        KrakenController.InvalidateAsync().GetAwaiter().GetResult();
        Project.NotifyProjectChanged();
        RebuildNodes();
        OnPropertyChanged(nameof(KrakenInstalled));
        OnPropertyChanged(nameof(KrakenButtonText));
        OnPropertyChanged(nameof(KrakenStatusText));
    }

    private void RebuildNodes()
    {
        IReadOnlyDictionary<int, KrakenNodeRoute> routes = KrakenTopology.BuildRouteMap(Chip.Kraken);
        Nodes.Clear();
        foreach (Ga144NodeConfiguration node in Chip.Nodes
                     .OrderByDescending(item => item.Coordinate / 100)
                     .ThenBy(item => item.Coordinate % 100))
        {
            routes.TryGetValue(node.Coordinate, out KrakenNodeRoute? route);
            Nodes.Add(new NodeViewModel(node, route));
        }
    }

    private string BuildKrakenStatus()
    {
        Chip.Kraken.Normalize();
        string lengths = string.Join(", ", Chip.Kraken.Tentacles
            .OrderBy(item => item.Number)
            .Select(item => $"T{item.Number} {item.Nodes.Count} nodes"));
        int covered = Chip.Kraken.Tentacles.Sum(item => item.Nodes.Count);
        return $"Kraken head 708; {lengths}. {covered}/143 non-head nodes covered.";
    }

    private string BuildRuntimeStatus()
    {
        if (!KrakenController.HardwareErected)
        {
            return string.Empty;
        }

        string endpoint = KrakenController.CurrentEndpoint?.PortName ?? "serial endpoint";
        if (KrakenController.TransportFaulted)
        {
            return $" Hardware Kraken RESERVED on {endpoint}; COM is parked while idle. " +
                   $"Transport/topology fault: {KrakenController.FaultText} No reset/probe/re-erection will be attempted.";
        }

        return $" Hardware Kraken ONLINE on {endpoint}; the Win32 COM handle is opened only during explicit Kraken operations and closed/parked while idle; no SerialPort event loop is used. " +
               "Reset, node-708 probing, and re-erection are forbidden while Kraken is resident; the COM endpoint remains reserved.";
    }

    private void OnKrakenControllerStateChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            OnPropertyChanged(nameof(KrakenStatusText));
            ToggleKrakenCommand.NotifyCanExecuteChanged();
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Refresh);
        }
        else
        {
            Refresh();
        }
    }
}
