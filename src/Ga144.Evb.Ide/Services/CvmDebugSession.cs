using Ga144.Cvm.Toolchain;
using Ga144.Evb.Ide.Compiler;
using Ga144.Evb.Ide.Cvm;

namespace Ga144.Evb.Ide.Services;

/// <summary>Why a <see cref="CvmDebugSession"/> is not currently servicing the wire.</summary>
public enum CvmDebugPauseReason
{
  /// <summary>A Step or Continue is in progress right now (only observable mid-call from another thread).</summary>
  NotPaused,
  Started,
  StepComplete,
  Breakpoint,
  TransactionCapReached,
  UserPaused,
  Faulted
}

/// <summary>
/// One completed (or, for a freshly breakpointed read, half-completed) memory-interface transaction,
/// as reported by <see cref="CvmDebugSession.Step"/>/<see cref="CvmDebugSession.Continue"/>.
/// <see cref="HitBreakpoint"/> is true exactly when THIS transaction just newly halted further
/// progress: for a read that means its reply was withheld (the real CVM is blocked on the wire
/// waiting for it right now); for a write it is purely informational, since a write has no reply to
/// withhold. It is false again on the transaction that later resumes a previously withheld read --
/// that one already reported the halt when it first occurred.
/// </summary>
public sealed record CvmDebugTransaction(
    bool IsWrite,
    int Page,
    int AddressInPage,
    int FlatAddress,
    int Value,
    IReadOnlyList<int> RawWords,
    bool HitBreakpoint,
    string LogLine);

/// <summary>
/// Drives a live CVM install interactively instead of running it to completion:
/// <see cref="Ga144CvmHardwareInstaller.StartDebugSessionAsync"/> compiles and boots the mesh, loads
/// the debugger's own test program (<see cref="CvmMemoryProtocol.TryBuildDebuggerTestProgram"/> --
/// deliberately its own variant, not the one the automatic "Install &amp; run CVM test" uses; see
/// that method's remarks) into a fresh <see cref="CvmSimulatedSram"/>, and wakes node 708's
/// <c>'start</c> -- but hands back this session, with the serial port left open, instead of
/// servicing the resulting traffic to completion. That initial program is only a starting point, not
/// permanent: <see cref="AssembleAndLoadProgram"/> lets the CVM Debugger's own Assembly Code editor
/// overwrite it with hand-written source at any time, so trying out a new opcode no longer requires
/// editing <see cref="CvmMemoryProtocol.TryBuildDebuggerTestProgram"/> and rebuilding the IDE.
///
/// <b>How stepping and breakpoints actually pause real hardware.</b> There is no debug/halt line on
/// this design -- the CVM's only synchronization point with the host is the memory interface itself
/// (<see cref="CvmMemoryProtocol"/>). A READ blocks the physical chip until the host replies, so
/// withholding that reply IS a true hardware pause; a WRITE has no reply to withhold, so a write
/// breakpoint can only halt the host's own bookkeeping right after the write lands -- the chip
/// itself keeps going until its very next memory request (in practice almost always its next
/// instruction fetch, milliseconds later), where it then stalls on the withheld reply anyway. This
/// mirrors exactly what Stefan asked for: "the CVM request memory through the memory interface and
/// if it accesses a breakpoint it should pause execution."
///
/// Not thread-safe against itself -- only one Step/Continue should be in flight at a time (the
/// owning ViewModel is expected to serialize these through its own busy flag, same as every other
/// long-running operation in this codebase) -- but <see cref="Breakpoints"/>/<see cref="AddBreakpoint"/>/
/// <see cref="RemoveBreakpoint"/>/<see cref="TransactionLog"/>/<see cref="ReadMemory"/> are safe to
/// call from the UI thread while a Continue() runs on a background thread, since those only touch
/// state guarded by <see cref="_sync"/> or (for the SRAM/port) already-published, append-only data.
/// </summary>
public sealed class CvmDebugSession : IDisposable
{
  private readonly NativeWindowsSerialPort _port;
  private readonly CvmSimulatedSram _sram;
  private IReadOnlyList<int> _program;
  private readonly IReadOnlyDictionary<int, F18CompileResult> _compiledRam;
  private readonly HashSet<int> _breakpoints = [];
  private readonly List<string> _transactionLog = [];
  private readonly object _sync = new();
  private bool _disposed;

