using System.Windows;
using System.Windows.Interop;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Raises <see cref="DeviceChanged"/> only when Windows reports an actual USB
/// device arrival or removal (WM_DEVICECHANGE), instead of polling the device
/// tree on a timer.
///
/// Why this exists: the previous design re-ran a WMI Win32_PnPEntity query plus
/// SerialPort.GetPortNames() every ~1.5 s. On a machine with a single xHCI host
/// controller shared with a KVM's emulated HID, that periodic device-tree walk
/// generates continuous host-side USB activity that intermittently stalls the
/// mouse/keyboard for the whole life of the process. Enumerating only on real
/// change events makes an idle system generate zero USB polling.
///
/// Debounced: a burst of DBT_DEVNODES_CHANGED / interface-arrival messages during
/// a single physical event collapses into one DeviceChanged raise.
/// </summary>
public sealed class SerialDeviceChangeWatcher : IDisposable
{
  private const int WmDeviceChange = 0x0219;
  private const int DbtDevNodesChanged = 0x0007;
  private const int DbtDeviceArrival = 0x8000;
  private const int DbtDeviceRemoveComplete = 0x8004;

  private readonly Window _window;
  private readonly Action _onChanged;
  private readonly System.Windows.Threading.DispatcherTimer _debounce;
  private HwndSource? _source;
  private bool _hooked;
  private bool _disposed;

  /// <param name="window">The main window whose HWND receives WM_DEVICECHANGE.</param>
  /// <param name="onChanged">
  /// Invoked on the UI thread after a debounced device change. Keep it fast;
  /// it should schedule the actual (async) enumeration, not block.
  /// </param>
  /// <param name="debounceMilliseconds">
  /// Collapse a burst of change messages from one physical event into a single
  /// raise. 400 ms comfortably covers FTDI re-enumeration after an erection
  /// reset without feeling sluggish.
  /// </param>
  public SerialDeviceChangeWatcher(Window window, Action onChanged, int debounceMilliseconds = 400)
  {
    _window = window ?? throw new ArgumentNullException(nameof(window));
    _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
    _debounce = new System.Windows.Threading.DispatcherTimer
    {
      Interval = TimeSpan.FromMilliseconds(Math.Max(50, debounceMilliseconds))
    };
    _debounce.Tick += OnDebounceElapsed;
  }

  public event EventHandler? DeviceChanged;

  /// <summary>
  /// Begin receiving device-change notifications. Safe to call once the window
  /// has a native handle (e.g. from Window.SourceInitialized or Loaded).
  /// </summary>
  public void Start()
  {
    if (_disposed || _hooked)
    {
      return;
    }

    var helper = new WindowInteropHelper(_window);
    IntPtr handle = helper.Handle;
    if (handle == IntPtr.Zero)
    {
      // The window has no HWND yet. Defer until it does.
      _window.SourceInitialized += OnSourceInitialized;
      return;
    }

    HookHandle(handle);
  }

  private void OnSourceInitialized(object? sender, EventArgs e)
  {
    _window.SourceInitialized -= OnSourceInitialized;
    var helper = new WindowInteropHelper(_window);
    HookHandle(helper.Handle);
  }

  private void HookHandle(IntPtr handle)
  {
    if (_disposed || _hooked || handle == IntPtr.Zero)
    {
      return;
    }

    _source = HwndSource.FromHwnd(handle);
    if (_source is null)
    {
      return;
    }

    _source.AddHook(WndProc);
    _hooked = true;
  }

  private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (msg == WmDeviceChange)
    {
      int eventType = wParam.ToInt32();
      if (eventType is DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged)
      {
        // Coalesce bursts: restart the debounce window on each message.
        _debounce.Stop();
        _debounce.Start();
      }
    }

    // Never mark handled: other listeners (and the default proc) must still
    // see WM_DEVICECHANGE.
    return IntPtr.Zero;
  }

  private void OnDebounceElapsed(object? sender, EventArgs e)
  {
    _debounce.Stop();
    if (_disposed)
    {
      return;
    }

    try
    {
      _onChanged();
    }
    catch
    {
      // A failed scan request must not tear down the watcher.
    }

    DeviceChanged?.Invoke(this, EventArgs.Empty);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _debounce.Stop();
    _debounce.Tick -= OnDebounceElapsed;
    _window.SourceInitialized -= OnSourceInitialized;

    if (_hooked && _source is not null)
    {
      _source.RemoveHook(WndProc);
    }
    _source = null;
    _hooked = false;
  }
}