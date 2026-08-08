using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ga144.Evb.Ide.Services;

/// <summary>
/// Minimal synchronous Win32 COM transport used only by a live Kraken.
///
/// This deliberately bypasses System.IO.Ports.SerialPort after Kraken erection.
/// No WaitCommEvent/event mask is installed and no background reader/event
/// thread is created by this class. The live Kraken may close the native handle
/// while idle and reopen it only for explicit transactions.
/// </summary>
internal sealed class NativeWindowsSerialPort : IDisposable
{
  private const uint GenericRead = 0x80000000;
  private const uint GenericWrite = 0x40000000;
  private const uint OpenExisting = 3;

  private const uint PurgeTxClear = 0x0004;
  private const uint PurgeRxClear = 0x0008;

  // FTDI modem-control (RTS/DTR) writes are USB control transfers; allow time
  // for the request to commit before the final CloseHandle in CloseForIdle
  // (used only by the CloseWhileIdle policy).
  private const int FtdiModemControlCommitMilliseconds = 8;

  private const uint EscapeSetRts = 3;
  private const uint EscapeClearRts = 4;
  private const uint EscapeSetDtr = 5;
  private const uint EscapeClearDtr = 6;

  private const uint FBinary = 0x00000001;
  private const uint FParity = 0x00000002;
  private const uint FOutxCtsFlow = 0x00000004;
  private const uint FOutxDsrFlow = 0x00000008;
  private const uint FDtrControlMask = 0x00000030;
  private const uint FDtrControlEnable = 0x00000010;
  private const uint FDsrSensitivity = 0x00000040;
  private const uint FTxContinueOnXoff = 0x00000080;
  private const uint FOutX = 0x00000100;
  private const uint FInX = 0x00000200;
  private const uint FErrorChar = 0x00000400;
  private const uint FNull = 0x00000800;
  private const uint FRtsControlMask = 0x00003000;
  private const uint FRtsControlEnable = 0x00001000;
  private const uint FAbortOnError = 0x00004000;

  private readonly SafeFileHandle _handle;
  private bool _disposed;

  private NativeWindowsSerialPort(SafeFileHandle handle, string portName, int baudRate)
  {
    _handle = handle;
    PortName = portName;
    BaudRate = baudRate;
  }

  public string PortName { get; }
  public int BaudRate { get; }
  public bool IsOpen => !_disposed && !_handle.IsClosed && !_handle.IsInvalid;

  public static NativeWindowsSerialPort Open(
      string portName,
      int baudRate,
      int readTimeoutMilliseconds,
      int writeTimeoutMilliseconds)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(portName);
    if (!OperatingSystem.IsWindows())
    {
      throw new PlatformNotSupportedException("Native Kraken COM transport requires Windows.");
    }

    string devicePath = portName.StartsWith("\\\\.\\", StringComparison.Ordinal)
        ? portName
        : $"\\\\.\\{portName}";

    SafeFileHandle handle = CreateFile(
        devicePath,
        GenericRead | GenericWrite,
        0,
        IntPtr.Zero,
        OpenExisting,
        0,
        IntPtr.Zero);

    if (handle.IsInvalid)
    {
      int error = Marshal.GetLastWin32Error();
      handle.Dispose();
      throw CreateWin32IOException($"Could not open {portName}", error);
    }