  // Set when a READ has been fully received (both request words) but its reply was withheld
  // because the address hit an armed breakpoint. The next Step() or Continue() call finishes this
  // exact transaction (sends the reply) before doing anything else.
  private (int Page, int AddressInPage, int FlatAddress, int PageWord)? _pendingRead;

  internal CvmDebugSession(
      NativeWindowsSerialPort port,
      CvmSimulatedSram sram,
      IReadOnlyList<int> program,
      CvmInstallReport install,
      IReadOnlyDictionary<int, F18CompileResult> compiledRam)
  {
    _port = port;
    _sram = sram;
    _program = program;
    Install = install;
    _compiledRam = compiledRam;
    PauseReason = CvmDebugPauseReason.Started;
  }

  /// <summary>How the install itself went -- boot frame count, etc. Always successful by the time a session exists (a failed install never produces one).</summary>
  public CvmInstallReport Install { get; }

  /// <summary>
  /// The program currently loaded into page 0 of the simulated SRAM. Initially the debugger's own
  /// fixed test program (the shared 5 'nop/'plit/literal/'pop/'push/8 trailing 'nop sequence, except
  /// word address <see cref="CvmMemoryProtocol.DebuggerCallTestAddress"/> is a <c>call</c> to
  /// <see cref="CvmMemoryProtocol.DebuggerCallTestTarget"/> instead of a plain 'nop, word address
  /// <see cref="CvmMemoryProtocol.DebuggerCallTestTarget"/> itself is a real 'ret -- confirmed working
  /// against real hardware -- completing the call/return round trip, and word address
  /// <see cref="CvmMemoryProtocol.DebuggerBranchTestAddress"/> (exactly where that round trip resumes)
  /// is a raw <c>br <see cref="CvmMemoryProtocol.DebuggerBranchTestOffset"/></c> opcode word instead
  /// of its own plain 'nop; see <see cref="CvmMemoryProtocol.TryBuildDebuggerTestProgram"/>'s own
  /// remarks) -- but replaceable at any time via <see cref="AssembleAndLoadProgram"/>, which is what
  /// the CVM Debugger's own Assembly Code editor does.
  /// </summary>
  public IReadOnlyList<int> Program => _program;

  /// <summary>
  /// Assembles <paramref name="sourceText"/> (<see cref="CvmAssemblyLanguage.ParseSource"/> then
  /// <see cref="CvmAssemblyLanguage.Assemble"/>, resolved against THIS run's own node 607 compile)
  /// and, on success, overwrites the simulated SRAM's page 0 with the result starting at address 0,
  /// zero-filling any leftover tail from a previous, longer <see cref="Program"/> so no stale opcode
  /// lingers past the new program's end, then replaces <see cref="Program"/> with it. This is a live
  /// reprogram, not a reset: the simulated SRAM is what a connected real CVM chip is actually reading
  /// its next instruction fetch from (see <see cref="CvmSimulatedSram"/>'s own remarks), so the chip's
  /// own P register, breakpoints, and transaction log are all left exactly as they were -- only the
  /// content the chip's NEXT fetch will see has changed. Returns an error message (never throws) on a
  /// parse/assemble failure, in which case neither the simulated SRAM nor <see cref="Program"/> is
  /// touched at all.
  /// </summary>
  public (bool Success, string? Error) AssembleAndLoadProgram(string sourceText)
  {
    (List<CvmAssemblyLanguage.CvmAsmInstruction>? instructions, string? parseError) = CvmAssemblyLanguage.ParseSource(sourceText);
    if (instructions is null)
    {
      return (false, parseError);
    }

    (List<int>? words, string? assembleError) = CvmAssemblyLanguage.Assemble(instructions, _compiledRam);
    if (words is null)
    {
      return (false, assembleError);
    }

    int previousLength = _program.Count;
    _sram.LoadProgram(words);
    if (words.Count < previousLength)
    {
      _sram.LoadProgram(new int[previousLength - words.Count], words.Count);
    }

    _program = words;
    return (true, null);
  }

  public int TransactionCount { get; private set; }

  /// <summary>The most recent page-0 (program) read address -- an approximate "PC": this design has no way to see node 607's internal slot cursor, only which word it last fetched.</summary>
  public int? LastFetchAddress { get; private set; }

  public CvmDebugPauseReason PauseReason { get; private set; }

