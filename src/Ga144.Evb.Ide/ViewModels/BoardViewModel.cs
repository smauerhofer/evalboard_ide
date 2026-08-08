using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class BoardViewModel : ObservableObject
{
  private readonly Action _changed;
  private bool _isConnected;
  private int _connectedPortCount;

  public BoardViewModel(Ga144Board model, Action changed)
  {
    Model = model;
    _changed = changed;
    Model.Normalize();
  }

  public Ga144Board Model { get; }
  public Guid Id => Model.Id;

  public string Name
  {
    get => Model.Name;
    set
    {
      string normalized = string.IsNullOrWhiteSpace(value) ? "GA144 Evalboard" : value.Trim();
      if (string.Equals(Model.Name, normalized, StringComparison.Ordinal))
      {
        return;
      }

      Model.Name = normalized;
      OnPropertyChanged();
      OnPropertyChanged(nameof(DisplayName));
      _changed();
    }
  }

  public string SerialNumber
  {
    get => Model.SerialNumber ?? string.Empty;
    set
    {
      string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
      if (string.Equals(Model.SerialNumber, normalized, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      Model.SerialNumber = normalized;
      OnPropertyChanged();
      OnPropertyChanged(nameof(DisplayName));
      OnPropertyChanged(nameof(IdentityText));
      _changed();
    }
  }

  public EvalBoardModel BoardModel
  {
    get => Model.Model;
    set
    {
      if (Model.Model == value)
      {
        return;
      }

      Model.Model = value;
      Model.ApplyDefaultJumpers(overwriteExisting: false);
      OnPropertyChanged();
      OnPropertyChanged(nameof(DisplayName));
      OnPropertyChanged(nameof(IdentityText));
      OnPropertyChanged(nameof(BoardDescription));
      OnPropertyChanged(nameof(BoardVisualRevision));
      _changed();
    }
  }

  public string DisplayName
  {
    get
    {
      string identity = BoardModel == EvalBoardModel.Unknown
          ? "unidentified"
          : string.IsNullOrWhiteSpace(SerialNumber)
              ? BoardModel.ToString()
              : $"{BoardModel} S{SerialNumber}";
      string connection = IsConnected ? "connected" : "offline";
      return $"{Name} ({identity}, {connection})";
    }
  }

  public string IdentityText => BoardModel == EvalBoardModel.Unknown
      ? "Unidentified evalboard"
      : string.IsNullOrWhiteSpace(SerialNumber)
          ? BoardModel.ToString()
          : $"{BoardModel} S{SerialNumber}";

  public bool IsConnected
  {
    get => _isConnected;
    private set
    {
      if (SetProperty(ref _isConnected, value))
      {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ConnectionText));
      }
    }
  }

  public int ConnectedPortCount
  {
    get => _connectedPortCount;
    private set
    {
      if (SetProperty(ref _connectedPortCount, value))
      {
        OnPropertyChanged(nameof(ConnectionText));
      }
    }
  }

  public string ConnectionText
  {
    get
    {
      if (IsConnected)
      {
        return $"Connected — {ConnectedPortCount} FTDI interface(s) present";
      }

      return Model.LastSeenUtc is DateTimeOffset lastSeen
          ? $"Offline — last seen {lastSeen.LocalDateTime:g}"
          : "Not currently connected";
    }
  }

  public string BoardDescription => BoardModel == EvalBoardModel.Unknown
      ? "Select EVB001 or EVB002 to display the selected physical board."
      : $"{IdentityText} — board hardware and the active project are selected independently. Click a jumper to toggle it, click H or T to open the corresponding chip from the active project, or select an FTDI row and click A, B, or C to assign that interface to this board.";

  public string BoardVisualRevision => $"{BoardModel}:{Model.Jumpers.Count}:{PortASummary}:{PortBSummary}:{PortCSummary}";

  public string PortASummary => FormatBinding(Model.PortA);
  public string PortBSummary => FormatBinding(Model.PortB);
  public string PortCSummary => FormatBinding(Model.PortC);

  public string GetPortSummary(EvalBoardPortRole role) => role switch
  {
    EvalBoardPortRole.PortAHost => PortASummary,
    EvalBoardPortRole.PortBGeneral => PortBSummary,
    EvalBoardPortRole.PortCTarget => PortCSummary,
    _ => "Not assigned"
  };

  public bool IsJumperInstalled(string jumperId) =>
      Model.Jumpers.TryGetValue(jumperId, out bool installed) && installed;

  public void ToggleJumper(string jumperId)
  {
    Model.Jumpers[jumperId] = !IsJumperInstalled(jumperId);
    _changed();
    OnPropertyChanged(nameof(BoardVisualRevision));
  }

  public void RefreshBindings()
  {
    OnPropertyChanged(nameof(PortASummary));
    OnPropertyChanged(nameof(PortBSummary));
    OnPropertyChanged(nameof(PortCSummary));
    OnPropertyChanged(nameof(BoardVisualRevision));
  }

  public void UpdateConnectionState(IEnumerable<SerialPortInfo> ports)
  {
    ArgumentNullException.ThrowIfNull(ports);
    int count = ports.Count(IsBoundPort);
    ConnectedPortCount = count;
    IsConnected = count > 0;
    if (IsConnected)
    {
      Model.LastSeenUtc = DateTimeOffset.UtcNow;
    }
  }

  private bool IsBoundPort(SerialPortInfo port) =>
      Model.PortA?.Ftdi.Matches(port) == true ||
      Model.PortB?.Ftdi.Matches(port) == true ||
      Model.PortC?.Ftdi.Matches(port) == true;

  private static string FormatBinding(BoardPortBinding? binding)
  {
    if (binding is null)
    {
      return "Not assigned";
    }

    string serial = string.IsNullOrWhiteSpace(binding.Ftdi.SerialNumber)
        ? binding.Ftdi.StableId
        : binding.Ftdi.SerialNumber;
    string port = string.IsNullOrWhiteSpace(binding.LastKnownComPort)
        ? "COM port unknown"
        : binding.LastKnownComPort;
    return $"{port} — FTDI {serial}";
  }
}