    try
    {
      // IMPORTANT for an already-erected Kraken: Port A RTS is the EVB
      // host RESET control. Request the inactive/high modem-control state
      // immediately after CreateFile, before doing any slower setup work.
      // This minimizes any driver-created low interval when reopening a
      // parked Kraken COM endpoint.
      if (!EscapeCommFunction(handle, EscapeSetDtr))
      {
        ThrowLastWin32($"Could not set DTR immediately after opening {portName}");
      }
      if (!EscapeCommFunction(handle, EscapeSetRts))
      {
        ThrowLastWin32($"Could not set RTS immediately after opening {portName}");
      }

      int dcbSize = Marshal.SizeOf<Dcb>();
      int timeoutSize = Marshal.SizeOf<CommTimeouts>();
      if (dcbSize != 28 || timeoutSize != 20)
      {
        throw new InvalidOperationException(
            $"Unexpected Win32 serial structure size (DCB={dcbSize}, COMMTIMEOUTS={timeoutSize}).");
      }

      if (!SetupComm(handle, 4096, 4096))
      {
        ThrowLastWin32($"SetupComm failed for {portName}");
      }

      var dcb = new Dcb
      {
        DCBlength = (uint)dcbSize
      };
      if (!GetCommState(handle, ref dcb))
      {
        ThrowLastWin32($"GetCommState failed for {portName}");
      }

      dcb.DCBlength = (uint)dcbSize;
      dcb.BaudRate = checked((uint)baudRate);
      dcb.Flags &= ~(
          FParity |
          FOutxCtsFlow |
          FOutxDsrFlow |
          FDtrControlMask |
          FDsrSensitivity |
          FTxContinueOnXoff |
          FOutX |
          FInX |
          FErrorChar |
          FNull |
          FRtsControlMask |
          FAbortOnError);
      // Binary mode only. RTS/DTR control bits are LEFT CLEARED
      // (= RTS_CONTROL_DISABLE / DTR_CONTROL_DISABLE) so the driver does
      // not drive the modem-control lines during SetCommState; we set them
      // explicitly via EscapeCommFunction. On Port A this keeps the driver
      // from asserting RTS (= EVB RESET-) as a side effect of configuration.
      // This benefits both idle policies and is required to make reopen
      // under CloseWhileIdle as glitch-quiet as the stock driver allows.
      dcb.Flags |= FBinary;
      dcb.ByteSize = 8;
      dcb.Parity = 0;   // NOPARITY
      dcb.StopBits = 0; // ONESTOPBIT

      if (!SetCommState(handle, ref dcb))
      {
        ThrowLastWin32($"SetCommState failed for {portName}");
      }

      var timeouts = new CommTimeouts
      {
        // One bounded synchronous read: return when the requested
        // bytes arrive or when the total timeout expires. No overlapped
        // read is left pending after this call returns.
        ReadIntervalTimeout = 0,
        ReadTotalTimeoutMultiplier = 0,
        ReadTotalTimeoutConstant = checked((uint)Math.Max(1, readTimeoutMilliseconds)),
        WriteTotalTimeoutMultiplier = 0,
        WriteTotalTimeoutConstant = checked((uint)Math.Max(1, writeTimeoutMilliseconds))
      };
      if (!SetCommTimeouts(handle, ref timeouts))
      {
        ThrowLastWin32($"SetCommTimeouts failed for {portName}");
      }

      // Explicitly request no communications events. This transport never
      // calls WaitCommEvent and has no event-monitoring worker.
      if (!SetCommMask(handle, 0))
      {
        ThrowLastWin32($"SetCommMask failed for {portName}");
      }

      var port = new NativeWindowsSerialPort(handle, portName, baudRate);
      port.SetDtr(true);
      port.SetRts(true);
      return port;
    }
    catch
    {
      handle.Dispose();
      throw;
    }
  }

  public void SetRts(bool enabled)
  {
    ThrowIfDisposed();
    if (!EscapeCommFunction(_handle, enabled ? EscapeSetRts : EscapeClearRts))
    {
      ThrowLastWin32($"Could not {(enabled ? "set" : "clear")} RTS on {PortName}");
    }
  }

  public void SetDtr(bool enabled)
  {
    ThrowIfDisposed();
    if (!EscapeCommFunction(_handle, enabled ? EscapeSetDtr : EscapeClearDtr))
    {
      ThrowLastWin32($"Could not {(enabled ? "set" : "clear")} DTR on {PortName}");
    }
  }

  public void PurgeInput()
  {
    Purge(PurgeRxClear, "RX");
  }

  public void PurgeInputOutput()
  {
    Purge(PurgeRxClear | PurgeTxClear, "RX/TX");
  }

  private void Purge(uint flags, string description)
  {
    ThrowIfDisposed();
    if (!PurgeComm(_handle, flags))
    {
      ThrowLastWin32($"PurgeComm {description} failed for {PortName}");
    }
  }

  public void Write(byte[] buffer)
  {
    ArgumentNullException.ThrowIfNull(buffer);
    ThrowIfDisposed();

    int offset = 0;
    while (offset < buffer.Length)
    {
      byte[] chunk;
      if (offset == 0)
      {
        chunk = buffer;
      }
      else
      {
        int remaining = buffer.Length - offset;
        chunk = new byte[remaining];
        Buffer.BlockCopy(buffer, offset, chunk, 0, remaining);
      }

      if (!WriteFile(_handle, chunk, checked((uint)chunk.Length), out uint written, IntPtr.Zero))
      {
        ThrowLastWin32($"WriteFile failed for {PortName}");
      }

      if (written == 0)
      {
        throw new IOException($"WriteFile wrote zero bytes to {PortName}.");
      }

      offset += checked((int)written);
    }
  }

  /// <summary>
  /// Performs one bounded synchronous ReadFile call. Zero means that the
  /// configured native COM read timeout elapsed with no data.
  /// </summary>
  public int Read(byte[] buffer, int offset, int count)
  {
    ArgumentNullException.ThrowIfNull(buffer);
    ArgumentOutOfRangeException.ThrowIfNegative(offset);
    ArgumentOutOfRangeException.ThrowIfNegative(count);
    if (offset + count > buffer.Length)
    {
      throw new ArgumentException("Read range exceeds the destination buffer.", nameof(count));
    }

    ThrowIfDisposed();
    if (count == 0)
    {
      return 0;
    }

    byte[] target = offset == 0 && count == buffer.Length ? buffer : new byte[count];
    if (!ReadFile(_handle, target, checked((uint)count), out uint read, IntPtr.Zero))
    {
      ThrowLastWin32($"ReadFile failed for {PortName}");
    }

    int bytesRead = checked((int)read);
    if (bytesRead > 0 && !ReferenceEquals(target, buffer))
    {
      Buffer.BlockCopy(target, 0, buffer, offset, bytesRead);
    }

    return bytesRead;
  }

  /// <summary>
  /// HoldOpen policy: quiesce a live Kraken handle between transactions WITHOUT
  /// closing it. The handle stays open for the whole Kraken lifetime, so the
  /// FTDI modem-control lines (Port A RTS = EVB RESET-) are held by us
  /// continuously and the device is kept out of USB selective suspend. Only the
  /// RX buffer is discarded so the next transaction starts clean. No CloseHandle
  /// here; real teardown happens only in Dispose.
  /// </summary>
  public void ParkIdle()
  {
    if (_disposed)
    {
      return;
    }

    // Reassert the inactive/high reset + DTR levels defensively (a no-op on a
    // healthy handle) and drop any stale received bytes.
    try { SetDtr(true); } catch { }
    try { SetRts(true); } catch { }
    try { PurgeInput(); } catch { }
  }

  /// <summary>
  /// CloseWhileIdle policy: close a live Kraken handle for an idle period. The
  /// EVB reset line is explicitly left inactive/high immediately before
  /// CloseHandle. Whether a particular FTDI/VCP driver preserves that
  /// modem-control level after the handle is closed is hardware/driver
  /// dependent; the next Kraken access reopens without deliberately pulsing
  /// reset. Used only when the session is configured for CloseWhileIdle.
  /// </summary>
  public void CloseForIdle()
  {
    if (_disposed)
    {
      return;
    }

    // Best effort only: if the USB device is disappearing, closing the
    // handle is still preferable to keeping a broken endpoint alive.
    try { SetDtr(true); } catch { }
    try { SetRts(true); } catch { }   // RESET- inactive/high before close

    // FTDI modem-control writes are USB control transfers with their own
    // latency; 1 ms is frequently too short for the request to actually land
    // before CloseHandle. Give it a realistic commit window. No read/write is
    // pending here.
    Thread.Sleep(FtdiModemControlCommitMilliseconds);
    Dispose();
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _handle.Dispose();
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed || _handle.IsClosed || _handle.IsInvalid, this);
  }

  private static void ThrowLastWin32(string operation)
  {
    int error = Marshal.GetLastWin32Error();
    throw CreateWin32IOException(operation, error);
  }

  private static IOException CreateWin32IOException(string operation, int error)
  {
    var win32 = new Win32Exception(error);
    return new IOException($"{operation}: {win32.Message} (Win32 error {error}).", win32);
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct Dcb
  {
    public uint DCBlength;
    public uint BaudRate;
    public uint Flags;
    public ushort wReserved;
    public ushort XonLim;
    public ushort XoffLim;
    public byte ByteSize;
    public byte Parity;
    public byte StopBits;
    public byte XonChar;
    public byte XoffChar;
    public byte ErrorChar;
    public byte EofChar;
    public byte EvtChar;
    public ushort wReserved1;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct CommTimeouts
  {
    public uint ReadIntervalTimeout;
    public uint ReadTotalTimeoutMultiplier;
    public uint ReadTotalTimeoutConstant;
    public uint WriteTotalTimeoutMultiplier;
    public uint WriteTotalTimeoutConstant;
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern SafeFileHandle CreateFile(
      string lpFileName,
      uint dwDesiredAccess,
      uint dwShareMode,
      IntPtr lpSecurityAttributes,
      uint dwCreationDisposition,
      uint dwFlagsAndAttributes,
      IntPtr hTemplateFile);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetupComm(SafeFileHandle hFile, uint dwInQueue, uint dwOutQueue);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetCommState(SafeFileHandle hFile, ref Dcb lpDcb);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetCommState(SafeFileHandle hFile, ref Dcb lpDcb);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref CommTimeouts lpCommTimeouts);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool SetCommMask(SafeFileHandle hFile, uint dwEvtMask);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool EscapeCommFunction(SafeFileHandle hFile, uint dwFunc);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool PurgeComm(SafeFileHandle hFile, uint dwFlags);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool ReadFile(
      SafeFileHandle hFile,
      [Out] byte[] lpBuffer,
      uint nNumberOfBytesToRead,
      out uint lpNumberOfBytesRead,
      IntPtr lpOverlapped);

  [DllImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool WriteFile(
      SafeFileHandle hFile,
      byte[] lpBuffer,
      uint nNumberOfBytesToWrite,
      out uint lpNumberOfBytesWritten,
      IntPtr lpOverlapped);
}