  public string? FaultMessage { get; private set; }

  public IReadOnlyList<string> TransactionLog
  {
    get { lock (_sync) { return _transactionLog.ToArray(); } }
  }

  public IReadOnlyCollection<int> Breakpoints
  {
    get { lock (_sync) { return _breakpoints.ToArray(); } }
  }

  public void AddBreakpoint(int flatAddress)
  {
    lock (_sync) { _breakpoints.Add(flatAddress & (CvmSimulatedSram.WordCapacity - 1)); }
  }

  public void RemoveBreakpoint(int flatAddress)
  {
    lock (_sync) { _breakpoints.Remove(flatAddress); }
  }

  public void ClearBreakpoints()
  {
    lock (_sync) { _breakpoints.Clear(); }
  }

  /// <summary>Read-only snapshot of <paramref name="count"/> simulated-SRAM words starting at <paramref name="startAddress"/>, for the memory inspector.</summary>
  public IReadOnlyList<int> ReadMemory(int startAddress, int count) => _sram.ReadRange(startAddress, count);

  /// <summary>
  /// Looks up any symbol node 607's own compile defines at <paramref name="flatAddress"/> (this only
  /// ever matches a page-0 program address, since 607's symbol table is addresses within ITS OWN
  /// compiled RAM, not the simulated SRAM's page/address space) -- mirrors the '.loc' compiler
  /// directive's own same-address symbol lookup, so the memory inspector can annotate a row like
  /// "0x0005: 0x0800C ('plit)" instead of a bare hex dump.
  /// </summary>
  public string? DescribeProgramSymbol(int flatAddress)
  {
    if (!_compiledRam.TryGetValue(CvmMemoryProtocol.NopSourceNodeCoordinate, out F18CompileResult? compile))
    {
      return null;
    }

    return compile.Symbols.Values.FirstOrDefault(symbol => symbol.Value == flatAddress)?.Name;
  }

  /// <summary>
  /// Linearly disassembles page 0 (the only page that is ever code) from address 0 up to but not
  /// including <paramref name="endAddressExclusive"/>, into CVM assembly language mnemonics
  /// (<see cref="CvmAssemblyLanguage"/>) resolved against node 607's own current compile, plus direct
  /// bit-pattern rules for <c>call</c>/<c>br</c>/<c>ifbr</c>/<c>slit</c>
  /// (<see cref="CvmInstructionSet.TryDescribeSelfDecodingWord"/>, see below) that need no
  /// compile/symbol at all. This
  /// MUST be a stateful scan starting at 0, never an independent per-word decode: pushlit is followed
  /// by a literal operand word that would otherwise be mistaken for its own opcode if a word were
  /// decoded in isolation.
  ///
  /// Returns a sparse map from flat address to a listing line: an instruction's own address gets
  /// its mnemonic, folded together with its operand when it has one (e.g. "pushlit 0x01234") so the
  /// memory inspector reads like a real disassembly rather than two disconnected rows; the operand
  /// word's own address is left out of the map entirely (no note at all), same as any other address
  /// that doesn't fall on a recognized instruction boundary -- typically because it holds an opcode
  /// this debugger doesn't know about yet.
  /// </summary>
  public IReadOnlyDictionary<int, string> DisassemblePage0(int endAddressExclusive)
  {
    IReadOnlyDictionary<int, (string Mnemonic, int WordLength)> decodeTable =
        CvmAssemblyLanguage.BuildDecodeTable(_compiledRam);
    var notes = new Dictionary<int, string>();
    int address = 0;
    while (address < endAddressExclusive)
    {
      int word = _sram.Read(CvmMemoryProtocol.CombineAddress(0, address));

      // "call", "br", "ifbr", and "slit" have no F18 symbol to resolve -- each one's whole word is
      // fully determined by its own bit pattern and operand alone (CvmInstructionSet.
      // CvmOperandEncoding.EmbeddedAddress / EmbeddedSignedValue), independent of node 607's live
      // compile, so all four are checked before consulting the (symbol-driven) decode table at all.
      string? selfDescribing = CvmInstructionSet.TryDescribeSelfDecodingWord(word);
      if (selfDescribing is not null)
      {
        notes[address] = selfDescribing;
        address += 1;
        continue;
      }

      if (decodeTable.TryGetValue(word, out (string Mnemonic, int WordLength) instruction))
      {
        int operandCount = instruction.WordLength - 1;
        if (operandCount == 1 && address + 1 < endAddressExclusive)
        {
          int operandValue = _sram.Read(CvmMemoryProtocol.CombineAddress(0, address + 1));
          notes[address] = $"{instruction.Mnemonic} 0x{operandValue:X4}";
        }
        else
        {
          notes[address] = instruction.Mnemonic;
        }

        address += instruction.WordLength;
      }
      else
      {
        address += 1;
      }
    }

    return notes;
  }

