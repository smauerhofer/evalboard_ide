using Ga144.Evb.Ide.Models;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class SerialPortViewModel : ObservableObject
{
    private ProbeState _probeState;
    private string _probeMessage = "Not probed";
    private bool _isGa144Node708;
    private string _assignmentText = "Unassigned";

    public SerialPortViewModel(SerialPortInfo port)
    {
        Port = port;
        ApplyIdentityHintToInitialState();
    }

    public SerialPortInfo Port { get; private set; }

    public string PortName => Port.PortName;
    public string FriendlyName => Port.FriendlyName;
    public string Manufacturer => Port.Manufacturer ?? string.Empty;
    public string PnpDeviceId => Port.PnpDeviceId ?? string.Empty;
    public string VidPid => string.IsNullOrWhiteSpace(Port.Vid) ? string.Empty : $"{Port.Vid}:{Port.Pid}";
    public string SerialNumber => Port.SerialNumber ?? string.Empty;
    public string StableId => Port.StableId;
    public bool IsFtdi => Port.IsFtdi;
    public EvalBoardSerialHint? EvalBoardHint => Port.EvalBoardHint;
    public bool HasEvalBoardHint => EvalBoardHint is not null;
    public bool IdentitySuggestsNode708 => EvalBoardHint?.IsNode708Port == true;
    public bool ShouldProbeNode708 => IsFtdi && (EvalBoardHint is null || EvalBoardHint.IsNode708Port);
    public string EvalBoardHintText => EvalBoardHint?.DisplayText ?? string.Empty;
    public string SuggestedPortText => EvalBoardHint is null
        ? string.Empty
        : $"USB {EvalBoardHint.PortDesignator}";

    public ProbeState ProbeState
    {
        get => _probeState;
        set
        {
            if (SetProperty(ref _probeState, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string ProbeMessage
    {
        get => _probeMessage;
        set => SetProperty(ref _probeMessage, value);
    }

    /// <summary>
    /// True only after an active reset/boot/challenge probe has succeeded.
    /// An FTDI serial-number hint alone does not set this flag.
    /// </summary>
    public bool IsGa144Node708
    {
        get => _isGa144Node708;
        set => SetProperty(ref _isGa144Node708, value);
    }

    public string AssignmentText
    {
        get => _assignmentText;
        set => SetProperty(ref _assignmentText, value);
    }

    public string StatusText => ProbeState switch
    {
        ProbeState.Probing => "Probing...",
        ProbeState.Detected => "Detected",
        ProbeState.NoResponse => HasEvalBoardHint ? $"USB {EvalBoardHint!.PortDesignator} / no reply" : "No response",
        ProbeState.Error => "Error",
        ProbeState.Identified => $"USB {EvalBoardHint!.PortDesignator}",
        ProbeState.Mapped => "Mapped",
        _ => IsFtdi ? "FTDI" : "Serial"
    };

    public void UpdatePort(SerialPortInfo port)
    {
        EvalBoardSerialHint? previousHint = EvalBoardHint;
        Port = port;
        OnPropertyChanged(nameof(PortName));
        OnPropertyChanged(nameof(FriendlyName));
        OnPropertyChanged(nameof(Manufacturer));
        OnPropertyChanged(nameof(PnpDeviceId));
        OnPropertyChanged(nameof(VidPid));
        OnPropertyChanged(nameof(SerialNumber));
        OnPropertyChanged(nameof(StableId));
        OnPropertyChanged(nameof(IsFtdi));
        OnPropertyChanged(nameof(EvalBoardHint));
        OnPropertyChanged(nameof(HasEvalBoardHint));
        OnPropertyChanged(nameof(IdentitySuggestsNode708));
        OnPropertyChanged(nameof(ShouldProbeNode708));
        OnPropertyChanged(nameof(EvalBoardHintText));
        OnPropertyChanged(nameof(SuggestedPortText));

        if (ProbeState is ProbeState.NotProbed or ProbeState.Identified or ProbeState.Mapped)
        {
            bool hintChanged = !Equals(previousHint, EvalBoardHint);
            if (hintChanged || ProbeState == ProbeState.NotProbed)
            {
                ApplyIdentityHintToInitialState();
            }
        }

        OnPropertyChanged(nameof(StatusText));
    }

    public void RestoreIdentityStateWhenUnmapped()
    {
        if (EvalBoardHint is null)
        {
            ProbeState = ProbeState.NotProbed;
            ProbeMessage = "Not probed";
            return;
        }

        ProbeState = ProbeState.Identified;
        ProbeMessage = BuildIdentityMessage(EvalBoardHint);
    }

    private void ApplyIdentityHintToInitialState()
    {
        if (EvalBoardHint is null)
        {
            if (ProbeState is ProbeState.NotProbed or ProbeState.Identified)
            {
                ProbeState = ProbeState.NotProbed;
                ProbeMessage = "Not probed";
            }

            return;
        }

        ProbeState = ProbeState.Identified;
        ProbeMessage = BuildIdentityMessage(EvalBoardHint);
    }

    private static string BuildIdentityMessage(EvalBoardSerialHint hint)
    {
        return hint.IsNode708Port
            ? $"FTDI serial identifies {hint.DisplayText}; active node-708 verification is pending."
            : $"FTDI serial identifies {hint.DisplayText}; USB B is not a node-708 endpoint.";
    }
}