  /// <summary>
  /// Services exactly one transaction, ignoring breakpoints entirely -- a manual Step always
  /// executes the next full instruction fetch (or stack access) worth of wire traffic, breakpoint
  /// or not, resuming a withheld reply first if one is currently pending.
  /// </summary>
  public CvmDebugTransaction Step(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    try
    {
      CvmDebugTransaction transaction = ServiceOneTransaction(honorBreakpoints: false, cancellationToken);
      PauseReason = CvmDebugPauseReason.StepComplete;
      FaultMessage = null;
      return transaction;
    }
    catch (Exception exception) when (exception is TimeoutException or IOException)
    {
      PauseReason = CvmDebugPauseReason.Faulted;
      FaultMessage = exception.Message;
      throw;
    }
  }

  /// <summary>
  /// Services transactions back-to-back until one hits an armed breakpoint, <paramref name="transactionCap"/>
  /// is reached, <paramref name="cancellationToken"/> is cancelled (reported as
  /// <see cref="CvmDebugPauseReason.UserPaused"/>, not a fault), or a timeout/IO error occurs
  /// (reported as <see cref="CvmDebugPauseReason.Faulted"/>, and NOT rethrown -- whatever transactions
  /// completed before the fault are still returned).
  /// </summary>
  public IReadOnlyList<CvmDebugTransaction> Continue(int transactionCap, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    var serviced = new List<CvmDebugTransaction>();
    FaultMessage = null;
    try
    {
      for (int i = 0; i < transactionCap; i++)
      {
        CvmDebugTransaction transaction = ServiceOneTransaction(honorBreakpoints: true, cancellationToken);
        serviced.Add(transaction);
        if (transaction.HitBreakpoint)
        {
          PauseReason = CvmDebugPauseReason.Breakpoint;
          return serviced;
        }
      }

      PauseReason = CvmDebugPauseReason.TransactionCapReached;
      return serviced;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      PauseReason = CvmDebugPauseReason.UserPaused;
      return serviced;
    }
    catch (Exception exception) when (exception is TimeoutException or IOException)
    {
      PauseReason = CvmDebugPauseReason.Faulted;
      FaultMessage = exception.Message;
      return serviced;
    }
  }

  private CvmDebugTransaction ServiceOneTransaction(bool honorBreakpoints, CancellationToken cancellationToken)
  {
    if (_pendingRead is { } pending)
    {
      _pendingRead = null;
      int replyValue = _sram.Read(pending.FlatAddress);
      SendReply(replyValue);
      TransactionCount++;
      if (pending.Page == 0)
      {
        LastFetchAddress = pending.FlatAddress;
      }

      string releasedLine = $"[READ ] {CvmMemoryProtocol.FormatPageAddress(pending.Page, pending.AddressInPage)} -> {replyValue:X4}  " +
          $"(raw [{CvmMemoryProtocol.FormatWords([pending.PageWord, pending.AddressInPage])}])  (breakpoint reply released)";
      Log(releasedLine);

      // HitBreakpoint is false here even though this transaction WAS the withheld breakpoint reply
      // -- Continue()'s loop uses HitBreakpoint to mean "a fresh halt just occurred, stop here",
      // and resuming past a previously-recorded halt should not immediately re-trigger that stop.
      // If the address is still armed (nobody removed the breakpoint), the very next transaction
      // that lands on it will set HitBreakpoint again on its own, same as any other fresh hit.
      return new CvmDebugTransaction(false, pending.Page, pending.AddressInPage, pending.FlatAddress, replyValue,
          [pending.PageWord, pending.AddressInPage], HitBreakpoint: false, releasedLine);
    }

    int pageWord = CvmMemoryProtocol.ReadWord(_port, CvmMemoryProtocol.ResponseTimeoutMilliseconds, cancellationToken);
    bool isWrite = (pageWord & CvmMemoryProtocol.SramWriteFlagBit) != 0;

    if (isWrite)
    {
      int page = (~pageWord) & F18InstructionSet.WordMask;
      int rawAddressInPage = CvmMemoryProtocol.ReadWord(_port, CvmMemoryProtocol.ResponseTimeoutMilliseconds, cancellationToken);
      int value = CvmMemoryProtocol.ReadWord(_port, CvmMemoryProtocol.ResponseTimeoutMilliseconds, cancellationToken);
      int addressInPage = (~rawAddressInPage) & F18InstructionSet.WordMask;
      int address = CvmMemoryProtocol.CombineAddress(page, addressInPage);
      _sram.Write(address, value);
      TransactionCount++;

      // A write has nothing left to withhold by the time we know its address -- it already fully
      // landed on the wire. "Pause" here just means the host stops consuming the CVM's NEXT
      // request; the physical chip stalls there on its own very soon after, almost always at its
      // next instruction fetch.
      bool hitBreakpoint = honorBreakpoints && IsBreakpoint(address);
      string writeLine = $"[WRITE] {CvmMemoryProtocol.FormatPageAddress(page, addressInPage)} <- {value:X4}  " +
          $"(raw [{CvmMemoryProtocol.FormatWords([pageWord, rawAddressInPage, value])}])" +
          (hitBreakpoint ? "  (breakpoint)" : string.Empty);
      Log(writeLine);
      return new CvmDebugTransaction(true, page, addressInPage, address, value, [pageWord, rawAddressInPage, value], hitBreakpoint, writeLine);
    }

    int readPage = pageWord & F18InstructionSet.WordMask;
    int readAddressInPage = CvmMemoryProtocol.ReadWord(_port, CvmMemoryProtocol.ResponseTimeoutMilliseconds, cancellationToken);
    int readAddress = CvmMemoryProtocol.CombineAddress(readPage, readAddressInPage);

    if (honorBreakpoints && IsBreakpoint(readAddress))
    {
      // The real CVM is blocked on the wire waiting for this exact reply right now -- withholding
      // it IS the pause. The next Step()/Continue() call finishes this transaction first (see the
      // _pendingRead branch above).
      _pendingRead = (readPage, readAddressInPage, readAddress, pageWord);
      string armedLine = $"[READ ] {CvmMemoryProtocol.FormatPageAddress(readPage, readAddressInPage)} -> (halted -- reply withheld at breakpoint)  " +
          $"(raw [{CvmMemoryProtocol.FormatWords([pageWord, readAddressInPage])}])";
      Log(armedLine);
      return new CvmDebugTransaction(false, readPage, readAddressInPage, readAddress, 0, [pageWord, readAddressInPage], HitBreakpoint: true, armedLine);
    }

    int readReplyValue = _sram.Read(readAddress);
    SendReply(readReplyValue);
    TransactionCount++;
    if (readPage == 0)
    {
      LastFetchAddress = readAddress;
    }

    string readLine = $"[READ ] {CvmMemoryProtocol.FormatPageAddress(readPage, readAddressInPage)} -> {readReplyValue:X4}  " +
        $"(raw [{CvmMemoryProtocol.FormatWords([pageWord, readAddressInPage])}])";
    Log(readLine);
    return new CvmDebugTransaction(false, readPage, readAddressInPage, readAddress, readReplyValue, [pageWord, readAddressInPage], false, readLine);
  }

  private void SendReply(int value)
  {
    byte[] replyBytes = new byte[3];
    Ga144Node708Probe.EncodeAsynchronousWord(value, replyBytes);
    _port.Write(replyBytes);
    CvmMemoryProtocol.WaitForTransmitDrain(_port, replyBytes.Length);
    Thread.Sleep(CvmMemoryProtocol.InterWordSettleMilliseconds);
  }

  private bool IsBreakpoint(int flatAddress)
  {
    lock (_sync) { return _breakpoints.Contains(flatAddress); }
  }

  private void Log(string line)
  {
    lock (_sync) { _transactionLog.Add(line); }
  }

  private void ThrowIfDisposed()
  {
    if (_disposed)
    {
      throw new ObjectDisposedException(nameof(CvmDebugSession));
    }
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    try { _port.SetRts(true); } catch { }
    try { _port.SetDtr(true); } catch { }
    _port.Dispose();
  }
}