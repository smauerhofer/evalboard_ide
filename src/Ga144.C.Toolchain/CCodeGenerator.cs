using System.Text;

namespace Ga144.C.Toolchain;

/// <summary>
/// REWRITTEN 2026-10-17 against the final, hardware-confirmed <c>CVM_pipeline</c> 79-mnemonic opcode
/// set (see <c>claude/cvm-opcode-audit-2026-10-15.md</c>'s combined-regression section) and the
/// CVM_pipeline C ABI v2 (<c>claude/cvm-abi.md</c> section 3). The previous version of this class
/// targeted a register architecture that no longer exists at all (an address-register file, a separate
/// float-register file, <c>enter</c>/<c>leave</c>/<c>ldl</c>/<c>stl</c>/<c>ldp</c>/<c>stp</c>) -- every
/// mnemonic this class emits below is one of the 79 that opcode audit confirmed; nothing from the old
/// ISA is referenced anywhere in this file.
///
/// <para><b>UPDATED 2026-11-08 to the CURRENT 122-mnemonic <c>CvmInstructionSet</c></b> (see
/// <c>claude/cvm-full-opcode-audit-2026-10-27.md</c>). Every mnemonic still emitted was re-checked against
/// the live <c>Instructions</c> table: none was removed or renamed since 2026-10-17. What changed is that
/// opcodes added since then are now USED where they replace longer sequences:</para>
/// <list type="bullet">
/// <item><description><c>lea rX, offset</c> (Id 243) loads a frame address in ONE instruction -- replaces
/// <c>fp@</c> + <c>rpop</c> + <c>rlit</c>/<c>add</c> (or repeated <c>inc</c>/<c>dec</c>).</description></item>
/// <item><description><c>dinc</c>/<c>ddec</c>/<c>dinc2</c>/<c>ddec2</c> (32-bit, carry into the page word)
/// replace the 4-word <c>dlit</c>+<c>dadd</c> pair for small cross-page pointer steps (|n| &lt;= 6).</description></item>
/// <item><description><c>dsext dN</c> (Id 258) sign-extends the index low word into a 32-bit delta --
/// replaces the <c>if</c>/<c>br</c>/<c>rlit -1|0</c> block in <see cref="EmitAddIndexScaledToPointer"/>.</description></item>
/// <item><description><c>mul2</c>/<c>udiv2</c>/<c>div2</c>/<c>highbyte</c>/<c>lowbyte</c>/<c>byteswap</c>
/// implement shifts by a CONSTANT amount, multiplication by a constant (shift-and-add), unsigned
/// division and modulo by a power of two, with no runtime-library call at all (see
/// <see cref="TryEmitConstantIntOperation"/>). Only a non-constant operand still reaches
/// <c>__mul</c>/<c>__divs</c>/... .</description></item>
/// </list>
///
/// Walks a <see cref="CTranslationUnit"/> exactly once and emits CVM assembly text (the same
/// <c>.casm</c> syntax <see cref="Ga144.Cvm.Toolchain.CvmAssembler"/> accepts) -- there is no separate
/// type-checking pass and no annotated tree: <see cref="EmitExprForValue"/> computes (and checks) each
/// expression's <see cref="CType"/> in the same walk that emits its code, exactly like the previous
/// version did.
///
/// <para><b>Read <c>claude/cvm-abi.md</c> section 3 before touching any codegen method below</b> -- it
/// is the complete design this file implements (memory model, register allocation, frame layout, global
/// addressing, comparisons, float strategy). The short version:</para>
/// <list type="bullet">
/// <item><description>A pointer is 2 words, <c>(page, address)</c> -- <see cref="CType.Pointer"/>'s own
/// <see cref="CType.SizeInWords"/> is 2.</description></item>
/// <item><description>Globals live on page <see cref="GlobalDataPage"/>, reached through
/// <c>dlit</c>+<c>rload</c>/<c>rstore</c>. Locals/parameters live in the current frame (page 1,
/// implicit), reached through <c>ldf</c>/<c>stf rX, offset</c> relative to <c>fp</c>.</description></item>
/// <item><description>Fastcall parameters: up to 4 scalar (<c>int</c>/<c>char</c>) in <c>r0</c>-<c>r3</c>,
/// up to 4 wide (pointer/<c>float</c>) in <c>d4</c>-<c>d7</c> -- both spilled into ordinary frame slots
/// in the prologue. Every other parameter (including every parameter of a non-<c>__fastcall</c>
/// function) is stack-passed, addressed directly at its own positive frame offset, no spill
/// needed.</description></item>
/// <item><description>Return value: 1-word in <c>r0</c>, 2-word (pointer/<c>float</c>) in
/// <c>d0</c>.</description></item>
/// <item><description>Expression scratch: <c>int</c>/<c>char</c> in <c>r8</c>-<c>r15</c> (depth-counter
/// allocated, left operand then right operand then combine in place); pointer/<c>float</c> in
/// <c>d1</c>-<c>d3</c>, same allocation scheme. <c>d0</c> itself is reserved separately -- shared by the
/// return value above and <see cref="EmitAddConstantToRegister"/>'s own transient pointer-arithmetic
/// scratch (CHANGED 2026-10-18, per Stefan directly: "reserve d0 for in-flight pointer arithmetic and
/// return values, not for parameter passing" -- see <c>claude/cvm-abi.md</c> section 3.4's own update).
/// That same method only uses the cheap single-double-register (<c>rlit</c>/<c>add</c>/<c>inc</c>/
/// <c>dec</c>) encoding when the caller can prove the result never leaves the current page; otherwise it
/// uses the full carry-propagating <c>dlit</c>/<c>dadd</c> encoding instead (CHANGED 2026-10-18, same
/// day, same person: "rlit and add can only be used if it is guaranteed that the final address does not
/// cross a page border. if not then dlit and dadd must be used for pointer arithmetic").
/// Every register is call-volatile; a sub-expression containing a call forces the other operand's value
/// to be spilled to a scratch frame slot first (see <see cref="ContainsCall"/>).</description></item>
/// <item><description><c>*</c>/<c>/</c>/<c>%</c> have no hardware opcode (the 16-bit binary ALU row is
/// <c>or</c>/<c>and</c>/<c>xor</c>/<c>sub</c>/<c>add</c>/<c>mov</c>/<c>packbytes</c> only) and neither
/// does a general shift -- all five lower to a call to a hand-written runtime library routine
/// (<c>__mul</c>/<c>__divs</c>/<c>__divu</c>/<c>__mods</c>/<c>__modu</c>/<c>__shl</c>/<c>__shrs</c>/
/// <c>__shru</c>), exactly the same pattern the previous version already used for
/// <c>*</c>/<c>/</c>/<c>%</c> (this is a real, pre-existing ISA gap, not new scope). <b>UPDATED 2026-11-08:
/// the library now exists</b> -- hand-written CVM assembler in
/// <c>libs/cvm/asm/cvmmath.asm</c>, declared in
/// <c>libs/cvm/include/cvm.h</c>, as <c>__fastcall</c> functions (arguments in <c>r0</c>/<c>r1</c>, result
/// in <c>r0</c>, no stack frame). A constant operand
/// avoids the call entirely (see <see cref="TryEmitConstantIntOperation"/>). <see
/// cref="EmitLibraryBinaryCall"/> follows the normal calling convention and preserves the live scalar
/// scratch registers itself; pointer/float registers held across a library call are spilled by their
/// holders (see <see cref="MayCallOrUseLibrary"/>).</description></item>
/// <item><description>A general two-operand comparison lowers to subtract + a <c>cond</c>/<c>if</c>
/// sign-test (<c>&lt;0</c>/<c>&lt;=0</c>/<c>&gt;0</c>/<c>&gt;=0</c>/<c>==0</c>/<c>!=0</c>); an unsigned
/// comparison XORs both operands with <c>0x8000</c> first (the standard bias trick).</description></item>
/// </list>
/// </summary>
public sealed class CCodeGenerator
{
  // ---------------------------------------------------------------------------------------------------
  // ABI v2 shared constants (claude/cvm-abi.md section 3). GlobalDataSectionName/GlobalDataPage are also
  // read directly by Ga144.Evb.Ide's own CvmLinker.Link call sites (CDebuggerViewModel.CompileAndLoadCore,
  // CProjectViewModel's Build path) to build CvmLinkOptions.SectionPages -- this is the ONE place either
  // value is defined; nothing else should hardcode "DATA" or 2 for this purpose.
  public const string GlobalDataSectionName = "DATA";
  public const int GlobalDataPage = 2;

  /// <summary>
  /// ADDED 2026-10-17, per Stefan's own ask for a way to find the heap's own start address (see
  /// <see cref="Ga144.Cvm.Toolchain.CvmLinkOptions.HeapStartSymbolName"/>'s own remarks for the full
  /// design): the already-mangled name <see cref="Ga144.Cvm.Toolchain.CvmLinker.Link"/> synthesizes a
  /// 2-word, pointer-shaped marker symbol under, placed immediately after the DATA section's own last
  /// word. Pre-mangled (leading
  /// underscore, matching <see cref="MangleExternalSymbol"/>'s own convention for every other global)
  /// specifically so plain C source can reach it with the ordinary, unmangled name: <c>extern void
  /// *heap_start;</c> -- the C compiler mangles that reference to "_heap_start" exactly like it would any
  /// other extern global, which is this constant's own value, so the two always agree without either side
  /// having to know about the other's naming convention directly. Read by <c>Ga144.Evb.Ide</c>'s own
  /// <c>CProjectViewModel.Build</c> (alongside <see cref="GlobalDataSectionName"/>/<see cref="GlobalDataPage"/>)
  /// to build every C project's own <see cref="Ga144.Cvm.Toolchain.CvmLinkOptions"/> -- this is the ONE
  /// place this name is defined; nothing else should hardcode "_heap_start" for this purpose.
  /// </summary>
  public const string HeapStartSymbolName = "_heap_start";

  // Register pools (claude/cvm-abi.md section 3.4). Fastcall parameter counts are CParser's own
  // MaxFastcallScalarParameters/MaxFastcallWideParameters (enforced there at parse time); this class
  // reads those constants directly rather than duplicating the numbers, so the two can never drift apart.
  private const int ScalarParamFirstRegister = 0;      // r0..r{MaxFastcallScalarParameters-1}
  private const int WideParamFirstDouble = 4;          // d4..d{3+MaxFastcallWideParameters}
  private const int ScalarReturnRegister = 0;          // r0
  private const int WideReturnDouble = 0;              // d0

  /// <summary>
  /// CHANGED 2026-10-18, per Stefan directly ("i suggest reserving 1 register(d0) for inflight pointer
  /// arithmetic. d0 is also used for return values, but this does not collide with pointer arithmetic.
  /// so change the ABI to use register d0 (r0,r1) as reserved for pointer arithmetic and return values
  /// and not for parameter passing"): <c>d0</c> is carved OUT of the general wide scratch pool below (it
  /// used to be <c>WideScratchFirstDouble</c>, i.e. depth 0 of that pool) and dedicated instead to this
  /// one narrow, always-transient purpose -- the scratch register <see
  /// cref="EmitAddConstantToRegister"/> uses to add/subtract a compile-time constant too large for
  /// repeated <c>inc</c>/<c>dec</c> to a frame/struct-member/array offset. This is safe to share with
  /// <see cref="WideReturnDouble"/> (also <c>d0</c>) for the same reason the old scheme already shared
  /// <c>d0</c> between the return value and the FIRST level of the general wide scratch pool: "return
  /// -value production always happens after scratch use ends for that frame" (claude/cvm-abi.md section
  /// 3.4) -- in-flight pointer arithmetic is itself just one more kind of scratch use, so the same timing
  /// argument covers it. <c>d0</c> was already never used for parameter passing (fastcall wide
  /// parameters have always been <c>d4</c>-<c>d7</c>, see <see cref="WideParamFirstDouble"/>) -- Stefan's
  /// own "not for parameter passing" is this ABI's own existing rule restated, now made explicit for this
  /// new reserved purpose too, not a change to where parameters go.
  /// </summary>
  private const int PointerArithmeticScratchDouble = WideReturnDouble; // d0 (r0/r1)

  private const int ScalarScratchFirstRegister = 8;    // r8..r15
  private const int ScalarScratchMaxDepth = 8;

  /// <summary>
  /// CHANGED 2026-10-18 (see <see cref="PointerArithmeticScratchDouble"/>'s own remarks): was 0
  /// (<c>d0</c>..<c>d3</c>), now 1 -- <c>d0</c> no longer belongs to this general pool at all, so
  /// <see cref="WideReg"/> can never return 0 any more, which is exactly what keeps
  /// <see cref="EmitAddConstantToRegister"/>'s own borrow of <c>d0</c> always collision-free regardless
  /// of expression nesting depth.
  /// </summary>
  private const int WideScratchFirstDouble = 1;        // d1..d3

  /// <summary>CHANGED 2026-10-18: was 4, now 3 -- the pool lost its <c>d0</c> member (now <see cref="PointerArithmeticScratchDouble"/>) without gaining a replacement, so a wide expression's maximum nesting depth is one level shallower than before. See <see cref="PointerArithmeticScratchDouble"/>'s own remarks.</summary>
  private const int WideScratchMaxDepth = 3;

  // ---------------------------------------------------------------------------------------------------
  // Symbol tables.

  /// <summary>Where one parameter or local variable's VALUE lives, relative to the current frame.
  /// <see cref="Offset"/> is meaningful only for <see cref="FrameSlot"/> (signed, relative to <c>fp</c>,
  /// matching <c>ldf</c>/<c>stf</c>'s own operand directly -- for a multi-word variable this is always
  /// the LOW word's offset, the more-negative/smaller one; see claude/cvm-abi.md section 3.5/3.6).
  /// <see cref="Label"/> is meaningful only for <see cref="StaticGlobal"/> (a "static" local compiles as
  /// an ordinary mangled global, exactly like the previous version of this class did).</summary>
  private enum CVarStorage { FrameSlot, StaticGlobal }

  private sealed record CVarSymbol(CVarStorage Storage, CType Type, int Offset = 0, string? Label = null);

  /// <summary>One parameter's ABI v2 passing shape, computed purely from its own type/position and
  /// whether the function is <c>__fastcall</c> -- see <see cref="ComputeParameterSlots"/>. Used
  /// identically by <see cref="EmitFunction"/>'s own prologue (spill register-passed parameters; address
  /// stack-passed ones directly) and by <see cref="EmitCall"/>'s own argument marshaling, so caller and
  /// callee can never disagree about where an argument goes.</summary>
  private sealed record CParamSlot(CType Type, bool IsRegister, bool IsWide, int RegisterIndex, int StackOffset);

  private sealed class CFunctionSignature
  {
    public required string Name { get; init; }
    public required CType ReturnType { get; init; }
    public required IReadOnlyList<CType> ParameterTypes { get; init; }
    public required bool IsFastcall { get; init; }
    public required bool IsDefinedHere { get; init; }
    public required bool IsOptimize { get; init; }
  }

  private sealed class CGlobalSymbol
  {
    public required CType Type { get; init; }
    public required string Label { get; init; }
  }

  /// <summary>Per-function codegen state, live only while <see cref="EmitFunction"/> is walking one
  /// function's body.</summary>
  private sealed class CFunctionContext
  {
    public required string Name { get; init; }
    public required CType ReturnType { get; init; }
    public required bool IsFastcall { get; init; }
    public List<Dictionary<string, CVarSymbol>> Scopes { get; } = [];
    public int NextFrameOffset { get; set; } // next free (most-negative) frame offset to hand out; locals occupy offsets 0, -1, ... -(N-1) (CHANGED 2026-11-08: the first local is at fp+0, not fp-1 -- see the frame layout remarks at ComputeParameterSlots).
    public List<string> BodyLines { get; } = []; // buffered separately from _codeLines -- see EmitFunction.
    public List<(string BreakLabel, string? ContinueLabel)> LoopLabels { get; } = [];
  }

  private readonly Dictionary<string, CFunctionSignature> _functions = new(StringComparer.Ordinal);
  private readonly Dictionary<string, CGlobalSymbol> _globals = new(StringComparer.Ordinal);
  private readonly SortedSet<string> _imports = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> _stringPool = new(StringComparer.Ordinal);
  private int _stringLabelCounter;
  private int _staticLocalCounter;
  private int _labelCounter;
  private readonly List<string> _codeLines = [];
  private readonly List<string> _dataLines = [];
  private readonly List<string> _diagnostics = [];
  private bool _hasError;
  private CFunctionContext? _currentFunction;
  private Dictionary<CStmt, string>? _switchLabelsByNode;

  private readonly bool _enablePeepholeOptimization;
  private readonly string? _sourceFileName;
  private readonly IReadOnlyList<string>? _sourceLines;

  public IReadOnlyDictionary<string, string> SourceCommentLabels => _sourceCommentLabels;
  private readonly Dictionary<string, string> _sourceCommentLabels = new(StringComparer.Ordinal);
  private int _sourceCommentCounter;

  public CCodeGenerator(bool enablePeepholeOptimization = false, string? sourceFileName = null, IReadOnlyList<string>? sourceLines = null)
  {
    _enablePeepholeOptimization = enablePeepholeOptimization;
    _sourceFileName = sourceFileName;
    _sourceLines = sourceLines;
  }

  // ---------------------------------------------------------------------------------------------------
  // Small shared helpers.

  private void Error(CSourceLocation location, string message)
  {
    _diagnostics.Add($"{_sourceFileName ?? "<source>"}:{location.Line}:{location.Column}: {message}");
    _hasError = true;
  }

  private string NewLabel(string hint) => $"__L{_labelCounter++}_{hint}";

  private static string MangleExternalSymbol(string name) => "_" + name;

  private void EmitCode(string line) => _currentFunction!.BodyLines.Add("  " + line);

  private void EmitLabel(string label) => _currentFunction!.BodyLines.Add($"{label}:");

  /// <summary>One source line's worth of generated code gets a local label immediately before it, keyed
  /// by its 0-based occurrence index -- the same per-statement source-comment-label mechanism the
  /// previous version of this class used, unchanged mechanics, so the C Debugger's own address-to-
  /// source-line mapping (<see cref="SourceCommentLabels"/>) keeps working exactly as before.</summary>
  private void EmitSourceLineComment(CSourceLocation location)
  {
    if (_sourceLines is null || location.Line < 1 || location.Line > _sourceLines.Count)
    {
      return;
    }

    string label = $"__srcline_{_sourceCommentCounter++}";
    EmitLabel(label);
    string text = _sourceLines[location.Line - 1].Trim();
    EmitCode($"; {location.Line}: {text}");
    _sourceCommentLabels[label] = text;
  }

  public (string? Assembly, IReadOnlyList<string> Diagnostics) Generate(CTranslationUnit unit)
  {
    CollectSignatures(unit);

    foreach (CGlobalVarDecl global in unit.Globals)
    {
      EmitGlobal(global);
    }

    foreach (CFunctionDecl function in unit.Functions)
    {
      if (function.Body is not null)
      {
        EmitFunction(function);
      }
    }

    EmitStringPool();

    if (_hasError)
    {
      return (null, _diagnostics);
    }

    List<string> finalCodeLines = _enablePeepholeOptimization ? CvmPeepholeOptimizer.Optimize(_codeLines) : _codeLines;

    var output = new StringBuilder();
    foreach (string import in _imports)
    {
      output.AppendLine($".import {import}");
    }

    if (_imports.Count > 0)
    {
      output.AppendLine();
    }

    output.AppendLine(".section CODE");
    foreach (string line in finalCodeLines)
    {
      output.AppendLine(line);
    }

    if (_dataLines.Count > 0)
    {
      output.AppendLine();
      output.AppendLine($".section {GlobalDataSectionName}");
      foreach (string line in _dataLines)
      {
        output.AppendLine(line);
      }
    }

    return (output.ToString(), _diagnostics);
  }

  // ---------------------------------------------------------------------------------------------------
  // Signature collection (two passes over the whole translation unit happen before any code is emitted,
  // so a function may call another one declared later in the same file, exactly like the previous
  // version of this class allowed).

  private void CollectSignatures(CTranslationUnit unit)
  {
    foreach (CGlobalVarDecl global in unit.Globals)
    {
      if (global.Type.IsFunction)
      {
        Error(global.Location, $"\"{global.Name}\" cannot be declared as a function type -- declare it as a function instead");
        continue;
      }

      _globals[global.Name] = new CGlobalSymbol { Type = global.Type, Label = MangleExternalSymbol(global.Name) };
    }

    foreach (CFunctionDecl function in unit.Functions)
    {
      if (_functions.TryGetValue(function.Name, out CFunctionSignature? existing) && existing.IsDefinedHere && function.Body is not null)
      {
        Error(function.Location, $"\"{function.Name}\" is already defined");
        continue;
      }

      if (function.IsOptimize && !IsRecognizedOptimizeSignature(function))
      {
        Error(function.Location, $"\"__optimize\" function \"{function.Name}\" is not one this compiler recognizes as a builtin (recognized: \"float fabsf(float)\", \"float fminf(float, float)\", \"float fmaxf(float, float)\")");
      }

      // FIXED 2026-11-08: a prototype that appears AFTER (or alongside) the definition of the same function --
      // the normal "header with prototypes + .c file with the definitions" layout, e.g. libs/cvm/include/cvm.h
      // included by libs/cvm/src/cvm.c -- must neither overwrite the definition's signature nor make the
      // function an import ("is both a local label and .import"ed").
      bool definedElsewhereInThisUnit = function.Body is null && existing is not null && existing.IsDefinedHere;
      if (definedElsewhereInThisUnit)
      {
        continue;
      }

      _functions[function.Name] = new CFunctionSignature
      {
        Name = function.Name,
        ReturnType = function.ReturnType,
        ParameterTypes = function.Parameters.Select(p => p.Type).ToList(),
        IsFastcall = function.IsFastcall,
        IsDefinedHere = function.Body is not null,
        IsOptimize = function.IsOptimize,
      };

      if (function.Body is null && !function.IsOptimize)
      {
        _imports.Add(function.Name);
      }
    }

    // ...and a prototype seen BEFORE the definition has already queued an import: drop it.
    _imports.RemoveWhere(name => _functions.TryGetValue(name, out CFunctionSignature? signature) && signature.IsDefinedHere);
  }

  private static bool IsRecognizedOptimizeSignature(CFunctionDecl function) =>
      function.Parameters.Count switch
      {
        1 => function.Name == "fabsf" && function.ReturnType.IsFloat && function.Parameters[0].Type.IsFloat,
        2 => (function.Name == "fminf" || function.Name == "fmaxf") && function.ReturnType.IsFloat
            && function.Parameters[0].Type.IsFloat && function.Parameters[1].Type.IsFloat,
        _ => false,
      };

  /// <summary>
  /// The ABI v2 parameter-passing shape for every parameter of a function with these parameter types,
  /// given whether it is <c>__fastcall</c> (claude/cvm-abi.md section 3.4). A plain (non-<c>__fastcall</c>)
  /// function passes every parameter on the stack, in declaration order. A <c>__fastcall</c> function
  /// passes every scalar (<c>int</c>/<c>char</c>, <c>SizeInWords</c> 1) parameter in <c>r0</c>..<c>r3</c>
  /// (declaration order among scalar parameters only) and every wide (pointer/<c>float</c>) parameter in
  /// <c>d4</c>..<c>d7</c> (declaration order among wide parameters only) -- <see cref="CParser"/>'s own
  /// parse-time check already guarantees there are at most <see cref="CParser.MaxFastcallScalarParameters"/>
  /// of the former and <see cref="CParser.MaxFastcallWideParameters"/> of the latter, so this method never
  /// needs to itself reject an overflow into the stack for those two categories. A <c>long</c>/struct
  /// parameter (<c>SizeInWords</c> 2, neither pointer nor <c>float</c>) is never register-eligible under
  /// this ABI at all, so it always falls to the stack, renumbered to skip whichever parameters (of either
  /// kind) went to a register -- exactly the old ABI's own "by register" renumbering rule, just against
  /// the new register pools.
  /// <para><b>Frame layout (CHANGED 2026-11-08, hardware-verified by Stefan's trace of a <c>main</c> with a local
  /// array).</b> The CVM stack pointer is the NEXT FREE slot (a push writes at [sp], then sp decrements). <c>call</c>
  /// pushes the return address, <c>link N</c> pushes the old fp and sets fp := sp (the free slot just below it),
  /// then sp := fp - N. Relative to fp: +3.. stack-passed parameters (the first one at +3), +2 return address,
  /// +1 saved fp, then fp+0 and below the N local words (offsets 0 down to -(N-1)); sp = fp - N is again the next
  /// free slot, so nothing a later push writes can touch a local. Before this change the compiler assumed fp
  /// pointed AT the saved fp (parameters from +2, locals from -1 to -N), which put the last local on the free
  /// slot and read the first parameter from the return address.</para>
  /// </summary>
  private static IReadOnlyList<CParamSlot> ComputeParameterSlots(IReadOnlyList<CType> parameterTypes, bool isFastcall)
  {
    var slots = new CParamSlot[parameterTypes.Count];
    int nextScalarRegister = ScalarParamFirstRegister;
    int nextWideDouble = WideParamFirstDouble;
    int nextStackOffset = 3; // offset +3 = the first stack-passed parameter, closest to fp. CHANGED 2026-11-08 from +2: on the real hardware fp is the FREE slot below the saved fp (saved fp at fp+1, return address at fp+2) -- see claude/cvm-frame-layout-hardware-2026-11-08.md.

    for (int i = 0; i < parameterTypes.Count; i++)
    {
      CType type = parameterTypes[i];
      bool isWideEligible = type.IsWideValue;
      bool isScalarEligible = !isWideEligible && type.SizeInWords <= 1;

      if (isFastcall && isScalarEligible && nextScalarRegister < ScalarParamFirstRegister + CParser.MaxFastcallScalarParameters)
      {
        slots[i] = new CParamSlot(type, IsRegister: true, IsWide: false, RegisterIndex: nextScalarRegister, StackOffset: 0);
        nextScalarRegister++;
      }
      else if (isFastcall && isWideEligible && nextWideDouble < WideParamFirstDouble + CParser.MaxFastcallWideParameters)
      {
        slots[i] = new CParamSlot(type, IsRegister: true, IsWide: true, RegisterIndex: nextWideDouble, StackOffset: 0);
        nextWideDouble++;
      }
      else
      {
        int size = Math.Max(type.SizeInWords, 1);
        slots[i] = new CParamSlot(type, IsRegister: false, IsWide: false, RegisterIndex: 0, StackOffset: nextStackOffset);
        nextStackOffset += size;
      }
    }

    return slots;
  }

  // ---------------------------------------------------------------------------------------------------
  // Globals and static locals. Unified for every type (int/char/float/long/pointer/array/struct) via
  // ComputeGlobalInitialWords -- under ABI v2 every multi-word type's access goes through the same
  // dlit+rload/rstore(+inc) mechanism (claude/cvm-abi.md section 3.6), so, unlike the previous version
  // of this class, float no longer needs its own separate "label"/"label__hi" two-label storage scheme:
  // every multi-word global is simply N contiguous words under ONE label, low word first.

  private void EmitGlobal(CGlobalVarDecl global)
  {
    string label = MangleExternalSymbol(global.Name);

    if (global.Type.IsFunction)
    {
      return; // already diagnosed in CollectSignatures.
    }

    if (global.IsExtern)
    {
      _imports.Add(label);
      return;
    }

    if (!global.IsStatic)
    {
      _dataLines.Add($".export {label}");
    }

    List<string> words = ComputeGlobalInitialWords(global.Type, global.Initializer, global.Location);
    AddGlobalDataLines(label, global.Type, words);
  }

  /// <summary>FIXED 2026-11-08: the load/store code addresses word k of a multi-word global (a float, a long,
  /// a pointer) through the label <c>name__wk</c> (see <see cref="GlobalWordLabel"/>), but only the first word
  /// ever got a label, so any such global failed to assemble ("_x__w1 is undefined"). Every word of a
  /// non-array global now gets its own label; the words stay contiguous, so the first label still addresses
  /// the whole variable.</summary>
  private void AddGlobalDataLines(string label, CType type, List<string> words)
  {
    if (type.IsArray || words.Count < 2)
    {
      _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
      return;
    }

    for (int k = 0; k < words.Count; k++)
    {
      _dataLines.Add($"{GlobalWordLabel(label, k)}: .word {words[k]}");
    }
  }

  /// <summary>Computes a global's (or static local's) initial DATA-section words: exactly
  /// <see cref="CType.SizeInWords"/> of them, low word first for a multi-word type, each either a
  /// decimal literal or a label name the assembler/linker will resolve. No initializer -> zero-filled.
  /// </summary>
  private List<string> ComputeGlobalInitialWords(CType type, CExpr? initializer, CSourceLocation location)
  {
    int size = Math.Max(type.SizeInWords, 1);

    if (initializer is null)
    {
      return Enumerable.Repeat("0", size).ToList();
    }

    if (type.IsFloat)
    {
      if (!TryEvaluateConstantFloat(initializer, out float value))
      {
        Error(location, "a 'float' global/static variable's initializer must be a compile-time constant floating-point literal");
        value = 0f;
      }

      int bits = BitConverter.SingleToInt32Bits(value);
      return [(bits & 0xFFFF).ToString(), ((bits >> 16) & 0xFFFF).ToString()];
    }

    if (type.IsLong)
    {
      // ADDED 2026-11-08: a long / unsigned long global or static initializer is folded to a full 32-bit
      // constant and emitted as two words, LOW word first (the same little-endian layout as float).
      if (!TryEvaluateConstantLong(initializer, out long longValue))
      {
        Error(location, "a 'long' global/static variable's initializer must be a compile-time constant integer expression");
        longValue = 0;
      }

      uint longBits = unchecked((uint)longValue);
      return [(longBits & 0xFFFF).ToString(), (longBits >> 16).ToString()];
    }

    if (type.IsPointer)
    {
      (string low, string high) = ComputeConstantPointerWords(initializer, location);
      return [low, high];
    }

    if (type.IsArray && type.ElementType is { IsIntegral: true } elementType && (elementType.Equals(CType.Char) || elementType.Equals(CType.UnsignedChar))
        && initializer is CStringLiteralExpr stringLiteral)
    {
      byte[] bytes = Encoding.ASCII.GetBytes(stringLiteral.Value);
      var words = bytes.Select(b => ((int)b).ToString()).ToList();
      words.Add("0"); // NUL terminator
      while (words.Count < size)
      {
        words.Add("0");
      }

      if (words.Count > size && type.ArrayLength >= 0)
      {
        Error(location, $"string literal initializer is too long for array of {size} elements");
        words = words.Take(size).ToList();
      }

      return words;
    }

    if (!TryEvaluateConstantGlobalInitializer(initializer, out string? word))
    {
      Error(location, "a global variable's initializer must be a compile-time constant (a literal, a negated literal, an address-of a global, or a string literal)");
      return Enumerable.Repeat("0", size).ToList();
    }

    var result = new List<string> { word! };
    while (result.Count < size)
    {
      result.Add("0");
    }

    return result;
  }

  /// <summary>Folds a pointer global's initializer to its two words (low = address, high = page) --
  /// claude/cvm-abi.md section 3.3/3.6. Only "0"/NULL, "&amp;globalName", a bare array/function name
  /// (which decays to its own address), and a string literal (which interns a fresh <c>char[]</c> and
  /// points at it) are supported, all of which live at the compile-time-known <see cref="GlobalDataPage"/>.
  /// </summary>
  private (string Low, string High) ComputeConstantPointerWords(CExpr initializer, CSourceLocation location)
  {
    switch (initializer)
    {
      case CIntLiteralExpr { Value: 0 }:
        return ("0", "0");

      case CUnaryExpr { Op: CUnaryOp.AddressOf, Operand: CNameExpr name } when _globals.ContainsKey(name.Name):
        return (MangleExternalSymbol(name.Name), GlobalDataPage.ToString());

      case CNameExpr arrayName when _globals.TryGetValue(arrayName.Name, out CGlobalSymbol? symbol) && symbol.Type.IsArray:
        return (MangleExternalSymbol(arrayName.Name), GlobalDataPage.ToString());

      case CStringLiteralExpr stringLiteral:
        return (InternStringLiteral(stringLiteral.Value), GlobalDataPage.ToString());

      default:
        Error(location, "a pointer global/static variable's initializer must be 0 (NULL), \"&amp;globalName\", an array/function name, or a string literal");
        return ("0", "0");
    }
  }

  private static bool TryEvaluateConstantFloat(CExpr expr, out float value)
  {
    switch (expr)
    {
      case CFloatLiteralExpr literal:
        value = literal.Value;
        return true;

      case CIntLiteralExpr intLiteral:
        value = intLiteral.Value;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus } unary when TryEvaluateConstantFloat(unary.Operand, out float v):
        value = -v;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Plus } unary when TryEvaluateConstantFloat(unary.Operand, out float v):
        value = v;
        return true;

      default:
        value = 0f;
        return false;
    }
  }

  private bool TryEvaluateConstantGlobalInitializer(CExpr expr, out string? word)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        word = literal.Value.ToString();
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus, Operand: CIntLiteralExpr inner }:
        word = (-inner.Value).ToString();
        return true;

      case CUnaryExpr { Op: CUnaryOp.BitwiseNot, Operand: CIntLiteralExpr inner }:
        word = (~inner.Value).ToString();
        return true;

      default:
        word = null;
        return false;
    }
  }

  /// <summary>Evaluates a constant integer expression at compile time (array bounds, <c>case</c> labels,
  /// <c>sizeof</c>'s array-length interplay). The binary-operator arithmetic itself is
  /// <see cref="CConstantFolder.TryEvaluateBinaryOp"/>, shared with the AST-level constant-folding
  /// optimizer pass so the two can never disagree about what a compile-time integer operation computes.
  /// </summary>
  private static bool TryEvaluateConstantLong(CExpr expr, out long value)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        value = literal.Value;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus } unary when TryEvaluateConstantLong(unary.Operand, out long v):
        value = -v;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Plus } unary when TryEvaluateConstantLong(unary.Operand, out long v):
        value = v;
        return true;

      case CUnaryExpr { Op: CUnaryOp.BitwiseNot } unary when TryEvaluateConstantLong(unary.Operand, out long v):
        value = ~v;
        return true;

      case CSizeOfTypeExpr sizeOfType:
        value = sizeOfType.Type.SizeInWords;
        return true;

      case CBinaryExpr binary when TryEvaluateConstantLong(binary.Left, out long l) && TryEvaluateConstantLong(binary.Right, out long r):
        return CConstantFolder.TryEvaluateBinaryOp(binary.Op, l, r, out value);

      default:
        value = 0;
        return false;
    }
  }

  private string InternStringLiteral(string value)
  {
    if (_stringPool.TryGetValue(value, out string? existing))
    {
      return existing;
    }

    string label = $"__str{_stringLabelCounter++}";
    _stringPool[value] = label;
    return label;
  }

  private void EmitStringPool()
  {
    foreach ((string value, string label) in _stringPool)
    {
      byte[] bytes = Encoding.ASCII.GetBytes(value);
      var words = bytes.Select(b => ((int)b).ToString()).ToList();
      words.Add("0");
      _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
    }
  }

  // ---------------------------------------------------------------------------------------------------
  // Functions: prologue (link + register-parameter spill), body, epilogue (unlink + ret). Functions are
  // NOT mangled the way globals are (CvmBootStreamBuilder/claude/cvm-abi.md's own "__start"/"__exit"
  // convention, and the previous version of this class, both use a function's own plain C name directly
  // as its label) -- only a GLOBAL VARIABLE gets the leading underscore (MangleExternalSymbol).

  private void EmitFunction(CFunctionDecl function)
  {
    var context = new CFunctionContext { Name = function.Name, ReturnType = function.ReturnType, IsFastcall = function.IsFastcall };
    _currentFunction = context;
    EnterScope();

    if (!function.IsStatic)
    {
      _codeLines.Add($".export {function.Name}");
    }

    EmitLabel(function.Name);
    int linkPlaceholderIndex = context.BodyLines.Count;
    context.BodyLines.Add(string.Empty); // patched below, once the final local word count is known.

    IReadOnlyList<CParamSlot> slots = ComputeParameterSlots(function.Parameters.Select(p => p.Type).ToList(), function.IsFastcall);
    for (int i = 0; i < function.Parameters.Count; i++)
    {
      CParameter parameter = function.Parameters[i];
      CParamSlot slot = slots[i];
      if (slot.IsRegister)
      {
        int size = Math.Max(parameter.Type.SizeInWords, 1);
        int offset = AllocateFrameWords(size);
        DeclareSymbol(parameter.Name, new CVarSymbol(CVarStorage.FrameSlot, parameter.Type, Offset: offset));
        if (slot.IsWide)
        {
          EmitCode($"stf r{2 * slot.RegisterIndex}, {offset}");
          EmitCode($"stf r{2 * slot.RegisterIndex + 1}, {offset + 1}");
        }
        else
        {
          EmitCode($"stf r{slot.RegisterIndex}, {offset}");
        }
      }
      else
      {
        DeclareSymbol(parameter.Name, new CVarSymbol(CVarStorage.FrameSlot, parameter.Type, Offset: slot.StackOffset));
      }
    }

    if (function.Body is not null)
    {
      foreach (CStmt statement in function.Body.Statements)
      {
        EmitStatement(statement);
      }
    }

    EmitCode("unlink");
    EmitCode("ret");

    int localWordCount = -context.NextFrameOffset;
    context.BodyLines[linkPlaceholderIndex] = $"  link {-localWordCount}";

    ExitScope();
    _codeLines.AddRange(context.BodyLines);
    _currentFunction = null;
  }

  private int AllocateFrameWords(int count)
  {
    CFunctionContext context = _currentFunction!;
    int low = context.NextFrameOffset - (count - 1);
    context.NextFrameOffset -= count;
    return low;
  }

  private void EnterScope() => _currentFunction!.Scopes.Add(new Dictionary<string, CVarSymbol>(StringComparer.Ordinal));

  private void ExitScope() => _currentFunction!.Scopes.RemoveAt(_currentFunction.Scopes.Count - 1);

  private void DeclareSymbol(string name, CVarSymbol symbol) => _currentFunction!.Scopes[^1][name] = symbol;

  private CVarSymbol? LookupVariable(string name)
  {
    for (int i = _currentFunction!.Scopes.Count - 1; i >= 0; i--)
    {
      if (_currentFunction.Scopes[i].TryGetValue(name, out CVarSymbol? symbol))
      {
        return symbol;
      }
    }

    return null;
  }

  /// <summary>A local variable declaration (<see cref="CLocalVarDecl"/>), reached as a statement. A
  /// "static" local compiles as an ordinary, uniquely-mangled global (same storage/lifetime as a real
  /// global, visible only inside this function) -- exactly the previous version's own approach, unchanged.
  /// </summary>
  private void DeclareLocal(CLocalVarDecl decl)
  {
    if (decl.IsStatic)
    {
      string label = $"_{_currentFunction!.Name}__static{_staticLocalCounter++}_{decl.Name}";
      List<string> words = ComputeGlobalInitialWords(decl.Type, decl.Initializer, decl.Location);
      AddGlobalDataLines(label, decl.Type, words);
      DeclareSymbol(decl.Name, new CVarSymbol(CVarStorage.StaticGlobal, decl.Type, Label: label));
      return;
    }

    int size = Math.Max(decl.Type.SizeInWords, 1);
    int offset = AllocateFrameWords(size);
    DeclareSymbol(decl.Name, new CVarSymbol(CVarStorage.FrameSlot, decl.Type, Offset: offset));

    if (decl.Initializer is not null)
    {
      if (IsUnsupportedWholeValueType(decl.Type))
      {
        ReportUnsupportedValueRead(decl.Location, decl.Type, $"local \"{decl.Name}\"");
        return;
      }

      EmitAssignToAddr(new CAddr(CAddrKind.Frame, decl.Type, offset), decl.Initializer, decl.Location, 0);
    }
  }

  // ---------------------------------------------------------------------------------------------------
  // Addressing (claude/cvm-abi.md section 3.3/3.6). Deliberately simplified relative to the previous
  // version of this class: a BARE name (local/parameter/static-local/global) resolves directly, with no
  // runtime pointer ever built, exactly like before. ANYTHING ELSE addressable -- a dereference, an
  // index, a member access via "." or "->" -- always resolves by computing a full runtime (page,
  // address) pointer into a wide scratch register first (<see cref="EmitAddressOfInto"/>), then reusing
  // the exact same word-at-a-time load/store machinery through that pointer. This costs a few extra
  // instructions for, e.g., a local struct's own member access (which could in principle be addressed
  // directly at a compile-time-constant frame offset, the way the previous version did) in exchange for
  // a single, uniform, much less error-prone addressing path -- a deliberate simplicity-over-optimality
  // scope choice for this rewrite, not a hardware limitation. Revisit if profiling ever shows this
  // actually matters.
  //
  // GlobalAddressTempDouble (d8 = r16/r17) is a dedicated scratch double register for building a Global
  // address's pointer -- never used for anything else (not a parameter/return/scratch-pool register), so
  // always safe to clobber. It is never used with `inc`/`add` (every Global word has its OWN label --
  // see <see cref="GlobalWordLabel"/> -- so no runtime increment is ever needed for the Global case),
  // which is why it's safe for it to sit outside the r0-r15 range every ordinary 16-bit op is restricted
  // to: `dlit`/`rload`/`rstore`'s own register-holding-the-pointer operand is a full 4-bit d0-d15 field,
  // unrelated to that restriction.
  //
  // <b>The shared nesting-depth parameter ("depth") every evaluator below takes.</b> Expression
  // evaluation needs two independent scratch pools (int r8-r15, wide d1-d3 -- claude/cvm-abi.md section
  // 3.4) PLUS the occasional transient borrow of the OTHER pool (e.g. an array index's own int value,
  // computed while building a pointer). Rather than thread two separate counters (and get their
  // interaction wrong), every evaluator takes ONE shared "depth" int: the scalar register used is
  // always r(8+depth), the wide register used is always d(1+depth) -- the SAME depth number, just
  // mapped into whichever pool that particular call needs (CHANGED 2026-10-18: the wide pool's own base
  // moved from d0 to d1 when d0 was carved out for PointerArithmeticScratchDouble -- see that constant's
  // own remarks). Recursing into a sub-expression ALWAYS passes depth+1 for an operand that must stay
  // independently alive (a binary op's own left/right operands, or a compound lvalue's transient pointer
  // one level "underneath" the value being loaded through it); a one-off borrow of the OTHER pool for
  // something that does not overlap the current value's own lifetime (an index's own int value while the
  // pointer it scales is being built) reuses the SAME depth. Since depth strictly increases going down
  // and the two pools are only ever indexed by the CURRENT depth, two live values can never collide. The
  // practical effect: a single expression can nest at most 3 levels deep before running out of wide
  // registers (the smaller of the two pools, now one level tighter than before d0's reservation) even in
  // a pure-int sub-expression that never touches a pointer -- tighter than the int pool's own 8-deep
  // limit, and worth knowing if a genuinely deep expression ever hits "expression nested too deeply".
  // d0 itself is never part of this depth-indexed scheme at all any more -- see
  // PointerArithmeticScratchDouble's own remarks for its separate, fixed, always-collision-free role.

  private enum CAddrKind { Frame, Global, Pointer }

  private sealed record CAddr(CAddrKind Kind, CType Type, int Offset = 0, string? Label = null, int PointerDouble = -1);

  private const int GlobalAddressTempDouble = 8;

  private static string GlobalWordLabel(string label, int wordIndex) => wordIndex == 0 ? label : $"{label}__w{wordIndex}";

  private int ScalarReg(int depth)
  {
    if (depth >= ScalarScratchMaxDepth)
    {
      _diagnostics.Add("<internal>: expression nested too deeply for this compiler's int scratch pool (max depth 8).");
      _hasError = true;
      depth = ScalarScratchMaxDepth - 1;
    }

    return ScalarScratchFirstRegister + depth;
  }

  private int WideReg(int depth)
  {
    if (depth >= WideScratchMaxDepth)
    {
      _diagnostics.Add("<internal>: expression nested too deeply for this compiler's pointer/float scratch pool (max depth 4).");
      _hasError = true;
      depth = WideScratchMaxDepth - 1;
    }

    return WideScratchFirstDouble + depth;
  }

  /// <summary>Resolves <paramref name="expr"/> to an addressable <see cref="CAddr"/>, borrowing wide
  /// register <c>WideReg(depth)</c> if a runtime pointer needs to be materialized (every non-bare-name
  /// case) -- or reports an error and returns a dummy Frame address at offset 0 if it is not an lvalue
  /// at all.</summary>
  private CAddr ResolveAddr(CExpr expr, int depth)
  {
    if (expr is CNameExpr name)
    {
      CVarSymbol? symbol = LookupVariable(name.Name);
      if (symbol is not null)
      {
        return symbol.Storage == CVarStorage.StaticGlobal
            ? new CAddr(CAddrKind.Global, symbol.Type, Label: symbol.Label)
            : new CAddr(CAddrKind.Frame, symbol.Type, Offset: symbol.Offset);
      }

      if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
      {
        return new CAddr(CAddrKind.Global, global.Type, Label: global.Label);
      }

      Error(expr.Location, $"\"{name.Name}\" is not declared");
      return new CAddr(CAddrKind.Frame, CType.Int, Offset: 0);
    }

    int pointerDouble = WideReg(depth);
    CType pointeeType = EmitAddressOfInto(expr, pointerDouble, depth);
    return new CAddr(CAddrKind.Pointer, pointeeType, PointerDouble: pointerDouble);
  }

  /// <summary>
  /// True when <c>&amp;</c><paramref name="expr"/> is guaranteed, at compile time, to land in a single
  /// bounded page -- a local's own frame slot (page 1, bounded by this function's own declared local
  /// size) or a global's own fixed location (page <see cref="GlobalDataPage"/>, bounded by the whole
  /// program's own declared global data size) -- as opposed to an address reached through an arbitrary
  /// RUNTIME pointer VALUE (a pointer parameter/local, a dereferenced pointer, the heap, a cast, a
  /// function call's return value, ...), which cannot be bounded this way and must be assumed to be
  /// anywhere on its own page. ADDED 2026-10-18, per Stefan directly ("the variable-index case has 2
  /// options. the variable is global (page 2) then 16-bit arithmetic can be used. but if the variable
  /// is on the heap or a pointer then the safe 32-bit arithmetic must be used") -- this is the SAME
  /// "guaranteedSamePage" boundary <see cref="EmitAddConstantToRegister"/> and <see
  /// cref="EmitAddIndexScaledToPointer"/> both need, extended (this round) past "is it a local frame
  /// address" to also cover "is it a global" (Stefan's own clarification: a global is JUST as bounded as
  /// a local, for this purpose -- both sit at a single, fixed, compile-time-known page -- only an actual
  /// pointer VALUE, wherever it points, is not), computed once here and reused by every call site that
  /// needs to decide it.
  ///
  /// "." inherits its base's own boundedness (a member of a bounded struct is itself bounded); "->"
  /// dereferences a pointer VALUE first, which breaks it immediately regardless of anything past that
  /// point. Indexing inherits its own base's boundedness the same way, but ONLY when that base is
  /// itself an array (<c>arr[i]</c> on a bounded array is bounded; <c>ptr[i]</c> on a pointer VALUE --
  /// even one currently held in a bounded local/global VARIABLE -- is not, since it is the VALUE stored
  /// in that variable, not the variable's own address, that ends up being indexed).
  /// </summary>
  private bool IsBoundedAddress(CExpr expr) => expr switch
  {
    CNameExpr => true,
    CMemberAccessExpr member => !member.IsArrow && IsBoundedAddress(member.Base),
    CIndexExpr index => IsBoundedPointerValue(index.Base),
    _ => false,
  };

  /// <summary>True when evaluating <paramref name="expr"/> AS A POINTER VALUE (not necessarily as an
  /// lvalue -- this is what <see cref="EmitPointerDecayInto"/>/<see cref="EmitWideExprInto"/>'s own
  /// array-decay branches actually produce) is guaranteed to land in a single bounded page: exactly when
  /// <paramref name="expr"/> is itself a compile-time-bounded ARRAY (see <see cref="IsBoundedAddress"/>),
  /// which decays to its own bounded address. Anything pointer-TYPED instead (a pointer parameter/local,
  /// a dereferenced pointer, the heap, a cast, a function call's return value, ...) evaluates to a
  /// general runtime VALUE this method cannot bound, even if that pointer happens to be held in a
  /// perfectly bounded local/global VARIABLE -- it is the VALUE, not the variable's own address, that
  /// ends up being indexed/offset. Used wherever a pointer-typed OPERAND (not an lvalue being addressed)
  /// needs this same "guaranteedSamePage" classification -- see <see
  /// cref="EmitWidePointerOrFloatAddSub"/>'s own two call sites.</summary>
  private bool IsBoundedPointerValue(CExpr expr) =>
      TryGetStaticType(expr, out CType type) && type.IsArray && IsBoundedAddress(expr);

  /// <summary>Computes <paramref name="expr"/>'s ADDRESS (not its value) into double register
  /// <paramref name="destDouble"/> -- the general machinery behind both the <c>&amp;</c> operator and
  /// every compound lvalue's own addressing (see this class's own "Addressing" remarks). <paramref
  /// name="depth"/> is <c>destDouble</c>'s own nesting depth (<c>destDouble == WideReg(depth)</c>),
  /// passed through so an internal sub-computation (an index's int value, a nested "." base's own
  /// address) can safely reuse it. Returns the pointee's type.</summary>
  private CType EmitAddressOfInto(CExpr expr, int destDouble, int depth)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is not null)
          {
            if (symbol.Storage == CVarStorage.StaticGlobal)
            {
              EmitCode($"dlit d{destDouble}, {GlobalDataPage}, {symbol.Label}");
              return symbol.Type;
            }

            // Frame address = fp + offset, page 1 (implicit). The result never leaves page 1: it is the
            // VM's OWN frame pointer plus this function's own local-frame offset (bounded by its total
            // declared local size, a compile-time-known small quantity); the stack itself would have to
            // overflow a whole 64K-word page before this could ever leave page 1, a pre-existing,
            // out-of-scope failure mode distinct from general C pointer arithmetic on an arbitrary
            // runtime pointer value (see the other three call sites below).
            // CHANGED 2026-11-08: "lea rX, offset" (CvmInstructionSet Id 243, node 408: rX = (fp + signed
            // offset) & 0xffff) does the whole job in one instruction, replacing the previous fp@ + rpop +
            // rlit/add (or inc/dec) offset adjustment.
            EmitCode($"lea r{2 * destDouble}, {symbol.Offset}");
            EmitCode($"rlit r{2 * destDouble + 1}, 1");
            return symbol.Type;
          }

          if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            EmitCode($"dlit d{destDouble}, {GlobalDataPage}, {global.Label}");
            return global.Type;
          }

          Error(expr.Location, $"\"{name.Name}\" is not declared");
          EmitCode($"dlit d{destDouble}, 0, 0");
          return CType.Int;
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        {
          CType pointerType = EmitWideExprInto(deref.Operand, destDouble, depth);
          if (!pointerType.IsPointer)
          {
            Error(expr.Location, $"cannot dereference a value of type \"{pointerType}\"");
            return CType.Int;
          }

          return pointerType.ElementType!;
        }

      case CIndexExpr index:
        {
          CType baseType = EmitPointerDecayInto(index.Base, destDouble, depth);
          if (!baseType.IsPointer)
          {
            Error(expr.Location, $"cannot index a value of type \"{baseType}\"");
            return CType.Int;
          }

          CType elementType = baseType.ElementType!;

          // guaranteedSamePage: IsBoundedAddress(index) -- true exactly when index.Base is itself a
          // bounded array (local OR global; see IsBoundedAddress's own remarks), in which case destDouble
          // decayed to that array's own compile-time-bounded address; false when index.Base is anything
          // pointer-typed (a pointer parameter/local, a dereferenced pointer, a heap pointer, ...), in
          // which case destDouble holds that pointer's own RUNTIME value -- could be anywhere on its page.
          EmitAddIndexScaledToPointer(destDouble, index.Index, Math.Max(elementType.SizeInWords, 1), depth, guaranteedSamePage: IsBoundedAddress(index));
          return elementType;
        }

      case CMemberAccessExpr member:
        {
          CType baseStructType;
          if (member.IsArrow)
          {
            baseStructType = EmitWideExprInto(member.Base, destDouble, depth);
            if (!baseStructType.IsPointer)
            {
              Error(expr.Location, $"\"->\" needs a pointer on its left, got \"{baseStructType}\"");
              return CType.Int;
            }

            baseStructType = baseStructType.ElementType!;
          }
          else
          {
            baseStructType = EmitAddressOfInto(member.Base, destDouble, depth);
          }

          if (!baseStructType.IsStruct)
          {
            Error(expr.Location, $"\"{member.Member}\" is not a member of \"{baseStructType}\" (not a struct)");
            return CType.Int;
          }

          CStructMember? field = baseStructType.Members?.FirstOrDefault(m => m.Name == member.Member);
          if (field is null)
          {
            Error(expr.Location, $"\"struct {baseStructType.Tag}\" has no member \"{member.Member}\"");
            return CType.Int;
          }

          if (field.Offset != 0)
          {
            // guaranteedSamePage: IsBoundedAddress(member) -- true via plain "." on a local/global
            // struct lvalue (destDouble holds a compile-time-bounded frame/global address, exactly like
            // CNameExpr above); false via "->" (destDouble holds an arbitrary RUNTIME pointer value --
            // could be anywhere on its own page). destDouble already holds a COMPLETE (page, address)
            // pair by this point in every case above, so dadd is immediately safe in the false case.
            EmitAddConstantToRegister(2 * destDouble, field.Offset, guaranteedSamePage: IsBoundedAddress(member));
          }

          return field.Type;
        }

      default:
        Error(expr.Location, "this expression is not addressable (it is not an lvalue)");
        EmitCode($"dlit d{destDouble}, 0, 0");
        return CType.Int;
    }
  }

  /// <summary>An array decays to its own address; anything else is evaluated for its (pointer) VALUE.
  /// Shared by index/pointer-arithmetic codegen, exactly mirroring ordinary C decay rules.</summary>
  private CType EmitPointerDecayInto(CExpr expr, int destDouble, int depth)
  {
    if (TryGetStaticType(expr, out CType type) && type.IsArray)
    {
      // BUG FIXED 2026-10-18 (confirmed by Stefan's own disassembly trace of "d[0] = a;" for a local
      // `int d[7]`, which showed a two-word store through a dummy wideSrc=0 instead of the real value
      // register): EmitAddressOfInto(expr, ...), for a plain array name/member/index, returns the
      // ARRAY's own declared type (e.g. "int[7]"), not its element type -- every one of its branches
      // (CNameExpr -> symbol.Type, CMemberAccessExpr -> field.Type, ...) returns the addressed entity's
      // OWN full type, array-ness included, by design (that's the right answer for every OTHER caller).
      // This decay is the one place that needs the ELEMENT type, so it must unwrap ElementType itself --
      // wrapping the array type directly in CType.PointerTo (as this used to do) produced a bogus
      // "pointer to int[7]" whose own ElementType (int[7], SizeInWords 7) then propagated into
      // EmitAddressOfInto's CIndexExpr case as the indexed element's type, corrupting every downstream
      // SizeInWords-based decision (EmitStoreValueToAddr/EmitLoadValueFromAddr only ever expect size 1
      // or 2, so a stray 7 silently fell through to the 2-word branch using whatever dummy register the
      // caller had passed as "unused").
      CType arrayType = EmitAddressOfInto(expr, destDouble, depth);
      return CType.PointerTo(arrayType.ElementType!);
    }

    return EmitWideExprInto(expr, destDouble, depth);
  }

  /// <summary>Adds <paramref name="indexExpr"/>'s value, scaled by <paramref name="elementWords"/>,
  /// to the pointer already sitting in double register <paramref name="pointerDouble"/>. <paramref
  /// name="guaranteedSamePage"/> is the caller's own page-crossing claim (see <see
  /// cref="IsBoundedAddress"/>'s own remarks) -- true when indexing into a compile-time-bounded array
  /// (local OR global), false when indexing through a general runtime pointer VALUE (parameter, heap,
  /// dereferenced pointer, ...).
  ///
  /// CHANGED 2026-10-18, per Stefan directly, two rounds: first "rlit and add can only be used if it is
  /// guaranteed that the final address does not cross a page border. if not then dlit and dadd must be
  /// used for pointer arithmetic", then the refinement that actually decides <paramref
  /// name="guaranteedSamePage"/> here: "the variable-index case has 2 options. the variable is global
  /// (page 2) then 16-bit arithmetic can be used. but if the variable is on the heap or a pointer then
  /// the safe 32-bit arithmetic must be used."
  ///
  /// - The COMPILE-TIME-CONSTANT index case goes through <see cref="EmitAddConstantToRegister"/>
  ///   directly, carrying <paramref name="guaranteedSamePage"/> through unchanged (<paramref
  ///   name="pointerDouble"/> is completely populated -- both halves -- by every caller of this method
  ///   before it runs, via <see cref="EmitPointerDecayInto"/>/<see cref="EmitAddressOfInto"/>, so that
  ///   method's own safe path is immediately usable here whenever it's needed).
  /// - The RUNTIME-VARIABLE index case (this method's own remaining logic) has no constant to hand
  ///   `dlit` -- the scaled delta is already sitting in a plain register (<c>indexRegister</c>) by the
  ///   time it needs adding. When guaranteed, the ORIGINAL plain 16-bit `add` is unchanged (cheapest,
  ///   and provably correct here). When NOT guaranteed, `indexRegister`'s value (a signed 16-bit delta)
  ///   is copied into <see cref="PointerArithmeticScratchDouble"/>'s own low alias, its HIGH alias is set
  ///   to the correct sign-extension (`0` or `-1` -- since 2026-11-08 by the single `dsext` opcode, before
  ///   that by an `if`/`br`/`rlit` block) so the resulting double register is
  ///   a properly sign-extended 32-bit delta, and `dadd` adds that into <paramref name="pointerDouble"/>
  ///   -- mirroring <see cref="EmitAddConstantToRegister"/>'s own `dlit`+`dadd` encoding, just with the
  ///   delta's low half coming from a register instead of a compile-time literal.
  ///   <b>Self-aliasing exception, flagged rather than fully solved</b>: if <paramref
  ///   name="pointerDouble"/> IS <see cref="PointerArithmeticScratchDouble"/> itself (the same rare
  ///   `return ptr[i];`/`return &amp;arr[i];`-into-the-return-register case <see
  ///   cref="EmitAddConstantToRegister"/> also has to guard against), there is no OTHER
  ///   individually-16-bit-addressable double register to borrow as scratch for the sign-extension step
  ///   (unlike that method's constant case, this one needs a plain `mov` into the scratch's low alias,
  ///   which only reaches <c>d0</c>-<c>d7</c> -- <see cref="GlobalAddressTempDouble"/>, `d8`, is not a
  ///   substitute here). This one combination falls back to the plain 16-bit `add` instead -- correct
  ///   unless THIS particular access also happens to cross a page, a narrower residual gap than before
  ///   this change, not a new one, and flagged rather than silently accepted.
  ///
  /// The index's own int value is computed into <c>ScalarReg(depth)</c> -- safe to reuse the SAME depth
  /// as the pointer itself, since the index value's lifetime ends (consumed into the pointer) before
  /// anything else at this depth runs.</summary>
  private void EmitAddIndexScaledToPointer(int pointerDouble, CExpr indexExpr, int elementWords, int depth, bool guaranteedSamePage)
  {
    if (TryEvaluateConstantLong(indexExpr, out long constantIndex))
    {
      EmitAddConstantToRegister(2 * pointerDouble, (int)(constantIndex * elementWords), guaranteedSamePage);
      return;
    }

    // ADDED 2026-11-08: the pointer in pointerDouble is live while the index is computed and scaled. A
    // call or runtime-library call in either step clobbers every wide register, so spill the pointer to a
    // frame slot first and reload it just before the add (previously a call in the index silently
    // destroyed the pointer).
    bool scaleUsesLibrary = elementWords != 1 && !MultiplyIsInline(elementWords);
    int? pointerSpill = MayCallOrUseLibrary(indexExpr) || scaleUsesLibrary ? AllocateFrameWords(2) : null;
    if (pointerSpill is not null)
    {
      EmitCode($"stf r{2 * pointerDouble}, {pointerSpill}");
      EmitCode($"stf r{2 * pointerDouble + 1}, {pointerSpill + 1}");
    }

    int indexRegister = ScalarReg(depth);
    EmitIntExprInto(indexExpr, indexRegister, depth);
    if (elementWords != 1)
    {
      // CHANGED 2026-11-08: inline shift-and-add (mul2/add/neg) for every constant that is cheap enough,
      // the runtime-library call only as the fallback -- see EmitMultiplyRegisterByConstant.
      EmitMultiplyRegisterByConstant(indexRegister, elementWords, indexRegister + 1);
    }

    if (pointerSpill is not null)
    {
      EmitCode($"ldf r{2 * pointerDouble}, {pointerSpill}");
      EmitCode($"ldf r{2 * pointerDouble + 1}, {pointerSpill + 1}");
    }

    if (guaranteedSamePage || pointerDouble == PointerArithmeticScratchDouble)
    {
      EmitCode($"add r{2 * pointerDouble}, r{indexRegister}");
      return;
    }

    // CHANGED 2026-11-08: "dsext dN" (CvmInstructionSet Id 258, node 208) sign-extends the low 16 bits of
    // a double register into the full 32 bits, so the previous if/br/rlit -1|0 block (and its two labels)
    // collapses to mov + dsext + dadd.
    EmitCode($"mov r{2 * PointerArithmeticScratchDouble}, r{indexRegister}");
    EmitCode($"dsext d{PointerArithmeticScratchDouble}");
    EmitCode($"dadd d{pointerDouble}, d{PointerArithmeticScratchDouble}");
  }

  /// <summary>Loads <paramref name="addr"/>'s whole value: 1 word into plain register <paramref
  /// name="scalarDest"/> when <see cref="CAddr.Type"/> is 1 word, or 2 words into double register
  /// <paramref name="wideDest"/> (low into its low r-alias, high into its high r-alias) when it is 2
  /// words. Exactly one of the two destinations is used, per the type's own <see
  /// cref="CType.SizeInWords"/>. <paramref name="depth"/> is only consulted in the rare self-aliasing
  /// case below; pass the caller's own current depth (or any value one call deeper, e.g. <c>baseDepth</c>
  /// in a caller that already bumped past a fresh-register depth) -- it is never consumed when that case
  /// does not apply.</summary>
  private void EmitLoadValueFromAddr(CAddr addr, int scalarDest, int wideDest, int depth)
  {
    int size = Math.Max(addr.Type.SizeInWords, 1);
    if (size == 1)
    {
      EmitLoadWordFromAddr(addr, 0, scalarDest);
      return;
    }

    // Guard added 2026-10-18, alongside the EmitPointerDecayInto/EmitWideExprInto array-decay fix that
    // bug exposed: this method only ever expects 1 word (above) or exactly 2 (below) -- never anything
    // else, since every addr.Type reaching here should be a scalar, a pointer, or a float. A corrupted
    // type (e.g. an array type wrongly surviving pointer decay, as Stefan's own disassembly trace of
    // "d[0] = a;" caught) would otherwise silently fall through to the 2-word path below and load through
    // whatever "unused" placeholder register the caller happened to pass for wideDest, exactly like that
    // bug did on the STORE side -- flagged loudly here instead of repeating that silently.
    if (size != 2)
    {
      _diagnostics.Add($"<internal>: EmitLoadValueFromAddr on \"{addr.Type}\" (SizeInWords={addr.Type.SizeInWords}) -- only 1-word and 2-word (pointer/float) types are expected here.");
      _hasError = true;
    }

    if (addr.Kind == CAddrKind.Pointer && addr.PointerDouble == wideDest)
    {
      // The destination IS the register holding the pointer (this happens routinely -- e.g. evaluating
      // "*pp" where pp is a pointer-to-wide-type at the top of an expression, where both the pointer
      // and its eventual value naturally land at the same depth-indexed register). Fetching word 0
      // directly into wideDest would overwrite the pointer's own low r-alias before word 1's fetch gets
      // a chance to `inc` it -- so route both words through a FRESH depth-indexed scratch double (never
      // GlobalAddressTempDouble/d8 here: its r-aliases, r16/r17, are not reachable by `mov`'s 4-bit
      // register field -- confirmed by Stefan 2026-10-18, "r16 and r17 are not accessible with opcodes
      // using rx") and move into place only once both words are safely captured. WideReg(depth + 1) is
      // always a DIFFERENT double register from wideDest itself: every call site that can actually reach
      // this branch passes a depth one shallower than the one used to produce wideDest (see this
      // method's callers), so WideReg's injectivity in depth guarantees no self-collision; WideReg's own
      // overflow guard also makes depth + 1 safe to call even in a pathological over-nested case.
      int scratchDouble = WideReg(depth + 1);
      EmitLoadWordFromAddr(addr, 0, 2 * scratchDouble);
      EmitLoadWordFromAddr(addr, 1, 2 * scratchDouble + 1);
      EmitCode($"mov r{2 * wideDest}, r{2 * scratchDouble}");
      EmitCode($"mov r{2 * wideDest + 1}, r{2 * scratchDouble + 1}");
      return;
    }

    EmitLoadWordFromAddr(addr, 0, 2 * wideDest);
    EmitLoadWordFromAddr(addr, 1, 2 * wideDest + 1);
  }

  private void EmitStoreValueToAddr(CAddr addr, int scalarSrc, int wideSrc)
  {
    int size = Math.Max(addr.Type.SizeInWords, 1);
    if (size == 1)
    {
      EmitStoreWordToAddr(addr, 0, scalarSrc);
      return;
    }

    // Guard added 2026-10-18 -- see EmitLoadValueFromAddr's own matching entry just above for why: this
    // is precisely the fallback that silently stored through the caller's "unused" wideSrc=0 placeholder
    // when a corrupted array-as-pointee type reached here with SizeInWords=7 (confirmed by Stefan's own
    // disassembly trace of "d[0] = a;"). Flagged loudly now instead of repeating that silently.
    if (size != 2)
    {
      _diagnostics.Add($"<internal>: EmitStoreValueToAddr on \"{addr.Type}\" (SizeInWords={addr.Type.SizeInWords}) -- only 1-word and 2-word (pointer/float) types are expected here.");
      _hasError = true;
    }

    EmitStoreWordToAddr(addr, 0, 2 * wideSrc);
    EmitStoreWordToAddr(addr, 1, 2 * wideSrc + 1);
  }

  // ---------------------------------------------------------------------------------------------------
  // Static type resolution -- pure type-system logic, independent of the register architecture, so
  // reused essentially unchanged from the previous version of this class.

  private bool TryGetStaticType(CExpr expr, out CType type)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        type = literal.IsLong
            ? (literal.IsUnsigned ? CType.UnsignedLong : CType.Long)
            : (literal.IsUnsigned ? CType.UnsignedInt : CType.Int);
        return true;

      case CFloatLiteralExpr:
        type = CType.Float;
        return true;

      case CStringLiteralExpr:
        type = CType.PointerTo(CType.Char);
        return true;

      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is not null)
          {
            type = symbol.Type;
            return true;
          }

          if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            type = global.Type;
            return true;
          }

          type = CType.Int;
          return false;
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        {
          if (TryGetStaticType(deref.Operand, out CType pointerType) && pointerType.IsPointer)
          {
            type = pointerType.ElementType!;
            return true;
          }

          type = CType.Int;
          return false;
        }

      case CUnaryExpr { Op: CUnaryOp.AddressOf } addressOf:
        {
          if (TryGetStaticType(addressOf.Operand, out CType pointeeType))
          {
            type = CType.PointerTo(pointeeType);
            return true;
          }

          type = CType.PointerTo(CType.Int);
          return false;
        }

      case CIndexExpr index:
        {
          if (TryGetStaticType(index.Base, out CType baseType))
          {
            CType decayed = baseType.Decay();
            if (decayed.IsPointer)
            {
              type = decayed.ElementType!;
              return true;
            }
          }

          type = CType.Int;
          return false;
        }

      case CMemberAccessExpr member:
        {
          if (!TryGetStaticType(member.Base, out CType baseType))
          {
            type = CType.Int;
            return false;
          }

          CType? structType = member.IsArrow
              ? (baseType.IsPointer && baseType.ElementType!.IsStruct ? baseType.ElementType : null)
              : (baseType.IsStruct ? baseType : null);

          CStructMember? field = structType?.Members?.FirstOrDefault(m => m.Name == member.Member);
          if (field is null)
          {
            type = CType.Int;
            return false;
          }

          type = field.Type;
          return true;
        }

      case CCastExpr cast:
        type = cast.TargetType;
        return true;

      case CCallExpr call when _functions.TryGetValue(call.FunctionName, out CFunctionSignature? signature):
        type = signature.ReturnType;
        return true;

      case CConditionalExpr conditional:
        {
          bool haveTrue = TryGetStaticType(conditional.WhenTrue, out CType trueStaticType);
          bool haveFalse = TryGetStaticType(conditional.WhenFalse, out CType falseStaticType);
          type = trueStaticType;
          if (haveTrue && haveFalse && (trueStaticType.IsLong || falseStaticType.IsLong) && trueStaticType.IsIntegral && falseStaticType.IsIntegral)
          {
            type = PromoteArithmeticType(trueStaticType, falseStaticType);
          }
          else if (haveTrue && haveFalse && (trueStaticType.IsFloat || falseStaticType.IsFloat)
              && (trueStaticType.IsFloat || trueStaticType.IsIntegral) && (falseStaticType.IsFloat || falseStaticType.IsIntegral))
          {
            type = CType.Float; // ADDED 2026-11-08: "c ? 1 : 2.5" is a float, the integer branch is converted.
          }

          return haveTrue;
        }

      case CAssignExpr assign:
        return TryGetStaticType(assign.Target, out type);

      case CCompoundAssignExpr compoundAssign:
        return TryGetStaticType(compoundAssign.Target, out type);

      case CCommaExpr comma:
        return TryGetStaticType(comma.Right, out type);

      case CUnaryExpr { Op: CUnaryOp.Plus or CUnaryOp.Minus or CUnaryOp.BitwiseNot } unary:
        {
          bool haveOperand = TryGetStaticType(unary.Operand, out CType operandType);
          type = haveOperand && (operandType.IsLong || operandType.IsFloat) ? operandType : CType.Int;
          return haveOperand;
        }

      case CUnaryExpr { Op: CUnaryOp.LogicalNot }:
        type = CType.Int;
        return true;

      case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostIncrement or CUnaryOp.PostDecrement } incDec:
        return TryGetStaticType(incDec.Operand, out type);

      case CBinaryExpr { Op: CBinaryOp.Add or CBinaryOp.Subtract } binary:
        {
          bool haveLeft = TryGetStaticType(binary.Left, out CType leftType);
          bool haveRight = TryGetStaticType(binary.Right, out CType rightType);
          if (haveLeft && leftType.IsPointer)
          {
            type = leftType;
            return true;
          }

          if (haveRight && rightType.IsPointer && binary.Op == CBinaryOp.Add)
          {
            type = rightType;
            return true;
          }

          type = PromoteArithmeticType(haveLeft ? leftType : null, haveRight ? rightType : null);
          return haveLeft || haveRight;
        }

      case CBinaryExpr { Op: CBinaryOp.Equal or CBinaryOp.NotEqual or CBinaryOp.Less or CBinaryOp.Greater or CBinaryOp.LessOrEqual or CBinaryOp.GreaterOrEqual or CBinaryOp.LogicalAnd or CBinaryOp.LogicalOr }:
        type = CType.Int;
        return true;

      case CBinaryExpr { Op: CBinaryOp.ShiftLeft or CBinaryOp.ShiftRight } shift:
        {
          // A shift's result has the (promoted) type of its LEFT operand only.
          bool haveShiftLeft = TryGetStaticType(shift.Left, out CType shiftLeftType);
          type = haveShiftLeft && (shiftLeftType.IsLong || shiftLeftType.IsFloat) ? shiftLeftType : CType.Int;
          return haveShiftLeft;
        }

      case CBinaryExpr binary:
        {
          bool haveLeft = TryGetStaticType(binary.Left, out CType leftType);
          bool haveRight = TryGetStaticType(binary.Right, out CType rightType);
          type = PromoteArithmeticType(haveLeft ? leftType : null, haveRight ? rightType : null);
          return haveLeft || haveRight;
        }

      default:
        type = CType.Int;
        return false;
    }
  }

  /// <summary>ADDED 2026-11-08. The usual arithmetic conversions of this compiler's type system: a
  /// <c>float</c> operand makes the result <c>float</c>; otherwise a <c>long</c> operand makes it
  /// <c>long</c> (<c>unsigned long</c> if either operand is <c>unsigned long</c> -- an <c>unsigned int</c>
  /// operand converts to a signed <c>long</c> because 32 bits hold every 16-bit value); otherwise the
  /// result stays plain <c>int</c> (its signedness is tracked by the int code paths themselves, as
  /// before).</summary>
  private static CType PromoteArithmeticType(CType? left, CType? right)
  {
    if (left is { IsFloat: true } || right is { IsFloat: true })
    {
      return CType.Float;
    }

    if (left is { IsLong: true } || right is { IsLong: true })
    {
      return left?.Kind == CTypeKind.UnsignedLong || right?.Kind == CTypeKind.UnsignedLong ? CType.UnsignedLong : CType.Long;
    }

    return CType.Int;
  }

  private bool IsLongTyped(CExpr expr) => TryGetStaticType(expr, out CType type) && type.IsLong;

  private static bool IsUnsupportedWholeValueType(CType type) => type.IsStruct;

  private CType ReportUnsupportedValueRead(CSourceLocation location, CType type, string subject)
  {
    string reason = "reading or assigning a whole struct value is not yet supported by this compiler (access one of its own members instead)";
    Error(location, $"{subject} has type \"{type}\" -- {reason}");
    return CType.Int;
  }

  /// <summary>Whether <paramref name="expr"/> contains a function call anywhere inside it --
  /// claude/cvm-abi.md section 3.4's "spilling across a call" rule: every register is call-volatile, so
  /// a binary expression whose RIGHT operand contains a call must spill its already-computed LEFT
  /// operand to a frame scratch slot first.</summary>
  private static bool ContainsCall(CExpr expr) => expr switch
  {
    CCallExpr => true,
    CUnaryExpr unary => ContainsCall(unary.Operand),
    CBinaryExpr binary => ContainsCall(binary.Left) || ContainsCall(binary.Right),
    CAssignExpr assign => ContainsCall(assign.Target) || ContainsCall(assign.Value),
    CCompoundAssignExpr compoundAssign => ContainsCall(compoundAssign.Target) || ContainsCall(compoundAssign.Value),
    CConditionalExpr conditional => ContainsCall(conditional.Condition) || ContainsCall(conditional.WhenTrue) || ContainsCall(conditional.WhenFalse),
    CIndexExpr index => ContainsCall(index.Base) || ContainsCall(index.Index),
    CCastExpr cast => ContainsCall(cast.Operand),
    CCommaExpr comma => ContainsCall(comma.Left) || ContainsCall(comma.Right),
    CMemberAccessExpr member => ContainsCall(member.Base),
    CSizeOfExprExpr => false, // the operand is never evaluated.
    _ => false,
  };

  /// <summary>ADDED 2026-11-08. Whether evaluating <paramref name="expr"/> may CLOBBER registers it was not
  /// asked to touch: it contains a real function call (<see cref="ContainsCall"/>), OR it contains an
  /// operator that lowers to a runtime-library call (<c>*</c>, <c>/</c>, <c>%</c>, a variable shift, or an
  /// array/pointer index whose element size is too large to scale inline). A library routine is an ordinary
  /// C function, so it is call-volatile for the WIDE scratch registers exactly like a user function.
  /// Callers use this (not <see cref="ContainsCall"/>) to decide whether a live pointer/float register pair
  /// must be spilled to a frame slot first. Scalar scratch registers need no such spill: <see
  /// cref="EmitLibraryBinaryCall"/> saves and restores those itself. Deliberately conservative -- a "true"
  /// only costs one extra spill.</summary>
  private bool MayCallOrUseLibrary(CExpr expr) => expr switch
  {
    CCallExpr => true,
    CUnaryExpr unary => MayCallOrUseLibrary(unary.Operand),
    CBinaryExpr binary => UsesLibraryOperator(binary.Op, binary.Left, binary.Right)
        || PointerScaleUsesLibrary(binary)
        || MayCallOrUseLibrary(binary.Left) || MayCallOrUseLibrary(binary.Right),
    CAssignExpr assign => MayCallOrUseLibrary(assign.Target) || MayCallOrUseLibrary(assign.Value)
        || (TryGetStaticType(assign.Target, out CType assignTargetType) && NeedsFloatConversionCall(assignTargetType, assign.Value)),
    CCompoundAssignExpr compoundAssign => UsesLibraryOperator(compoundAssign.Op, compoundAssign.Target, compoundAssign.Value)
        || MayCallOrUseLibrary(compoundAssign.Target) || MayCallOrUseLibrary(compoundAssign.Value),
    CConditionalExpr conditional => MayCallOrUseLibrary(conditional.Condition) || MayCallOrUseLibrary(conditional.WhenTrue) || MayCallOrUseLibrary(conditional.WhenFalse)
        || (TryGetStaticType(conditional, out CType conditionalType)
            && (NeedsFloatConversionCall(conditionalType, conditional.WhenTrue) || NeedsFloatConversionCall(conditionalType, conditional.WhenFalse))),
    CIndexExpr index => MayCallOrUseLibrary(index.Base) || MayCallOrUseLibrary(index.Index) || IndexScaleUsesLibrary(index) || IsFloatTyped(index.Index),
    CCastExpr cast => MayCallOrUseLibrary(cast.Operand) || NeedsFloatConversionCall(cast.TargetType, cast.Operand),
    CCommaExpr comma => MayCallOrUseLibrary(comma.Left) || MayCallOrUseLibrary(comma.Right),
    CMemberAccessExpr member => MayCallOrUseLibrary(member.Base),
    CSizeOfExprExpr => false, // the operand is never evaluated.
    _ => false,
  };

  private bool IsFloatTyped(CExpr expr) => TryGetStaticType(expr, out CType type) && type.IsFloat;

  /// <summary>ADDED 2026-11-08. Whether converting <paramref name="value"/> to <paramref name="targetType"/>
  /// calls a cvmfloat.asm routine: float &lt;-&gt; integer conversions do (<c>__ltof</c>, <c>__ultof</c>,
  /// <c>__ftol</c>, <c>__ftoul</c>), except an integer CONSTANT converted to float, which is folded at compile
  /// time. Pointer conversions are errors elsewhere and count as "no call".</summary>
  private bool NeedsFloatConversionCall(CType targetType, CExpr value)
  {
    if (!TryGetStaticType(value, out CType valueType) || targetType.IsFloat == valueType.IsFloat)
    {
      return false;
    }

    if (targetType.IsPointer || valueType.IsPointer)
    {
      return false;
    }

    return !(targetType.IsFloat && TryGetSimpleConstant(value, out _, out _));
  }

  /// <summary>A float operand expression that is evaluated while another wide value is live: true when it may
  /// call or use a library routine, or needs an int/long -> float conversion call.</summary>
  private bool FloatOperandUsesLibrary(CExpr expr) => MayCallOrUseLibrary(expr) || NeedsFloatConversionCall(CType.Float, expr);

  /// <summary>Whether converting <paramref name="value"/> to <paramref name="targetType"/>
  /// calls a cvmfloat.asm routine.</summary>
  private bool ConversionUsesLibrary(CType targetType, CExpr value) => NeedsFloatConversionCall(targetType, value);

  /// <summary>Whether <c>left op right</c> (or <c>left op= right</c>) takes the <c>__mul</c>/<c>__divs</c>/...
  /// library path. Mirrors <see cref="TryEmitConstantIntOperation"/>: a constant operand that has an inline
  /// form does not. Float <c>*</c> and <c>/</c> are coprocessor opcodes, not library calls. Unknown static
  /// types are treated as "uses the library".</summary>
  private bool UsesLibraryOperator(CBinaryOp op, CExpr left, CExpr right)
  {
    // ADDED 2026-11-08: every long * / % shift and ordered comparison is a call into the cvm library.
    if (op is CBinaryOp.Multiply or CBinaryOp.Divide or CBinaryOp.Modulo or CBinaryOp.ShiftLeft or CBinaryOp.ShiftRight
        or CBinaryOp.Less or CBinaryOp.LessOrEqual or CBinaryOp.Greater or CBinaryOp.GreaterOrEqual
        && (IsLongTyped(left) || IsLongTyped(right)))
    {
      return true;
    }

    // ADDED 2026-11-08: float comparisons (__flt ... __fne) and the implicit int/long -> float conversion of
    // a mixed int/float operation (__ltof / __ultof) are calls into cvmfloat.asm; any other operator with a
    // float operand falls back to converting it to an integer (__ftol).
    if (op is not (CBinaryOp.LogicalAnd or CBinaryOp.LogicalOr))
    {
      bool leftIsFloat = IsFloatTyped(left);
      bool rightIsFloat = IsFloatTyped(right);
      if (leftIsFloat || rightIsFloat)
      {
        if (op is CBinaryOp.Add or CBinaryOp.Subtract or CBinaryOp.Multiply or CBinaryOp.Divide)
        {
          return NeedsFloatConversionCall(CType.Float, leftIsFloat ? right : left);
        }

        return true;
      }
    }

    switch (op)
    {
      case CBinaryOp.Multiply:
      case CBinaryOp.Divide:
        {
          if (TryGetStaticType(left, out CType leftType) && leftType.IsFloat)
          {
            return false;
          }

          if (op == CBinaryOp.Multiply)
          {
            if (TryGetSimpleConstant(right, out long factor, out _) && factor >= -32768 && factor <= 65535 && MultiplyIsInline(factor))
            {
              return false;
            }

            if (TryGetSimpleConstant(left, out long leftFactor, out _) && leftFactor >= -32768 && leftFactor <= 65535 && MultiplyIsInline(leftFactor))
            {
              return false;
            }

            return true;
          }

          return !IsInlineUnsignedPowerOfTwo(left, right);
        }

      case CBinaryOp.Modulo:
        return !IsInlineUnsignedPowerOfTwo(left, right);

      case CBinaryOp.ShiftLeft:
      case CBinaryOp.ShiftRight:
        return !(TryGetSimpleConstant(right, out long count, out _) && count >= 0 && count <= 255);

      // ADDED 2026-11-08: ordered comparisons call __lts/__ltu/... except against a literal 0 on a plain
      // (non-pointer, non-float) left operand -- mirrors EmitComparisonInto. Unknown static types count as
      // "uses the library" (the safe direction: an extra spill, never a missed one).
      case CBinaryOp.Less:
      case CBinaryOp.LessOrEqual:
      case CBinaryOp.Greater:
      case CBinaryOp.GreaterOrEqual:
        {
          bool leftKnown = TryGetStaticType(left, out CType orderedLeftType);
          bool rightKnown = TryGetStaticType(right, out CType orderedRightType);
          if (leftKnown && rightKnown && orderedLeftType.IsFloat | orderedRightType.IsFloat)
          {
            return false; // rejected with an error anyway.
          }

          bool plainIntegers = leftKnown && rightKnown && !orderedLeftType.IsPointer && !orderedRightType.IsPointer;
          return !(plainIntegers && TryGetSimpleConstant(right, out long zero, out _) && zero == 0);
        }

      default:
        return false;
    }
  }

  /// <summary>ADDED 2026-11-08. True for an int expression that can safely be evaluated with its partial
  /// results held in <c>r0</c> (see the <see cref="CReturnStmt"/> case of <c>EmitStatement</c>): only
  /// literals, non-pointer/non-float variable reads, unary <c>+ - ! ~</c>, casts, and binary operators that
  /// are not runtime-library calls. Nothing in such an expression can call a function, take the library
  /// path, or touch the <c>d0</c> pointer-arithmetic scratch.</summary>
  private bool IsPlainScalarExpression(CExpr expr) => expr switch
  {
    CIntLiteralExpr => true,
    CNameExpr name => TryGetStaticType(name, out CType type) && !type.IsPointer && !type.IsFloat && !type.IsArray,
    CUnaryExpr unary => unary.Op is CUnaryOp.Plus or CUnaryOp.Minus or CUnaryOp.LogicalNot or CUnaryOp.BitwiseNot
        && IsPlainScalarExpression(unary.Operand),
    CCastExpr cast => IsPlainScalarExpression(cast.Operand),
    CBinaryExpr binary => !UsesLibraryOperator(binary.Op, binary.Left, binary.Right)
        && IsPlainScalarExpression(binary.Left) && IsPlainScalarExpression(binary.Right),
    _ => false,
  };

  private bool IsInlineUnsignedPowerOfTwo(CExpr left, CExpr right)
  {
    if (!TryGetSimpleConstant(right, out long constant, out bool rightIsUnsigned) || !IsPowerOfTwo(constant) || constant > 0x8000)
    {
      return false;
    }

    return rightIsUnsigned || (TryGetStaticType(left, out CType leftType) && leftType.IsUnsigned);
  }

  /// <summary>Pointer +/- integer where the element size is too large for an inline scale.</summary>
  private bool PointerScaleUsesLibrary(CBinaryExpr binary)
  {
    if (binary.Op is not (CBinaryOp.Add or CBinaryOp.Subtract))
    {
      return false;
    }

    CExpr indexSide;
    CType pointerType;
    if (TryGetStaticType(binary.Left, out CType leftType) && leftType.IsPointer)
    {
      indexSide = binary.Right;
      pointerType = leftType;
    }
    else if (binary.Op == CBinaryOp.Add && TryGetStaticType(binary.Right, out CType rightType) && rightType.IsPointer)
    {
      indexSide = binary.Left;
      pointerType = rightType;
    }
    else
    {
      return false;
    }

    return pointerType.ElementType is not null
        && !TryEvaluateConstantLong(indexSide, out _)
        && ElementScaleUsesLibrary(pointerType.ElementType);
  }

  private bool IndexScaleUsesLibrary(CIndexExpr index)
  {
    if (TryEvaluateConstantLong(index.Index, out _))
    {
      return false;
    }

    return !TryGetStaticType(index.Base, out CType baseType) || baseType.ElementType is null || ElementScaleUsesLibrary(baseType.ElementType);
  }

  private static bool ElementScaleUsesLibrary(CType elementType)
  {
    int words = Math.Max(elementType.SizeInWords, 1);
    return words != 1 && !MultiplyIsInline(words);
  }

  // ---------------------------------------------------------------------------------------------------
  // Expression evaluation. EmitIntExprInto evaluates an int/char/unsigned/comparison/logical-result
  // expression into plain register `reg`; EmitWideExprInto evaluates a pointer/float expression into
  // double register `destDouble`. `depth` is the shared nesting-depth parameter described on the
  // Addressing section above -- `reg`/`destDouble` need not themselves equal ScalarReg(depth)/
  // WideReg(depth) (a top-level caller may pass a param/return register instead), but every INTERNAL
  // sub-computation always does.
  //
  // A statement-level caller that just needs an expression evaluated (and does not care where the
  // result lands) goes through EmitStatementExprForEffect below instead of a dedicated "for value"
  // entry point -- it already takes the shared `depth` parameter every internal evaluator needs, so one
  // method covers both "evaluate at the top of a statement" (depth 0) and "evaluate as the discarded
  // left side of a comma operator at whatever depth the enclosing expression is already at".

  /// <summary>Loads an addressable expression's (name/index/member/dereference) whole value into
  /// whichever of <paramref name="scalarReg"/>/<paramref name="wideDouble"/> matches its own type --
  /// shared by both <see cref="EmitIntExprInto"/> and <see cref="EmitWideExprInto"/>'s leaf cases.
  /// </summary>
  private CType EmitLoadAddressableValue(CExpr expr, int scalarReg, int wideDouble, int depth)
  {
    CAddr addr = ResolveAddr(expr, depth);
    if (IsUnsupportedWholeValueType(addr.Type))
    {
      return ReportUnsupportedValueRead(expr.Location, addr.Type, "this expression");
    }

    EmitLoadValueFromAddr(addr, scalarReg, wideDouble, depth);
    return addr.Type;
  }

  // FIXED 2026-11-08: the two scalar-leaf cases below pass a wide register as the (unused) "wideDouble"
  // placeholder. That used to be WideReg(depth), which reported "pointer/float scratch pool exhausted" for any
  // PURELY INTEGER expression nested more than 3 levels deep (the wide pool has 3 levels, the scalar pool 8),
  // e.g. "(a * b + c) * (d - a / b)". The placeholder is clamped to the last wide level instead; it is never
  // written for a scalar value.
  private CType EmitIntExprInto(CExpr expr, int reg, int depth)
  {
    // ADDED 2026-11-08: an expression whose static type is long / unsigned long is evaluated in a double
    // register and only its LOW word is kept (C's conversion of a long to a 16-bit int).
    if (TryGetStaticType(expr, out CType staticLongCheck) && staticLongCheck.IsLong)
    {
      return EmitLongTruncatedInto(expr, reg, depth);
    }

    // ADDED 2026-11-08: a float-typed expression used where an int is wanted is converted (truncated toward
    // zero) with __ftol and its low word kept.
    if (TryGetStaticType(expr, out CType staticFloatCheck) && staticFloatCheck.IsFloat)
    {
      return EmitFloatTruncatedInto(expr, reg, depth, false);
    }

    switch (expr)
    {
      case CIntLiteralExpr literal:
        EmitCode($"rlit r{reg}, {literal.Value}");
        return literal.IsUnsigned ? CType.UnsignedInt : CType.Int;

      case CNameExpr or CIndexExpr or CMemberAccessExpr or CUnaryExpr { Op: CUnaryOp.Dereference }:
        return EmitLoadAddressableValue(expr, reg, WideReg(Math.Min(depth, WideScratchMaxDepth - 1)), depth);

      case CUnaryExpr { Op: CUnaryOp.Plus } unary:
        return EmitIntExprInto(unary.Operand, reg, depth);

      case CUnaryExpr { Op: CUnaryOp.Minus } unary:
        {
          CType type = EmitIntExprInto(unary.Operand, reg, depth);
          EmitCode($"neg r{reg}");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.BitwiseNot } unary:
        {
          CType type = EmitIntExprInto(unary.Operand, reg, depth);
          EmitCode($"inv r{reg}");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.LogicalNot } unary:
        {
          EmitConditionTestInto(unary.Operand, reg, depth);
          EmitCode($"cond r{reg}, ==0");
          EmitCode($"rpop r{reg}");
          return CType.Int;
        }

      case CUnaryExpr { Op: CUnaryOp.AddressOf }:
        Error(expr.Location, "the address of an expression (\"&...\") is a pointer -- assign it to a pointer-typed variable or expression");
        EmitCode($"rlit r{reg}, 0");
        return CType.Int;

      case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostIncrement or CUnaryOp.PostDecrement } incDec:
        return EmitIncrementOrDecrement(incDec, reg, WideReg(Math.Min(depth, WideScratchMaxDepth - 1)), depth);

      case CBinaryExpr { Op: CBinaryOp.LogicalAnd or CBinaryOp.LogicalOr } logical:
        EmitLogicalInto(logical, reg, depth);
        return CType.Int;

      case CBinaryExpr { Op: CBinaryOp.Equal or CBinaryOp.NotEqual or CBinaryOp.Less or CBinaryOp.Greater or CBinaryOp.LessOrEqual or CBinaryOp.GreaterOrEqual } comparison:
        EmitComparisonInto(comparison, reg, depth);
        return CType.Int;

      case CBinaryExpr binary:
        {
          // ADDED 2026-11-08: multiplication is commutative, so a constant LEFT factor ("4 * x") is
          // swapped to the right where TryEmitConstantIntOperation can strength-reduce it. The constant
          // has no side effects and no call, so the evaluation order of the remaining operand is untouched.
          CExpr leftOperand = binary.Left;
          CExpr rightOperand = binary.Right;
          if (binary.Op == CBinaryOp.Multiply && TryGetSimpleConstant(leftOperand, out _, out _) && !TryGetSimpleConstant(rightOperand, out _, out _))
          {
            (leftOperand, rightOperand) = (rightOperand, leftOperand);
          }

          bool spillNeeded = ContainsCall(rightOperand);
          int? spillSlot = spillNeeded ? AllocateFrameWords(1) : null;
          CType leftType = EmitIntExprInto(leftOperand, reg, depth);

          // ADDED 2026-11-08: a constant right operand with a cheaper current-opcode form (inc/dec,
          // lowbyte, mask15, inv15, mul2/udiv2/div2 shifts, shift-and-add multiply, ...) is handled here
          // without ever loading the constant -- see TryEmitConstantIntOperation. spillSlot is always null
          // in that case (a constant contains no call).
          if (TryEmitConstantIntOperation(binary.Op, leftType, rightOperand, reg, ScalarReg(depth + 1), out CType constantResultType))
          {
            return constantResultType;
          }

          if (spillSlot is not null)
          {
            EmitCode($"stf r{reg}, {spillSlot}");
          }

          int rightReg = ScalarReg(depth + 1);
          CType rightType = EmitIntExprInto(rightOperand, rightReg, depth + 1);
          if (spillSlot is not null)
          {
            EmitCode($"ldf r{reg}, {spillSlot}");
          }

          return EmitIntBinaryOperation(binary.Op, leftType, rightType, reg, rightReg, binary.Location);
        }

      case CCastExpr cast:
        return EmitIntCast(cast, reg, depth);

      case CConditionalExpr conditional:
        return EmitConditionalInto(conditional, reg, -1, depth, isWide: false);

      case CAssignExpr assign:
        return EmitAssignInto(assign, reg, -1, depth, isWide: false);

      case CCompoundAssignExpr compoundAssign:
        return EmitCompoundAssignInto(compoundAssign, reg, -1, depth, isWide: false);

      case CCallExpr call:
        return EmitCallInto(call, reg, -1, depth, isWide: false);

      case CCommaExpr comma:
        EmitStatementExprForEffect(comma.Left, depth);
        return EmitIntExprInto(comma.Right, reg, depth);

      case CSizeOfTypeExpr sizeOfType:
        EmitCode($"rlit r{reg}, {sizeOfType.Type.SizeInWords}");
        return CType.UnsignedInt;

      case CSizeOfExprExpr sizeOfExpr:
        {
          TryGetStaticType(sizeOfExpr.Operand, out CType operandType);
          EmitCode($"rlit r{reg}, {Math.Max(operandType.SizeInWords, 1)}");
          return CType.UnsignedInt;
        }

      default:
        Error(expr.Location, $"this expression is not yet supported by the code generator");
        EmitCode($"rlit r{reg}, 0");
        return CType.Int;
    }
  }

  private CType EmitWideExprInto(CExpr expr, int destDouble, int depth)
  {
    switch (expr)
    {
      case CIntLiteralExpr { IsLong: true } longLiteral:
        EmitLongConstantInto(longLiteral.Value, destDouble);
        return longLiteral.IsUnsigned ? CType.UnsignedLong : CType.Long;

      case CUnaryExpr { Op: CUnaryOp.Minus or CUnaryOp.BitwiseNot } longUnary when IsLongTyped(expr):
        return EmitLongUnary(longUnary, destDouble, depth);

      case CBinaryExpr longBinary when IsLongTyped(expr):
        return EmitLongBinary(longBinary, destDouble, depth);

      case CIntLiteralExpr { Value: 0 }:
        // A plain "0" is the only integer literal that may stand for a NULL pointer.
        EmitCode($"dlit d{destDouble}, 0, 0");
        return CType.PointerTo(CType.Void);

      case CFloatLiteralExpr literal:
        EmitFloatLiteralInto(literal.Value, destDouble);
        return CType.Float;

      case CStringLiteralExpr stringLiteral:
        EmitCode($"dlit d{destDouble}, {GlobalDataPage}, {InternStringLiteral(stringLiteral.Value)}");
        return CType.PointerTo(CType.Char);

      case CNameExpr or CIndexExpr or CMemberAccessExpr or CUnaryExpr { Op: CUnaryOp.Dereference }:
        {
          if (TryGetStaticType(expr, out CType exprType) && exprType.IsArray)
          {
            // Array used as a value (not &arr, not arr[i]) decays to its own address. Same fix as
            // EmitPointerDecayInto's own matching entry above (and same bug, duplicated here) --
            // EmitAddressOfInto returns the ARRAY's own type, not its element type, so this must
            // unwrap ElementType itself before wrapping it in CType.PointerTo.
            CType arrayType = EmitAddressOfInto(expr, destDouble, depth);
            return CType.PointerTo(arrayType.ElementType!);
          }

          return EmitLoadAddressableValue(expr, ScalarReg(depth), destDouble, depth);
        }

      case CUnaryExpr { Op: CUnaryOp.AddressOf } addressOf:
        {
          CType pointeeType = EmitAddressOfInto(addressOf.Operand, destDouble, depth);
          return CType.PointerTo(pointeeType);
        }

      case CUnaryExpr { Op: CUnaryOp.Plus } unary:
        return EmitWideExprInto(unary.Operand, destDouble, depth);

      case CUnaryExpr { Op: CUnaryOp.Minus } unary:
        {
          CType type = EmitWideExprInto(unary.Operand, destDouble, depth);
          if (!type.IsFloat)
          {
            Error(expr.Location, $"unary \"-\" is not defined for \"{type}\"");
            return type;
          }

          // No float negate opcode exists -- flip the IEEE sign bit via the r-alias trick (valid for
          // d0-d7, the individually-addressable half; destDouble is always d1-d3 here, the general wide
          // scratch pool -- claude/cvm-abi.md section 3.4/3.8).
          EmitCode($"inv15 r{2 * destDouble + 1}");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostIncrement or CUnaryOp.PostDecrement } incDec:
        return EmitIncrementOrDecrement(incDec, ScalarReg(depth), destDouble, depth);

      case CBinaryExpr { Op: CBinaryOp.Add or CBinaryOp.Subtract } addSub:
        return EmitWidePointerOrFloatAddSub(addSub, destDouble, depth);

      case CBinaryExpr { Op: CBinaryOp.Multiply or CBinaryOp.Divide } floatMulDiv:
        return EmitFloatBinary(floatMulDiv, destDouble, depth);

      case CConditionalExpr conditional:
        return EmitConditionalInto(conditional, -1, destDouble, depth, isWide: true);

      case CAssignExpr assign:
        return EmitAssignInto(assign, -1, destDouble, depth, isWide: true);

      case CCompoundAssignExpr compoundAssign:
        return EmitCompoundAssignInto(compoundAssign, -1, destDouble, depth, isWide: true);

      case CCallExpr call:
        return EmitCallInto(call, -1, destDouble, depth, isWide: true);

      case CCastExpr cast:
        return EmitWideCast(cast, destDouble, depth);

      case CCommaExpr comma:
        EmitStatementExprForEffect(comma.Left, depth);
        return EmitWideExprInto(comma.Right, destDouble, depth);

      default:
        Error(expr.Location, "this expression is not yet supported by the code generator");
        EmitCode($"dlit d{destDouble}, 0, 0");
        return CType.PointerTo(CType.Void);
    }
  }

  /// <summary>Evaluates <paramref name="expr"/> purely for a side effect, discarding its value --
  /// comma operator's left-hand side, and a bare expression statement.</summary>
  private void EmitStatementExprForEffect(CExpr expr, int depth)
  {
    TryGetStaticType(expr, out CType type);
    if (type.IsWideValue)
    {
      EmitWideExprInto(expr, WideReg(depth), depth);
    }
    else
    {
      EmitIntExprInto(expr, ScalarReg(depth), depth);
    }
  }

  /// <summary>Evaluates <paramref name="expr"/> as a condition test subject and leaves <c>rN</c> set up
  /// so an immediately-following <c>cond</c>/<c>if</c> tests the right thing: for an int/pointer
  /// expression that is simply its value (nonzero = true); float conditions are not supported (no
  /// hardware sign-test exists for a float's bit pattern as a truth value here).</summary>
  private void EmitConditionTestInto(CExpr expr, int reg, int depth)
  {
    TryGetStaticType(expr, out CType type);
    if (type.IsFloat)
    {
      // ADDED 2026-11-08: a float is "true" when it is not +/-0.0: the high word without its sign bit, or'd
      // with the low word, is non-zero (a NaN is true, as in C).
      int floatDouble = WideReg(depth);
      EmitWideExprInto(expr, floatDouble, depth);
      EmitCode($"mov r{reg}, r{2 * floatDouble + 1}");
      EmitCode($"mask15 r{reg}");
      EmitCode($"or r{reg}, r{2 * floatDouble}");
      return;
    }

    if (type.IsLong)
    {
      // ADDED 2026-11-08: a long is "true" when either of its two words is non-zero.
      int longDouble = WideReg(depth);
      EmitWideExprInto(expr, longDouble, depth);
      EmitCode($"mov r{reg}, r{2 * longDouble}");
      EmitCode($"or r{reg}, r{2 * longDouble + 1}");
      return;
    }

    if (type.IsPointer)
    {
      int wideDouble = WideReg(depth);
      EmitWideExprInto(expr, wideDouble, depth);
      EmitCode($"mov r{reg}, r{2 * wideDouble}"); // NULL test only looks at the address half -- see EmitWidePointerOrFloatAddSub's own "far pointers" remarks for why this project never needs to look at the page half for a truth test.
      return;
    }

    EmitIntExprInto(expr, reg, depth);
  }

  /// <summary>
  /// Adds a compile-time constant to a pointer/frame-relative ADDRESS in place -- used for a
  /// frame/struct-member/constant-array-index/pointer-increment offset. <paramref name="register"/> is
  /// always the low (address) r-alias of some double register (<c>2 *</c> that double register's own
  /// index -- every call site below satisfies this). <paramref name="guaranteedSamePage"/> is the
  /// caller's own claim, decided per call site (see each one's own remarks), about whether
  /// <paramref name="register"/>'s CURRENT value plus <paramref name="constant"/> is guaranteed to never
  /// leave the current page (never overflow/underflow the low 16 bits).
  ///
  /// CHANGED 2026-10-18 (two rounds, both per Stefan directly): first "i suggest reserving 1
  /// register(d0) for inflight pointer arithmetic ... so change the ABI to use register d0 (r0,r1) as
  /// reserved for pointer arithmetic and return values", then the correction that actually drives the
  /// branch below: "rlit and add can only be used if it is guaranteed that the final address does not
  /// cross a page border. if not then dlit and dadd must be used for pointer arithmetic." The reason: a
  /// plain 16-bit <c>inc</c>/<c>dec</c>/<c>add</c> on the address half alone WRAPS silently on
  /// overflow/underflow -- it has no carry-out to the page half -- so if the true result ever left the
  /// current page, that cheap encoding would silently produce a wrong address on the SAME page instead
  /// of the correct address on the next/previous one. There is still no add-WITH-IMMEDIATE opcode
  /// either way (the 16-bit binary ALU row is register-register only), so a literal first has to land in
  /// a scratch register regardless of which path is taken.
  ///
  /// - <paramref name="guaranteedSamePage"/> == <c>true</c>: the cheap encoding. <c>|constant| &lt;= 1</c>
  ///   goes through a single <c>inc</c>/<c>dec</c> (one word); anything larger loads the constant into
  ///   <see cref="PointerArithmeticScratchDouble"/>'s own low alias (<c>r0</c>) via <c>rlit</c> and does
  ///   ONE <c>add</c> (two words flat, regardless of magnitude -- replacing what used to be one
  ///   <c>inc</c>/<c>dec</c> PER UNIT, e.g. a frame offset of 7 used to cost 7 words). <c>rlit</c> accepts
  ///   a negative literal directly, so <c>add</c> alone covers both directions.
  /// - <paramref name="guaranteedSamePage"/> == <c>false</c>: the safe encoding. <paramref
  ///   name="register"/>'s own double register (<c>register / 2</c>) must already hold a COMPLETE, valid
  ///   <c>(page, address)</c> value in both halves before this call -- true at every call site below (see
  ///   each one's own remarks) -- because the constant is loaded as a full signed 32-bit value via
  ///   <c>dlit</c> into a scratch double register, then added with <c>dadd</c> (node 408's own
  ///   carry-propagating 32-bit add, <c>x5/dadd</c>), so a carry/borrow out of the address half correctly
  ///   adjusts the page half too. Four words total regardless of <paramref name="constant"/>'s magnitude
  ///   or sign -- correctness over the cheap path's word-count savings, for any address this method
  ///   cannot prove stays on one page.
  ///
  /// Either path needs a scratch double register distinct from the one actually being adjusted (so the
  /// literal load doesn't clobber the very base value being adjusted) -- unavoidable at one specific call
  /// path: a pointer/<c>float</c> <c>return</c> statement computes its value directly into <see
  /// cref="WideReturnDouble"/>, which IS <see cref="PointerArithmeticScratchDouble"/> (<c>d0</c>),
  /// deliberately, per the ABI change above (e.g. <c>return &amp;arr[7];</c>). The cheap path has no
  /// OTHER individually-addressable double register available to borrow (the r-alias trick -- a plain
  /// 16-bit op on one half -- only reaches <c>d0</c>-<c>d7</c>, all of which are otherwise already
  /// claimed by the param/scratch pools), so it falls back to the always-correct repeated
  /// <c>inc</c>/<c>dec</c> instead when the target IS <c>d0</c> itself. The safe path, needing no
  /// individually-16-bit-addressable register at all (<c>dlit</c>/<c>dadd</c> are whole-double-register
  /// ops), simply borrows <see cref="GlobalAddressTempDouble"/> (<c>d8</c>) instead in that one case --
  /// already established elsewhere in this file as a dedicated, collision-free scratch double.
  /// </summary>
  private void EmitAddConstantToRegister(int register, int constant, bool guaranteedSamePage)
  {
    if (Math.Abs(constant) > 128)
    {
      _diagnostics.Add($"<internal>: EmitAddConstantToRegister({constant}) on r{register} -- this compiler only expects small frame/member/index offsets; something is unexpectedly large.");
    }

    int targetDouble = register / 2;
    bool targetIsPointerArithmeticScratchItself = targetDouble == PointerArithmeticScratchDouble;

    if (!guaranteedSamePage)
    {
      // ADDED 2026-11-08: dinc/ddec/dinc2/ddec2 (node 208) add/subtract 1 or 2 to the WHOLE 32-bit
      // double register with the carry/borrow propagating into the page word -- the same full-width
      // semantics as dlit+dadd, but without the 3-word dlit, the scratch double register, or the 1-word
      // dadd. At most three such instructions are emitted (|constant| <= 6); anything bigger keeps the
      // dlit+dadd pair (4 words, no better).
      if (Math.Abs(constant) <= 6)
      {
        string stepTwo = constant > 0 ? "dinc2" : "ddec2";
        string stepOne = constant > 0 ? "dinc" : "ddec";
        for (int remaining = Math.Abs(constant); remaining > 0; remaining -= 2)
        {
          EmitCode($"{(remaining >= 2 ? stepTwo : stepOne)} d{targetDouble}");
        }

        return;
      }

      int scratchDouble = targetIsPointerArithmeticScratchItself ? GlobalAddressTempDouble : PointerArithmeticScratchDouble;
      EmitCode($"dlit d{scratchDouble}, {constant}");
      EmitCode($"dadd d{targetDouble}, d{scratchDouble}");
      return;
    }

    if (Math.Abs(constant) <= 1 || targetIsPointerArithmeticScratchItself)
    {
      int steps = Math.Abs(constant);
      string mnemonic = constant > 0 ? "inc" : "dec";
      for (int i = 0; i < steps; i++)
      {
        EmitCode($"{mnemonic} r{register}");
      }

      return;
    }

    EmitCode($"rlit r{2 * PointerArithmeticScratchDouble}, {constant}");
    EmitCode($"add r{register}, r{2 * PointerArithmeticScratchDouble}");
  }

  /// <summary>Reads ONE word of <paramref name="addr"/> (word <paramref name="wordIndex"/>, 0 = low)
  /// into plain register <paramref name="destRegister"/>.</summary>
  private void EmitLoadWordFromAddr(CAddr addr, int wordIndex, int destRegister)
  {
    switch (addr.Kind)
    {
      case CAddrKind.Frame:
        EmitCode($"ldf r{destRegister}, {addr.Offset + wordIndex}");
        return;

      case CAddrKind.Global:
        EmitCode($"dlit d{GlobalAddressTempDouble}, {GlobalDataPage}, {GlobalWordLabel(addr.Label!, wordIndex)}");
        EmitCode($"rload r{destRegister}, d{GlobalAddressTempDouble}");
        return;

      case CAddrKind.Pointer:
        if (wordIndex != 0)
        {
          EmitCode($"inc r{2 * addr.PointerDouble}");
        }

        EmitCode($"rload r{destRegister}, d{addr.PointerDouble}");
        if (wordIndex != 0)
        {
          EmitCode($"dec r{2 * addr.PointerDouble}");
        }

        return;
    }
  }

  /// <summary>Writes ONE word of <paramref name="addr"/> from plain register <paramref
  /// name="srcRegister"/> -- the store-side mirror of <see cref="EmitLoadWordFromAddr"/>.</summary>
  private void EmitStoreWordToAddr(CAddr addr, int wordIndex, int srcRegister)
  {
    switch (addr.Kind)
    {
      case CAddrKind.Frame:
        EmitCode($"stf r{srcRegister}, {addr.Offset + wordIndex}");
        return;

      case CAddrKind.Global:
        EmitCode($"dlit d{GlobalAddressTempDouble}, {GlobalDataPage}, {GlobalWordLabel(addr.Label!, wordIndex)}");
        EmitCode($"rstore r{srcRegister}, d{GlobalAddressTempDouble}");
        return;

      case CAddrKind.Pointer:
        if (wordIndex != 0)
        {
          EmitCode($"inc r{2 * addr.PointerDouble}");
        }

        EmitCode($"rstore r{srcRegister}, d{addr.PointerDouble}");
        if (wordIndex != 0)
        {
          EmitCode($"dec r{2 * addr.PointerDouble}");
        }

        return;
    }
  }

  // ---------------------------------------------------------------------------------------------------
  // Lvalue mutation (assignment, compound assignment, increment/decrement). All three resolve the
  // target's address exactly ONCE (claude/cvm-abi.md's own addressing remarks, and CCompoundAssignExpr's
  // own doc comment) and share one register-safety rule: when the target's address itself lives in a
  // register (CAddrKind.Pointer -- e.g. "*p = ...", "*p += ...", "(*p)++"), the NEW value must be
  // computed/held in a register OTHER than the address's own, or computing it would overwrite the
  // pointer before the store-back gets to use it. Every method below gets this right by using depth+1
  // (a register guaranteed different from WideReg(depth)/ScalarReg(depth), which is what ResolveAddr
  // used to build the Pointer-kind address at the SAME depth) for that case, then moving into the
  // caller's requested destination afterward if needed; a Frame/Global target holds no register at all,
  // so the value can be computed directly into the destination with nothing to protect.

  private void CheckAssignmentType(CSourceLocation location, CType targetType, CType valueType)
  {
    if (targetType.IsFloat != valueType.IsFloat)
    {
      // An integer converts to / from float implicitly (cvmfloat.asm); a pointer or other type does not.
      if (!(targetType.IsFloat ? valueType : targetType).IsIntegral)
      {
        Error(location, $"cannot assign \"{valueType}\" to \"{targetType}\" (float/non-float mismatch -- cast explicitly)");
      }
    }
    else if (targetType.IsLong && !valueType.IsIntegral || valueType.IsLong && targetType.IsPointer)
    {
      Error(location, $"cannot assign \"{valueType}\" to \"{targetType}\" (cast explicitly)");
    }
  }

  /// <summary>ADDED 2026-11-08. When <paramref name="addr"/> is a computed pointer address
  /// (<see cref="CAddrKind.Pointer"/>) that stays live while <paramref name="laterExpr"/> is evaluated and
  /// that expression may call a function or a runtime-library routine, spills the address pair to a fresh
  /// 2-word frame slot and returns the slot; otherwise emits nothing and returns null. Pair with <see
  /// cref="RestorePointerAddress"/>.</summary>
  private int? SpillPointerAddressIfNeeded(CAddr addr, CExpr laterExpr)
  {
    if (addr.Kind != CAddrKind.Pointer || !(MayCallOrUseLibrary(laterExpr) || ConversionUsesLibrary(addr.Type, laterExpr)))
    {
      return null;
    }

    int slot = AllocateFrameWords(2);
    EmitCode($"stf r{2 * addr.PointerDouble}, {slot}");
    EmitCode($"stf r{2 * addr.PointerDouble + 1}, {slot + 1}");
    return slot;
  }

  private void RestorePointerAddress(CAddr addr, int? slot)
  {
    if (slot is null)
    {
      return;
    }

    EmitCode($"ldf r{2 * addr.PointerDouble}, {slot}");
    EmitCode($"ldf r{2 * addr.PointerDouble + 1}, {slot + 1}");
  }

  /// <summary>The "just do the assignment, no result needed" form -- used by <see cref="DeclareLocal"/>
  /// for an initializer, where <paramref name="addr"/> is always <see cref="CAddrKind.Frame"/> (a brand
  /// new local has no pointer register to protect), but written to handle every <see cref="CAddrKind"/>
  /// correctly regardless, for any future caller.</summary>
  private void EmitAssignToAddr(CAddr addr, CExpr valueExpr, CSourceLocation location, int depth)
  {
    if (IsUnsupportedWholeValueType(addr.Type))
    {
      ReportUnsupportedValueRead(location, addr.Type, "this assignment's target");
      return;
    }

    bool isWide = addr.Type.IsWideValue;
    int valueDepth = addr.Kind == CAddrKind.Pointer ? depth + 1 : depth;
    int? addressSpill = SpillPointerAddressIfNeeded(addr, valueExpr);

    if (isWide)
    {
      int valueDouble = WideReg(valueDepth);
      CType valueType = EmitValueForTarget(addr.Type, valueExpr, valueDouble, valueDepth);
      RestorePointerAddress(addr, addressSpill);
      CheckAssignmentType(location, addr.Type, valueType);
      EmitStoreValueToAddr(addr, 0, valueDouble);
    }
    else
    {
      int valueReg = ScalarReg(valueDepth);
      CType valueType = EmitIntExprInto(valueExpr, valueReg, valueDepth);
      RestorePointerAddress(addr, addressSpill);
      CheckAssignmentType(location, addr.Type, valueType);
      EmitStoreValueToAddr(addr, valueReg, 0);
    }
  }

  /// <summary>Plain "target = value" used as an EXPRESSION (its own value is the assigned value) --
  /// exactly one of <paramref name="scalarDest"/>/<paramref name="wideDest"/> is meaningful, selected by
  /// <paramref name="isWide"/>, per this file's own "-1 sentinel for the unused side" convention.</summary>
  private CType EmitAssignInto(CAssignExpr assign, int scalarDest, int wideDest, int depth, bool isWide)
  {
    CAddr addr = ResolveAddr(assign.Target, depth);
    if (IsUnsupportedWholeValueType(addr.Type))
    {
      return ReportUnsupportedValueRead(assign.Location, addr.Type, "this assignment's target");
    }

    bool targetIsWide = addr.Type.IsWideValue;
    if (targetIsWide != isWide)
    {
      Error(assign.Location, $"internal: assignment target wideness mismatch for \"{addr.Type}\"");
    }

    bool needsFreshRegister = addr.Kind == CAddrKind.Pointer;
    int? addressSpill = SpillPointerAddressIfNeeded(addr, assign.Value);

    if (targetIsWide)
    {
      int valueDouble = needsFreshRegister ? WideReg(depth + 1) : wideDest;
      CType valueType = EmitValueForTarget(addr.Type, assign.Value, valueDouble, needsFreshRegister ? depth + 1 : depth);
      RestorePointerAddress(addr, addressSpill);
      CheckAssignmentType(assign.Location, addr.Type, valueType);
      EmitStoreValueToAddr(addr, 0, valueDouble);
      if (needsFreshRegister && isWide && valueDouble != wideDest)
      {
        EmitCode($"mov r{2 * wideDest}, r{2 * valueDouble}");
        EmitCode($"mov r{2 * wideDest + 1}, r{2 * valueDouble + 1}");
      }
    }
    else
    {
      int valueReg = needsFreshRegister ? ScalarReg(depth + 1) : scalarDest;
      CType valueType = EmitIntExprInto(assign.Value, valueReg, needsFreshRegister ? depth + 1 : depth);
      RestorePointerAddress(addr, addressSpill);
      CheckAssignmentType(assign.Location, addr.Type, valueType);
      EmitStoreValueToAddr(addr, valueReg, 0);
      if (needsFreshRegister && !isWide && valueReg != scalarDest)
      {
        EmitCode($"mov r{scalarDest}, r{valueReg}");
      }
    }

    return addr.Type;
  }

  /// <summary>"target op= value" -- the target's address is resolved exactly once (see this section's
  /// own remarks), its current value loaded, combined with the right-hand side, stored back, and the
  /// NEW value (standard C semantics for a compound-assignment expression's own value) handed back to
  /// the caller.</summary>
  private CType EmitCompoundAssignInto(CCompoundAssignExpr compoundAssign, int scalarDest, int wideDest, int depth, bool isWide)
  {
    CAddr addr = ResolveAddr(compoundAssign.Target, depth);
    if (IsUnsupportedWholeValueType(addr.Type))
    {
      return ReportUnsupportedValueRead(compoundAssign.Location, addr.Type, "this assignment's target");
    }

    bool targetIsWide = addr.Type.IsWideValue;
    bool needsFreshRegister = addr.Kind == CAddrKind.Pointer;
    int baseDepth = needsFreshRegister ? depth + 1 : depth;

    // ADDED 2026-11-08: a computed pointer address stays live while the right-hand side is evaluated; spill
    // it across any call / runtime-library call there. (Also, for an operand holding a CALL, the target's
    // current value is loaded AFTER the right-hand side is evaluated -- C leaves that order unspecified --
    // so it is never held in a register across the call.)
    int? addressSpill = SpillPointerAddressIfNeeded(addr, compoundAssign.Value);
    bool valueHasCall = ContainsCall(compoundAssign.Value);

    if (!addr.Type.IsFloat && IsFloatTyped(compoundAssign.Value))
    {
      // "int op= float" would have to compute in float and convert back; write "i = i op f" instead.
      RestorePointerAddress(addr, addressSpill);
      Error(compoundAssign.Location, $"a compound assignment to \"{addr.Type}\" with a 'float' right-hand side is not supported (write \"x = x op f\" explicitly)");
      EmitCode(isWide ? $"dlit d{wideDest}, 0, 0" : $"rlit r{scalarDest}, 0");
      return addr.Type;
    }

    if (addr.Type.IsLong)
    {
      return EmitLongCompoundAssign(compoundAssign, addr, addressSpill, wideDest, baseDepth, isWide);
    }

    if (targetIsWide)
    {
      int currentDouble = WideReg(baseDepth);
      bool floatValueFirst = (valueHasCall || FloatOperandUsesLibrary(compoundAssign.Value)) && addr.Type.IsFloat && compoundAssign.Op is CBinaryOp.Add or CBinaryOp.Subtract or CBinaryOp.Multiply or CBinaryOp.Divide;
      if (!floatValueFirst)
      {
        EmitLoadValueFromAddr(addr, 0, currentDouble, baseDepth);
      }

      if (addr.Type.IsPointer && compoundAssign.Op is CBinaryOp.Add or CBinaryOp.Subtract)
      {
        int elementWords = Math.Max(addr.Type.ElementType!.SizeInWords, 1);

        // guaranteedSamePage: false, unconditionally -- currentDouble just above holds the TARGET's
        // current pointer VALUE, loaded from memory via EmitLoadValueFromAddr; that value is a general
        // runtime pointer regardless of where/what addr itself is (a bounded local/global pointer
        // VARIABLE still holds an unbounded pointer VALUE -- see IsBoundedPointerValue's own remarks).
        EmitAddIndexScaledToPointer(currentDouble, compoundAssign.Value, compoundAssign.Op == CBinaryOp.Subtract ? -elementWords : elementWords, baseDepth + 1, guaranteedSamePage: false);
        RestorePointerAddress(addr, addressSpill);
      }
      else if (addr.Type.IsFloat && compoundAssign.Op is CBinaryOp.Add or CBinaryOp.Subtract or CBinaryOp.Multiply or CBinaryOp.Divide)
      {
        int rightDouble = WideReg(baseDepth + 1);
        CType rightFloatType = EmitFloatValueInto(compoundAssign.Value, rightDouble, baseDepth + 1);
        RestorePointerAddress(addr, addressSpill);
        if (floatValueFirst)
        {
          EmitLoadValueFromAddr(addr, 0, currentDouble, baseDepth);
        }

        if (!rightFloatType.IsFloat)
        {
          Error(compoundAssign.Location, "this compound assignment needs a 'float' right-hand side");
        }

        string op = compoundAssign.Op switch
        {
          CBinaryOp.Add => "fadd",
          CBinaryOp.Subtract => "fsub",
          CBinaryOp.Multiply => "fmul",
          _ => "fdiv",
        };
        EmitCode($"{op} f{currentDouble}, f{rightDouble}"); // float instructions take floating-point registers fN (CHANGED 2026-11-08; dN is an assembler error for them)
      }
      else
      {
        RestorePointerAddress(addr, addressSpill);
        Error(compoundAssign.Location, $"this compound assignment operator is not supported for \"{addr.Type}\"");
      }

      EmitStoreValueToAddr(addr, 0, currentDouble);

      if (isWide && currentDouble != wideDest)
      {
        EmitCode($"mov r{2 * wideDest}, r{2 * currentDouble}");
        EmitCode($"mov r{2 * wideDest + 1}, r{2 * currentDouble + 1}");
      }

      return addr.Type;
    }

    int currentReg = ScalarReg(baseDepth);
    int rightReg = ScalarReg(baseDepth + 1);
    CType resultType;

    if (valueHasCall)
    {
      CType rightType = EmitIntExprInto(compoundAssign.Value, rightReg, baseDepth + 1);
      RestorePointerAddress(addr, addressSpill);
      EmitLoadValueFromAddr(addr, currentReg, 0, baseDepth);
      resultType = EmitIntBinaryOperation(compoundAssign.Op, addr.Type, rightType, currentReg, rightReg, compoundAssign.Location);
    }
    else
    {
      EmitLoadValueFromAddr(addr, currentReg, 0, baseDepth);

      // ADDED 2026-11-08: "x <<= 3", "x *= 10", "x &= 0xFF", "x += 1" ... with a constant right-hand side
      // use the same inline current-opcode sequences as the plain binary operators.
      if (!TryEmitConstantIntOperation(compoundAssign.Op, addr.Type, compoundAssign.Value, currentReg, rightReg, out resultType))
      {
        CType rightType = EmitIntExprInto(compoundAssign.Value, rightReg, baseDepth + 1);
        resultType = EmitIntBinaryOperation(compoundAssign.Op, addr.Type, rightType, currentReg, rightReg, compoundAssign.Location);
      }

      RestorePointerAddress(addr, addressSpill);
    }

    EmitStoreValueToAddr(addr, currentReg, 0);

    if (!isWide && currentReg != scalarDest)
    {
      EmitCode($"mov r{scalarDest}, r{currentReg}");
    }

    return resultType;
  }

  /// <summary>Pre/post increment/decrement on any lvalue -- int (by 1), pointer (by its own element
  /// size, via the same scaling <see cref="EmitAddIndexScaledToPointer"/> uses for "+"/"-"). 'float' is
  /// rejected (no float increment opcode, and this compiler has never been asked for one). A post-form's
  /// own value is the OLD one, captured to a frame scratch slot before the update.</summary>
  private CType EmitIncrementOrDecrement(CUnaryExpr incDec, int scalarReg, int wideDouble, int depth)
  {
    bool isIncrement = incDec.Op is CUnaryOp.PreIncrement or CUnaryOp.PostIncrement;
    bool isPost = incDec.Op is CUnaryOp.PostIncrement or CUnaryOp.PostDecrement;

    CAddr addr = ResolveAddr(incDec.Operand, depth);
    if (IsUnsupportedWholeValueType(addr.Type))
    {
      return ReportUnsupportedValueRead(incDec.Location, addr.Type, "this expression");
    }

    bool isWide = addr.Type.IsWideValue;
    if (addr.Type.IsFloat)
    {
      Error(incDec.Location, "'++'/'--' on a 'float' is not supported by this compiler (no float increment/decrement opcode exists)");
    }

    bool needsFreshRegister = addr.Kind == CAddrKind.Pointer;

    if (isWide)
    {
      int valueDouble = needsFreshRegister ? WideReg(depth + 1) : wideDouble;
      EmitLoadValueFromAddr(addr, 0, valueDouble, depth);

      int? spillLow = isPost ? AllocateFrameWords(2) : null;
      if (spillLow is not null)
      {
        EmitCode($"stf r{2 * valueDouble}, {spillLow}");
        EmitCode($"stf r{2 * valueDouble + 1}, {spillLow + 1}");
      }

      int elementWords = addr.Type.IsPointer ? Math.Max(addr.Type.ElementType!.SizeInWords, 1) : 1;

      // guaranteedSamePage: false -- valueDouble holds an arbitrary runtime pointer/float VALUE just
      // loaded from addr (EmitLoadValueFromAddr just above always populates both halves), not a
      // compile-time-bounded frame/global address, so a page-crossing ++/-- (e.g. incrementing a pointer
      // right up to the end of a page-spanning buffer) must carry into the page half correctly.
      EmitAddConstantToRegister(2 * valueDouble, isIncrement ? elementWords : -elementWords, guaranteedSamePage: false);
      EmitStoreValueToAddr(addr, 0, valueDouble);

      if (isPost && spillLow is not null)
      {
        EmitCode($"ldf r{2 * valueDouble}, {spillLow}");
        EmitCode($"ldf r{2 * valueDouble + 1}, {spillLow + 1}");
      }

      if (needsFreshRegister && valueDouble != wideDouble)
      {
        EmitCode($"mov r{2 * wideDouble}, r{2 * valueDouble}");
        EmitCode($"mov r{2 * wideDouble + 1}, r{2 * valueDouble + 1}");
      }

      return addr.Type;
    }

    int reg = needsFreshRegister ? ScalarReg(depth + 1) : scalarReg;
    EmitLoadValueFromAddr(addr, reg, 0, depth);

    int? spillSlot = isPost ? AllocateFrameWords(1) : null;
    if (spillSlot is not null)
    {
      EmitCode($"stf r{reg}, {spillSlot}");
    }

    EmitCode(isIncrement ? $"inc r{reg}" : $"dec r{reg}");
    EmitStoreValueToAddr(addr, reg, 0);

    if (isPost && spillSlot is not null)
    {
      EmitCode($"ldf r{reg}, {spillSlot}");
    }

    if (needsFreshRegister && reg != scalarReg)
    {
      EmitCode($"mov r{scalarReg}, r{reg}");
    }

    return addr.Type;
  }

  // ---------------------------------------------------------------------------------------------------
  // Comparisons and short-circuit logical operators (claude/cvm-abi.md section 3.7). `cond`/`if` only
  // expose a single-register sign-test against zero -- there is no two-operand compare opcode.
  // CHANGED 2026-11-08: == and != still lower to subtract + `cond ==0`/`!=0` (exact). The ordered
  // comparisons < <= > >= call the exact libs/cvm helpers (__lts ... __geu): "subtract, test the sign"
  // (with the unsigned 0x8000 bias) is wrong whenever the operands are 0x8000 or more apart. The one
  // inline exception is an ordered comparison against a literal 0 (EmitOrderedComparisonWithZero).

  /// <summary>Evaluates a `==`/`!=`/`&lt;`/`&gt;`/`&lt;=`/`&gt;=` comparison into 0/1 in <paramref
  /// name="reg"/>. Pointer operands compare their ADDRESS half only (same "ignore the page half"
  /// convention <see cref="EmitConditionTestInto"/> already uses for a pointer's NULL test -- this
  /// compiler never needs a true cross-page ordering). 'float' operands are rejected: no float-compare
  /// opcode exists in the final ISA.</summary>
  private void EmitComparisonInto(CBinaryExpr comparison, int reg, int depth)
  {
    TryGetStaticType(comparison.Left, out CType leftStaticType);
    TryGetStaticType(comparison.Right, out CType rightStaticType);

    if (leftStaticType.IsFloat || rightStaticType.IsFloat)
    {
      if (leftStaticType.IsPointer || rightStaticType.IsPointer)
      {
        Error(comparison.Location, "comparing a 'float' with a pointer is not supported");
        EmitCode($"rlit r{reg}, 0");
        return;
      }

      EmitFloatComparisonInto(comparison, reg, depth);
      return;
    }

    if ((leftStaticType.IsLong || rightStaticType.IsLong) && !leftStaticType.IsPointer && !rightStaticType.IsPointer)
    {
      EmitLongComparisonInto(comparison, leftStaticType, rightStaticType, reg, depth);
      return;
    }

    bool spillNeeded = ContainsCall(comparison.Right);
    bool wideSpillNeeded = MayCallOrUseLibrary(comparison.Right);
    int leftReg, rightReg;
    bool isUnsigned;

    if (leftStaticType.IsPointer || rightStaticType.IsPointer)
    {
      int? spillLow = wideSpillNeeded ? AllocateFrameWords(2) : null;
      int leftDouble = WideReg(depth);
      EmitWideExprInto(comparison.Left, leftDouble, depth);
      if (spillLow is not null)
      {
        EmitCode($"stf r{2 * leftDouble}, {spillLow}");
        EmitCode($"stf r{2 * leftDouble + 1}, {spillLow + 1}");
      }

      int rightDouble = WideReg(depth + 1);
      EmitWideExprInto(comparison.Right, rightDouble, depth + 1);
      if (spillLow is not null)
      {
        EmitCode($"ldf r{2 * leftDouble}, {spillLow}");
        EmitCode($"ldf r{2 * leftDouble + 1}, {spillLow + 1}");
      }

      leftReg = ScalarReg(depth);
      rightReg = ScalarReg(depth + 1);
      EmitCode($"mov r{leftReg}, r{2 * leftDouble}");
      EmitCode($"mov r{rightReg}, r{2 * rightDouble}");
      isUnsigned = true; // pointers compare as unsigned addresses.
    }
    else
    {
      int? spillSlot = spillNeeded ? AllocateFrameWords(1) : null;
      leftReg = reg;
      CType leftType = EmitIntExprInto(comparison.Left, leftReg, depth);
      if (spillSlot is not null)
      {
        EmitCode($"stf r{leftReg}, {spillSlot}");
      }

      // ADDED 2026-11-08: "x < 0", "x >= 0", ... against a literal zero is exact without the library (the
      // sign of x itself is the answer) and needs no second operand at all -- see EmitOrderedComparisonWithZero.
      if (IsOrderedComparison(comparison.Op)
          && TryGetSimpleConstant(comparison.Right, out long zeroCandidate, out bool zeroIsUnsigned)
          && zeroCandidate == 0)
      {
        EmitOrderedComparisonWithZero(comparison.Op, leftType.IsUnsigned || zeroIsUnsigned, leftReg);
        if (leftReg != reg)
        {
          EmitCode($"mov r{reg}, r{leftReg}");
        }

        return;
      }

      rightReg = ScalarReg(depth + 1);
      CType rightType = EmitIntExprInto(comparison.Right, rightReg, depth + 1);
      if (spillSlot is not null)
      {
        EmitCode($"ldf r{leftReg}, {spillSlot}");
      }

      isUnsigned = leftType.IsUnsigned || rightType.IsUnsigned;
    }

    // CHANGED 2026-11-08: an ORDERED comparison (< <= > >=) no longer uses "bias, subtract, test the sign".
    // That form is only exact while the operands are less than 0x8000 apart (the 16-bit `sub` wraps), so
    // e.g. unsigned 0 < 0xFFFF and signed -32768 < 32767 came out wrong. It now calls the exact helpers
    // from libs/cvm (__lts/__les/__gts/__ges signed, __ltu/__leu/__gtu/__geu unsigned) through
    // EmitLibraryBinaryCall; the 0/1 result comes back in leftReg. == and != need no helper: the
    // difference of two equal values is exactly 0 whatever the magnitude, so they keep sub + cond (and no
    // longer waste two xors on the 0x8000 bias, which equality never needed).
    if (IsOrderedComparison(comparison.Op))
    {
      EmitLibraryBinaryCall(OrderedComparisonHelper(comparison.Op, isUnsigned), leftReg, rightReg);
      if (leftReg != reg)
      {
        EmitCode($"mov r{reg}, r{leftReg}");
      }

      return;
    }

    EmitCode($"sub r{leftReg}, r{rightReg}");
    EmitCode($"cond r{leftReg}, {(comparison.Op == CBinaryOp.NotEqual ? "!=0" : "==0")}");
    EmitCode($"rpop r{reg}");
  }

  private static bool IsOrderedComparison(CBinaryOp op) =>
      op is CBinaryOp.Less or CBinaryOp.LessOrEqual or CBinaryOp.Greater or CBinaryOp.GreaterOrEqual;

  /// <summary>Name of the libs/cvm helper that computes an ordered comparison exactly.</summary>
  private static string OrderedComparisonHelper(CBinaryOp op, bool isUnsigned) => (op, isUnsigned) switch
  {
    (CBinaryOp.Less, false) => "__lts",
    (CBinaryOp.LessOrEqual, false) => "__les",
    (CBinaryOp.Greater, false) => "__gts",
    (CBinaryOp.GreaterOrEqual, false) => "__ges",
    (CBinaryOp.Less, true) => "__ltu",
    (CBinaryOp.LessOrEqual, true) => "__leu",
    (CBinaryOp.Greater, true) => "__gtu",
    _ => "__geu",
  };

  /// <summary>Ordered comparison of the value in <paramref name="reg"/> with a literal 0, result 0/1 back in
  /// <paramref name="reg"/>, no library call. Signed: the sign test of the value itself is exact (there is no
  /// subtraction to overflow). Unsigned: nothing is below zero (<c>&lt;</c> is always 0, <c>&gt;=</c> always
  /// 1), <c>&lt;=</c> means "is zero" and <c>&gt;</c> means "is not zero".</summary>
  private void EmitOrderedComparisonWithZero(CBinaryOp op, bool isUnsigned, int reg)
  {
    if (!isUnsigned)
    {
      string condition = op switch
      {
        CBinaryOp.Less => "<0",
        CBinaryOp.LessOrEqual => "<=0",
        CBinaryOp.Greater => ">0",
        _ => ">=0",
      };
      EmitCode($"cond r{reg}, {condition}");
      EmitCode($"rpop r{reg}");
      return;
    }

    switch (op)
    {
      case CBinaryOp.Less:
        EmitCode($"rlit r{reg}, 0");
        break;
      case CBinaryOp.GreaterOrEqual:
        EmitCode($"rlit r{reg}, 1");
        break;
      case CBinaryOp.LessOrEqual:
        EmitCode($"cond r{reg}, ==0");
        EmitCode($"rpop r{reg}");
        break;
      default:
        EmitCode($"cond r{reg}, !=0");
        EmitCode($"rpop r{reg}");
        break;
    }
  }

  /// <summary>Short-circuit `&amp;&amp;`/`||`, normalizing the result to a clean 0/1 in <paramref
  /// name="reg"/> either way (the left/right operands' own truth-test value need not already be exactly
  /// 1).</summary>
  private void EmitLogicalInto(CBinaryExpr logical, int reg, int depth)
  {
    bool isAnd = logical.Op == CBinaryOp.LogicalAnd;
    string shortCircuitLabel = NewLabel(isAnd ? "and_false" : "or_true");
    string endLabel = NewLabel(isAnd ? "and_end" : "or_end");

    EmitConditionTestInto(logical.Left, reg, depth);
    EmitCode($"if r{reg} {(isAnd ? "==0" : "!=0")} then {shortCircuitLabel}");

    EmitConditionTestInto(logical.Right, reg, depth);
    EmitCode($"cond r{reg}, !=0");
    EmitCode($"rpop r{reg}");
    EmitCode($"br {endLabel}");

    EmitLabel(shortCircuitLabel);
    EmitCode($"rlit r{reg}, {(isAnd ? 0 : 1)}");

    EmitLabel(endLabel);
  }

  // ---------------------------------------------------------------------------------------------------
  // Binary operator lowering, casts, the conditional operator, float literals, and function calls.

  /// <summary>Combines <paramref name="leftReg"/> := <paramref name="leftReg"/> <paramref name="op"/>
  /// <paramref name="rightReg"/> in place. `+`/`-`/`&amp;`/`|`/`^` are direct hardware opcodes; `*`/`/`/
  /// `%`/`&lt;&lt;`/`&gt;&gt;` have no hardware opcode at all (the 16-bit binary ALU row is only `or`/
  /// `and`/`xor`/`sub`/`add`/`mov`/`packbytes`, and only `mul2`/`div2`/`udiv2` -- fixed x2/÷2 -- exist for
  /// shift-like operations) and lower to a runtime library call instead -- see <see
  /// cref="EmitLibraryBinaryCall"/>'s own doc comment for the important caveat about that library.
  /// </summary>
  private CType EmitIntBinaryOperation(CBinaryOp op, CType leftType, CType rightType, int leftReg, int rightReg, CSourceLocation location)
  {
    bool isUnsigned = leftType.IsUnsigned || rightType.IsUnsigned;
    CType resultType = isUnsigned ? CType.UnsignedInt : CType.Int;

    switch (op)
    {
      case CBinaryOp.Add:
        EmitCode($"add r{leftReg}, r{rightReg}");
        return resultType;

      case CBinaryOp.Subtract:
        EmitCode($"sub r{leftReg}, r{rightReg}");
        return resultType;

      case CBinaryOp.BitwiseAnd:
        EmitCode($"and r{leftReg}, r{rightReg}");
        return resultType;

      case CBinaryOp.BitwiseOr:
        EmitCode($"or r{leftReg}, r{rightReg}");
        return resultType;

      case CBinaryOp.BitwiseXor:
        EmitCode($"xor r{leftReg}, r{rightReg}");
        return resultType;

      case CBinaryOp.Multiply:
        EmitLibraryBinaryCall("__mul", leftReg, rightReg);
        return resultType;

      case CBinaryOp.Divide:
        EmitLibraryBinaryCall(isUnsigned ? "__divu" : "__divs", leftReg, rightReg);
        return resultType;

      case CBinaryOp.Modulo:
        EmitLibraryBinaryCall(isUnsigned ? "__modu" : "__mods", leftReg, rightReg);
        return resultType;

      // CHANGED 2026-11-08: a shift's result type (and the choice between logical and arithmetic right
      // shift) follows the LEFT operand only, per C; the shift count's own signedness never matters.
      // Constant counts never reach here -- see TryEmitConstantIntOperation.
      case CBinaryOp.ShiftLeft:
        EmitLibraryBinaryCall("__shl", leftReg, rightReg);
        return leftType.IsUnsigned ? CType.UnsignedInt : CType.Int;

      case CBinaryOp.ShiftRight:
        EmitLibraryBinaryCall(leftType.IsUnsigned ? "__shru" : "__shrs", leftReg, rightReg);
        return leftType.IsUnsigned ? CType.UnsignedInt : CType.Int;

      default:
        Error(location, $"binary operator \"{op}\" is not supported by this code generator");
        return resultType;
    }
  }

  /// <summary>Library helpers (libs/cvm/asm/cvmmath.asm) declared <c>__fastcall</c> in cvm.h: they take their
  /// two arguments in r0 and r1 instead of on the stack. (<c>__udivmod</c>, <c>__cmps</c> and <c>__cmpu</c> are
  /// fastcall too but are never emitted by the compiler.)</summary>
  private static readonly HashSet<string> FastcallLibraryFunctions = new()
  {
    "__mul", "__divs", "__mods", "__divu", "__modu", "__shl", "__shrs", "__shru",
    "__lts", "__les", "__gts", "__ges", "__ltu", "__leu", "__gtu", "__geu",
  };

  /// <summary>Calls a 2-argument runtime library routine (<c>__mul</c>, <c>__divs</c>, <c>__divu</c>,
  /// <c>__mods</c>, <c>__modu</c>, <c>__shl</c>, <c>__shrs</c>, <c>__shru</c>, all hand-written CVM
  /// assembler in <c>libs/cvm/asm/cvmmath.asm</c> and declared in <c>libs/cvm/include/cvm.h</c>) (and the ordered
  /// comparison predicates <c>__lts</c> ... <c>__geu</c>). All of them are <c>__fastcall</c> today (see
  /// <see cref="FastcallLibraryFunctions"/>); a routine NOT in that set would get the ordinary stack
  /// convention (claude/cvm-abi.md section 3.5): the arguments are pushed right-to-left (so the first
  /// parameter, <paramref name="leftReg"/>, ends at the lowest frame offset +2), <c>call</c>, the caller
  /// drops both argument words, and the 1-word result comes back in <c>r0</c>.
  /// <b>REWRITTEN 2026-11-08</b>: the previous <c>rpush left; rpush right; call; rpop left</c> sequence did
  /// not match that convention (wrong push order, no <c>drop</c>s, result read from the stack instead of
  /// r0) and did not protect live registers. Because every register is call-volatile, the sequence now
  /// saves the outer scalar scratch registers <c>r8..leftReg-1</c> (which an enclosing expression may
  /// still hold) on the VM stack around the call and restores them afterwards. <c>r0</c> never holds a
  /// live partial result across a library call: an expression that can reach one is not evaluated into
  /// r0 (see <see cref="IsPlainScalarExpression"/>). WIDE registers (pointers, floats) are not saved
  /// here; their holders spill them to a frame slot instead (see <see
  /// cref="MayCallOrUseLibrary"/>).
  /// <b>2026-11-08</b>: the helpers listed in <see cref="FastcallLibraryFunctions"/> (every routine at present) are
  /// <c>__fastcall</c>: the arguments are moved into <c>r1</c> (right) and <c>r0</c> (left), there is
  /// nothing to push or drop, and the result still comes back in <c>r0</c>.</summary>
  private void EmitLibraryBinaryCall(string functionName, int leftReg, int rightReg)
  {
    _imports.Add(functionName);

    var saved = new List<int>();
    for (int r = ScalarReg(0); r < leftReg; r++)
    {
      saved.Add(r);
    }

    foreach (int r in saved)
    {
      EmitCode($"rpush r{r}");
    }

    if (FastcallLibraryFunctions.Contains(functionName))
    {
      // __fastcall helper: first argument in r0, second in r1, no stack traffic and nothing to drop.
      // leftReg/rightReg are scalar scratch registers (r8 and up), so the two moves cannot collide.
      EmitCode($"mov r{ScalarParamFirstRegister + 1}, r{rightReg}");
      EmitCode($"mov r{ScalarParamFirstRegister}, r{leftReg}");
      EmitCode($"call {functionName}");
    }
    else
    {
      EmitCode($"rpush r{rightReg}");
      EmitCode($"rpush r{leftReg}");
      EmitCode($"call {functionName}");
      EmitCode("drop");
      EmitCode("drop");
    }

    if (leftReg != ScalarReturnRegister)
    {
      EmitCode($"mov r{leftReg}, r{ScalarReturnRegister}");
    }

    for (int k = saved.Count - 1; k >= 0; k--)
    {
      EmitCode($"rpop r{saved[k]}");
    }
  }

  // ---------------------------------------------------------------------------------------------------
  // Constant-operand strength reduction. ADDED 2026-11-08, to use the 16-bit unary opcodes that exist in
  // the CURRENT CvmInstructionSet (mul2 / udiv2 / div2 / highbyte / lowbyte / byteswap / mask15 / inv15 /
  // inc / dec / neg / inv) instead of a runtime-library call or an `rlit` + binary-op pair. Everything in
  // this section only fires when the RIGHT operand is a compile-time constant; a non-constant operand
  // still takes the ordinary path (`add`/`sub`/... or the `__mul`/`__divs`/... library calls).
  //
  // Semantics relied on (claude/cvm-16bit-ops-test.md, node 206 source): mul2 = x << 1, udiv2 = x >> 1
  // logical, div2 = x >> 1 arithmetic (sign kept), highbyte = x >> 8 logical, lowbyte = x & 0xFF,
  // byteswap = swap the two bytes, mask15 = x & 0x7FFF, inv15 = x ^ 0x8000; all mod 0x10000.

  /// <summary>Largest instruction count <see cref="TryEmitMultiplyByConstant"/> will spend on one
  /// constant multiply before it gives up and lets the caller use the library call instead.</summary>
  private const int MaxInlineMultiplyInstructions = 12;

  /// <summary>Recognizes the few constant shapes the code generator can strength-reduce. Deliberately
  /// narrow (a literal, a signed literal, <c>sizeof(type)</c>) -- anything more elaborate has already been
  /// folded to a literal by <see cref="CConstantFolder"/>, or simply takes the ordinary path.</summary>
  private static bool TryGetSimpleConstant(CExpr expr, out long value, out bool isUnsigned)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        value = literal.Value;
        isUnsigned = literal.IsUnsigned;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Minus, Operand: CIntLiteralExpr inner }:
        value = -inner.Value;
        isUnsigned = inner.IsUnsigned;
        return true;

      case CUnaryExpr { Op: CUnaryOp.Plus, Operand: CIntLiteralExpr inner }:
        value = inner.Value;
        isUnsigned = inner.IsUnsigned;
        return true;

      case CSizeOfTypeExpr sizeOfType:
        value = sizeOfType.Type.SizeInWords;
        isUnsigned = true; // sizeof yields CType.UnsignedInt in EmitIntExprInto, same here.
        return true;

      default:
        value = 0;
        isUnsigned = false;
        return false;
    }
  }

  private static bool IsPowerOfTwo(long value) => value > 0 && (value & (value - 1)) == 0;

  private static int Log2(long powerOfTwo) => System.Numerics.BitOperations.Log2((ulong)powerOfTwo);

  /// <summary>Emits <c>reg &lt;&lt;= count</c> for 0 &lt;= count &lt;= 15 with no library call: <c>count</c>
  /// copies of <c>mul2</c> for count &lt; 8, and for count &gt;= 8 the byte route -- <c>lowbyte</c> +
  /// <c>byteswap</c> is exactly <c>(x &amp; 0xFF) &lt;&lt; 8</c> -- followed by <c>count - 8</c>
  /// <c>mul2</c>. Counts of 16 and above (undefined behaviour in C) produce 0.</summary>
  private void EmitShiftLeftByConstant(int reg, int count)
  {
    if (count >= 16)
    {
      EmitCode($"rlit r{reg}, 0");
      return;
    }

    int remaining = count;
    if (count >= 8)
    {
      EmitCode($"lowbyte r{reg}");
      EmitCode($"byteswap r{reg}");
      remaining = count - 8;
    }

    for (int i = 0; i < remaining; i++)
    {
      EmitCode($"mul2 r{reg}");
    }
  }

  /// <summary>Emits <c>reg &gt;&gt;= count</c> with no library call. Unsigned: <c>udiv2</c> per bit, or
  /// <c>highbyte</c> (x &gt;&gt; 8) first for count &gt;= 8; 16 and above gives 0. Signed (arithmetic shift):
  /// <c>div2</c> per bit; a shift of 15 or more is pure sign fill and is done as
  /// <c>cond r, &lt;0</c> / <c>rpop r</c> / <c>neg r</c> (1 or 0, negated to -1 or 0).</summary>
  private void EmitShiftRightByConstant(int reg, int count, bool isUnsigned)
  {
    if (isUnsigned)
    {
      if (count >= 16)
      {
        EmitCode($"rlit r{reg}, 0");
        return;
      }

      int remaining = count;
      if (count >= 8)
      {
        EmitCode($"highbyte r{reg}");
        remaining = count - 8;
      }

      for (int i = 0; i < remaining; i++)
      {
        EmitCode($"udiv2 r{reg}");
      }

      return;
    }

    if (count >= 15)
    {
      EmitCode($"cond r{reg}, <0");
      EmitCode($"rpop r{reg}");
      EmitCode($"neg r{reg}");
      return;
    }

    for (int i = 0; i < count; i++)
    {
      EmitCode($"div2 r{reg}");
    }
  }

  /// <summary>Digits (0/1, least significant first) of a positive value.</summary>
  private static int[] BinaryDigits(long value)
  {
    var digits = new List<int>();
    for (long v = value; v > 0; v >>= 1)
    {
      digits.Add((int)(v & 1));
    }

    return [.. digits];
  }

  /// <summary>Non-adjacent-form digits (-1/0/1, least significant first) of a positive value: no two
  /// adjacent digits are non-zero, which minimizes the number of non-zero digits.</summary>
  private static int[] NonAdjacentFormDigits(long value)
  {
    var digits = new List<int>();
    for (long v = value; v > 0; v >>= 1)
    {
      int digit = 0;
      if ((v & 1) != 0)
      {
        digit = 2 - (int)(v & 3); // v mod 4 == 1 -> +1, == 3 -> -1
        v -= digit;
      }

      digits.Add(digit);
    }

    return [.. digits];
  }

  /// <summary>Instruction count of the Horner sequence for <paramref name="digits"/>: one <c>mov</c>
  /// (save x), one <c>mul2</c> per digit below the top one, one <c>add</c>/<c>sub</c> per non-zero digit
  /// below the top one.</summary>
  private static int MultiplyCost(int[] digits)
  {
    int nonZeroBelowTop = 0;
    for (int i = 0; i < digits.Length - 1; i++)
    {
      if (digits[i] != 0)
      {
        nonZeroBelowTop++;
      }
    }

    return 1 + (digits.Length - 1) + nonZeroBelowTop;
  }

  /// <summary>
  /// Tries to emit <c>reg *= constant</c> inline and returns whether it did. A power of two is a shift
  /// (<see cref="EmitShiftLeftByConstant"/>). Any other constant uses Horner's scheme on a signed-digit
  /// expansion (binary or non-adjacent form, whichever is cheaper): copy x into <paramref name="scratchReg"/>,
  /// then for every digit below the top one <c>mul2</c> the accumulator and, when that digit is +1/-1,
  /// <c>add</c>/<c>sub</c> the saved x -- e.g. x*3 = <c>mov t,x; mul2 x; add x,t</c>, x*255 = x*256 - x. A negative constant multiplies by its magnitude and then <c>neg</c>s.
  /// Gives up (returns false, emits nothing) when the sequence would exceed <see
  /// cref="MaxInlineMultiplyInstructions"/>. All arithmetic is mod 0x10000, so signedness is irrelevant.
  /// </summary>
  private bool TryEmitMultiplyByConstant(int reg, long constant, int scratchReg)
  {
    if (!MultiplyIsInline(constant))
    {
      return false;
    }

    bool negate = constant < 0;
    long magnitude = Math.Abs(constant) & 0xFFFF;

    if (magnitude == 0)
    {
      EmitCode($"rlit r{reg}, 0");
      return true;
    }

    if (IsPowerOfTwo(magnitude))
    {
      EmitShiftLeftByConstant(reg, Log2(magnitude));
    }
    else
    {
      int[] digits = CheaperMultiplyDigits(magnitude);
      EmitCode($"mov r{scratchReg}, r{reg}");
      for (int bit = digits.Length - 2; bit >= 0; bit--)
      {
        EmitCode($"mul2 r{reg}");
        if (digits[bit] > 0)
        {
          EmitCode($"add r{reg}, r{scratchReg}");
        }
        else if (digits[bit] < 0)
        {
          EmitCode($"sub r{reg}, r{scratchReg}");
        }
      }
    }

    if (negate)
    {
      EmitCode($"neg r{reg}");
    }

    return true;
  }

  /// <summary>Both the plain binary expansion (digits 0/1) and the non-adjacent form (digits -1/0/1, fewer
  /// non-zero digits for runs of ones: 255 = 256 - 1) are costed; the cheaper wins. Digit 0 is the least
  /// significant; the top digit is +1.</summary>
  private static int[] CheaperMultiplyDigits(long magnitude)
  {
    int[] binaryDigits = BinaryDigits(magnitude);
    int[] nafDigits = NonAdjacentFormDigits(magnitude);
    return MultiplyCost(nafDigits) < MultiplyCost(binaryDigits) ? nafDigits : binaryDigits;
  }

  /// <summary>Whether <c>x * constant</c> is emitted inline (no library call) by <see
  /// cref="TryEmitMultiplyByConstant"/>. Pure function of the constant, so callers can ask it at analysis
  /// time (see <see cref="MayCallOrUseLibrary"/>).</summary>
  private static bool MultiplyIsInline(long constant)
  {
    bool negate = constant < 0;
    long magnitude = Math.Abs(constant) & 0xFFFF;
    if (magnitude == 0)
    {
      return true;
    }

    if (IsPowerOfTwo(magnitude))
    {
      int shift = Log2(magnitude);
      int shiftCost = shift >= 8 ? 2 + (shift - 8) : shift;
      return shiftCost + (negate ? 1 : 0) <= MaxInlineMultiplyInstructions;
    }

    return MultiplyCost(CheaperMultiplyDigits(magnitude)) + (negate ? 1 : 0) <= MaxInlineMultiplyInstructions;
  }

  /// <summary>Inline <c>reg *= constant</c> if <see cref="TryEmitMultiplyByConstant"/> can; otherwise the
  /// ordinary <c>__mul</c> library call (constant loaded into <paramref name="scratchReg"/>).</summary>
  private void EmitMultiplyRegisterByConstant(int reg, long constant, int scratchReg)
  {
    if (TryEmitMultiplyByConstant(reg, constant, scratchReg))
    {
      return;
    }

    EmitCode($"rlit r{scratchReg}, {constant}");
    EmitLibraryBinaryCall("__mul", reg, scratchReg);
  }

  /// <summary>
  /// Strength-reduces <c>reg = reg op constant</c> when <paramref name="rightExpr"/> is a simple
  /// compile-time constant and <paramref name="op"/> has a cheaper current-opcode form than "load the
  /// constant, then use the generic binary op / library call". Returns true (and the C result type)
  /// when it emitted the operation; returns false and emits NOTHING otherwise, so the caller must then
  /// evaluate the right operand and use <see cref="EmitIntBinaryOperation"/> as before.
  /// <para><paramref name="scratchReg"/> must be a register that is free for the duration of the
  /// operation (callers pass <c>ScalarReg(depth + 1)</c>, the slot the right operand would have used).</para>
  /// <list type="bullet">
  /// <item><description><c>+ k</c> / <c>- k</c> for |k| &lt;= 2: <c>inc</c>/<c>dec</c> (1 word each, versus
  /// a 2-word <c>rlit</c> plus the add).</description></item>
  /// <item><description><c>&amp; 0xFF</c> -&gt; <c>lowbyte</c>; <c>&amp; 0x7FFF</c> -&gt; <c>mask15</c>;
  /// <c>^ 0x8000</c> -&gt; <c>inv15</c>; <c>^ -1</c> -&gt; <c>inv</c>; and the identities/zeroes (<c>&amp; 0</c>,
  /// <c>| 0</c>, <c>^ 0</c>, <c>&amp; -1</c>, <c>| -1</c>).</description></item>
  /// <item><description><c>* k</c>: <see cref="TryEmitMultiplyByConstant"/>.</description></item>
  /// <item><description>UNSIGNED <c>/ 2^n</c> (a logical right shift) and UNSIGNED <c>% 2^n</c>
  /// (a mask: <c>lowbyte</c> for 256, <c>mask15</c> for 32768, else <c>rlit</c>+<c>and</c>). Signed
  /// division/modulo are NOT shifts/masks for negative dividends and keep the library call.</description></item>
  /// <item><description><c>&lt;&lt; k</c> / <c>&gt;&gt; k</c> for a constant 0..255 count (the C rule: the
  /// result has the type and signedness of the LEFT operand only).</description></item>
  /// </list>
  /// </summary>
  private bool TryEmitConstantIntOperation(CBinaryOp op, CType leftType, CExpr rightExpr, int reg, int scratchReg, out CType resultType)
  {
    resultType = CType.Int;
    if (!TryGetSimpleConstant(rightExpr, out long constant, out bool rightIsUnsigned))
    {
      return false;
    }

    bool isUnsigned = leftType.IsUnsigned || rightIsUnsigned;
    resultType = isUnsigned ? CType.UnsignedInt : CType.Int;
    bool fitsWord = constant >= -32768 && constant <= 65535;

    switch (op)
    {
      case CBinaryOp.Add when fitsWord && Math.Abs(constant) <= 2:
      case CBinaryOp.Subtract when fitsWord && Math.Abs(constant) <= 2:
        {
          long delta = op == CBinaryOp.Add ? constant : -constant;
          string mnemonic = delta >= 0 ? "inc" : "dec";
          for (long i = 0; i < Math.Abs(delta); i++)
          {
            EmitCode($"{mnemonic} r{reg}");
          }

          return true;
        }

      case CBinaryOp.BitwiseAnd when fitsWord:
        switch (constant & 0xFFFF)
        {
          case 0x0000:
            EmitCode($"rlit r{reg}, 0");
            return true;
          case 0xFFFF:
            return true;
          case 0x00FF:
            EmitCode($"lowbyte r{reg}");
            return true;
          case 0x7FFF:
            EmitCode($"mask15 r{reg}");
            return true;
          default:
            return false;
        }

      case CBinaryOp.BitwiseOr when fitsWord:
        switch (constant & 0xFFFF)
        {
          case 0x0000:
            return true;
          case 0xFFFF:
            EmitCode($"rlit r{reg}, -1");
            return true;
          default:
            return false;
        }

      case CBinaryOp.BitwiseXor when fitsWord:
        switch (constant & 0xFFFF)
        {
          case 0x0000:
            return true;
          case 0xFFFF:
            EmitCode($"inv r{reg}");
            return true;
          case 0x8000:
            EmitCode($"inv15 r{reg}");
            return true;
          default:
            return false;
        }

      case CBinaryOp.Multiply when fitsWord:
        return TryEmitMultiplyByConstant(reg, constant, scratchReg);

      case CBinaryOp.Divide when isUnsigned && IsPowerOfTwo(constant) && constant <= 0x8000:
        EmitShiftRightByConstant(reg, Log2(constant), isUnsigned: true);
        return true;

      case CBinaryOp.Modulo when isUnsigned && IsPowerOfTwo(constant) && constant <= 0x8000:
        {
          long mask = constant - 1;
          if (mask == 0)
          {
            EmitCode($"rlit r{reg}, 0");
          }
          else if (mask == 0xFF)
          {
            EmitCode($"lowbyte r{reg}");
          }
          else if (mask == 0x7FFF)
          {
            EmitCode($"mask15 r{reg}");
          }
          else
          {
            EmitCode($"rlit r{scratchReg}, {mask}");
            EmitCode($"and r{reg}, r{scratchReg}");
          }

          return true;
        }

      case CBinaryOp.ShiftLeft when constant >= 0 && constant <= 255:
        resultType = leftType.IsUnsigned ? CType.UnsignedInt : CType.Int;
        EmitShiftLeftByConstant(reg, (int)Math.Min(constant, 16));
        return true;

      case CBinaryOp.ShiftRight when constant >= 0 && constant <= 255:
        resultType = leftType.IsUnsigned ? CType.UnsignedInt : CType.Int;
        EmitShiftRightByConstant(reg, (int)Math.Min(constant, 16), leftType.IsUnsigned);
        return true;

      default:
        return false;
    }
  }

  /// <summary>`(int)`/`(char)`/`(unsigned ...)`/pointer-to-int casts. Every integer kind is exactly one
  /// CVM word (this class's own top doc comment / <see cref="CType"/>'s own remarks), so int/char/
  /// unsigned casts are a free register reinterpretation; a pointer operand is narrowed to its own
  /// address half (the page is discarded, same "address half only" convention used throughout this
  /// file). A 'float' operand is rejected -- no int&lt;-&gt;float conversion opcode exists.</summary>
  private CType EmitIntCast(CCastExpr cast, int reg, int depth)
  {
    TryGetStaticType(cast.Operand, out CType operandType);

    if (operandType.IsFloat)
    {
      // ADDED 2026-11-08: float -> integer truncates toward zero (__ftol / __ftoul, low word kept).
      EmitFloatTruncatedInto(cast.Operand, reg, depth, cast.TargetType.IsUnsigned);
      return cast.TargetType;
    }

    if (operandType.IsPointer)
    {
      int pointerDouble = WideReg(depth);
      EmitWideExprInto(cast.Operand, pointerDouble, depth);
      EmitCode($"mov r{reg}, r{2 * pointerDouble}");
      return cast.TargetType;
    }

    EmitIntExprInto(cast.Operand, reg, depth);
    return cast.TargetType;
  }

  // ---------------------------------------------------------------------------------------------------
  // long / unsigned long (ADDED 2026-11-08, see claude/cvm-long-support-2026-11-08.md).
  //
  // A long is a 2-word little-endian value (low word at the lower address / even register, high word at the
  // higher address / odd register) that lives in a wide scratch double register (d1..d3) while it is being
  // computed, exactly like a pointer or a float. Operators:
  //   + - & | ^ ~ unary-minus ++ --   inline 32-bit opcodes (dadd dneg dinv dand dor dxor dinc ddec); a
  //                                   subtraction is "dneg right; dadd left, right".
  //   int -> long                     mov + dsext (signed source) or mov + "high word := 0" (unsigned source).
  //   long -> int                     the low word.
  //   * / % << >> < <= > >=           calls into the cvm library (__lmul, __ldivs, __ldivu, __lmods, __lmodu,
  //                                   __lshl, __lshrs, __lshru, __llts ... __lgeu). They are __fastcall: first
  //                                   long in d4 (r8/r9), second long in d5 (r10/r11), an int shift count in r0;
  //                                   a long result comes back in d0 (r0/r1), a comparison result in r0.
  //   == !=                           inline (xor both word pairs, or them together, test for zero).
  // Every register is call-volatile, so a library call saves the outer scalar scratch registers
  // (r8..ScalarReg(depth)-1) with rpush/rpop, and a holder of a live wide value spills it around any operand
  // that may call or use a library routine (MayCallOrUseLibrary), exactly as for float.

  /// <summary>Emits <c>dlit</c> for a 32-bit constant (the value's two's complement bits; operand order is
  /// high word first, like every other <c>dlit</c> in this file).</summary>
  private void EmitLongConstantInto(long value, int destDouble)
  {
    uint bits = unchecked((uint)value);
    EmitCode($"dlit d{destDouble}, 0x{bits >> 16:X4}, 0x{bits & 0xFFFF:X4}");
  }

  /// <summary>Evaluates <paramref name="expr"/> as a long into <paramref name="destDouble"/>, converting an
  /// integer expression (int, unsigned, char) the way C does: a signed source is sign-extended, an unsigned
  /// one zero-extended. Returns <c>long</c>/<c>unsigned long</c> according to the SOURCE type.</summary>
  private CType EmitLongValueInto(CExpr expr, int destDouble, int depth)
  {
    if (TryGetSimpleConstant(expr, out long constant, out bool constantIsUnsigned))
    {
      EmitLongConstantInto(constant, destDouble);
      bool constantIsLong = expr is CIntLiteralExpr { IsLong: true } || expr is CUnaryExpr { Operand: CIntLiteralExpr { IsLong: true } };
      return constantIsUnsigned && constantIsLong ? CType.UnsignedLong : constantIsUnsigned && constant > 0x7FFFFFFF ? CType.UnsignedLong : CType.Long;
    }

    TryGetStaticType(expr, out CType type);
    if (type.IsLong)
    {
      return EmitWideExprInto(expr, destDouble, depth);
    }

    if (type.IsFloat)
    {
      return EmitFloatToLongInto(expr, destDouble, depth, false);
    }

    if (type.IsPointer)
    {
      Error(expr.Location, $"cannot convert \"{type}\" to a long (cast explicitly)");
      EmitWideExprInto(expr, destDouble, depth);
      return CType.Long;
    }

    int sourceReg = ScalarReg(depth);
    CType sourceType = EmitIntExprInto(expr, sourceReg, depth);
    EmitCode($"mov r{2 * destDouble}, r{sourceReg}");
    if (sourceType.IsUnsigned)
    {
      EmitCode($"rlit r{2 * destDouble + 1}, 0");
      return CType.Long;
    }

    EmitCode($"dsext d{destDouble}");
    return CType.Long;
  }

  /// <summary>A long-typed expression used where a 16-bit int is wanted: evaluated as a long, low word kept.</summary>
  private CType EmitLongTruncatedInto(CExpr expr, int reg, int depth)
  {
    if (expr is CIntLiteralExpr { IsLong: true } literal)
    {
      EmitCode($"rlit r{reg}, {literal.Value & 0xFFFF}");
      return literal.IsUnsigned ? CType.UnsignedInt : CType.Int;
    }

    int wide = WideReg(depth);
    CType type = EmitWideExprInto(expr, wide, depth);
    EmitCode($"mov r{reg}, r{2 * wide}");
    return type.IsUnsigned ? CType.UnsignedInt : CType.Int;
  }

  private CType EmitLongUnary(CUnaryExpr unary, int destDouble, int depth)
  {
    CType type = EmitLongValueInto(unary.Operand, destDouble, depth);
    EmitCode(unary.Op == CUnaryOp.Minus ? $"dneg d{destDouble}" : $"dinv d{destDouble}");
    return type;
  }

  /// <summary>A binary arithmetic / bitwise / shift expression whose promoted type is long.</summary>
  private CType EmitLongBinary(CBinaryExpr binary, int destDouble, int depth)
  {
    TryGetStaticType(binary, out CType resultType);
    bool isShift = binary.Op is CBinaryOp.ShiftLeft or CBinaryOp.ShiftRight;
    bool spillNeeded = MayCallOrUseLibrary(binary.Right);
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;

    EmitLongValueInto(binary.Left, destDouble, depth);
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * destDouble}, {spillLow}");
      EmitCode($"stf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    if (isShift)
    {
      int countReg = ScalarReg(depth + 1);
      EmitIntExprInto(binary.Right, countReg, depth + 1);
      if (spillLow is not null)
      {
        EmitCode($"ldf r{2 * destDouble}, {spillLow}");
        EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
      }

      EmitLongBinaryOperation(binary.Op, resultType.Kind == CTypeKind.UnsignedLong, destDouble, -1, countReg, depth);
      return resultType;
    }

    int rightDouble = WideReg(depth + 1);
    EmitLongValueInto(binary.Right, rightDouble, depth + 1);
    if (spillLow is not null)
    {
      EmitCode($"ldf r{2 * destDouble}, {spillLow}");
      EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    EmitLongBinaryOperation(binary.Op, resultType.Kind == CTypeKind.UnsignedLong, destDouble, rightDouble, -1, depth);
    return resultType;
  }

  /// <summary><paramref name="leftDouble"/> := <paramref name="leftDouble"/> <paramref name="op"/> right, where
  /// the right operand is the long in <paramref name="rightDouble"/> or (for a shift) the int in
  /// <paramref name="countReg"/>. <paramref name="isUnsigned"/> selects the unsigned library routine for
  /// <c>/</c>, <c>%</c> and <c>&gt;&gt;</c>.</summary>
  private void EmitLongBinaryOperation(CBinaryOp op, bool isUnsigned, int leftDouble, int rightDouble, int countReg, int depth)
  {
    switch (op)
    {
      case CBinaryOp.Add:
        EmitCode($"dadd d{leftDouble}, d{rightDouble}");
        break;
      case CBinaryOp.Subtract:
        EmitCode($"dneg d{rightDouble}");
        EmitCode($"dadd d{leftDouble}, d{rightDouble}");
        break;
      case CBinaryOp.BitwiseAnd:
        EmitCode($"dand d{leftDouble}, d{rightDouble}");
        break;
      case CBinaryOp.BitwiseOr:
        EmitCode($"dor d{leftDouble}, d{rightDouble}");
        break;
      case CBinaryOp.BitwiseXor:
        EmitCode($"dxor d{leftDouble}, d{rightDouble}");
        break;
      case CBinaryOp.Multiply:
        EmitLongLibraryCall("__lmul", leftDouble, rightDouble, -1, depth, -1);
        break;
      case CBinaryOp.Divide:
        EmitLongLibraryCall(isUnsigned ? "__ldivu" : "__ldivs", leftDouble, rightDouble, -1, depth, -1);
        break;
      case CBinaryOp.Modulo:
        EmitLongLibraryCall(isUnsigned ? "__lmodu" : "__lmods", leftDouble, rightDouble, -1, depth, -1);
        break;
      case CBinaryOp.ShiftLeft:
        EmitLongLibraryCall("__lshl", leftDouble, -1, countReg, depth, -1);
        break;
      case CBinaryOp.ShiftRight:
        EmitLongLibraryCall(isUnsigned ? "__lshru" : "__lshrs", leftDouble, -1, countReg, depth, -1);
        break;
      default:
        _diagnostics.Add($"<internal>: operator {op} is not defined for long");
        _hasError = true;
        break;
    }
  }

  /// <summary>Calls a __fastcall cvm library routine on long operands: the left long goes to d4 (r8/r9), the
  /// right long (if any) to d5 (r10/r11), an int count (if any) to r0. The result is a long copied back into
  /// <paramref name="leftDouble"/> (<paramref name="scalarResultReg"/> &lt; 0) or an int 0/1 or other word
  /// copied into <paramref name="scalarResultReg"/>. The outer scalar scratch registers r8..ScalarReg(depth)-1
  /// are saved around the call (the library freely clobbers every register).</summary>
  private void EmitLongLibraryCall(string functionName, int leftDouble, int rightDouble, int countReg, int depth, int scalarResultReg)
  {
    _imports.Add(functionName);

    var saved = new List<int>();
    for (int r = ScalarScratchFirstRegister; r < ScalarScratchFirstRegister + depth; r++)
    {
      saved.Add(r);
    }

    foreach (int r in saved)
    {
      EmitCode($"rpush r{r}");
    }

    if (countReg >= 0)
    {
      // First: the count register may be one of r8..r11, which the long arguments overwrite below.
      EmitCode($"mov r{ScalarParamFirstRegister}, r{countReg}");
    }

    EmitCode($"mov r{2 * WideParamFirstDouble}, r{2 * leftDouble}");
    EmitCode($"mov r{2 * WideParamFirstDouble + 1}, r{2 * leftDouble + 1}");
    if (rightDouble >= 0)
    {
      EmitCode($"mov r{2 * (WideParamFirstDouble + 1)}, r{2 * rightDouble}");
      EmitCode($"mov r{2 * (WideParamFirstDouble + 1) + 1}, r{2 * rightDouble + 1}");
    }

    EmitCode($"call {functionName}");

    if (scalarResultReg >= 0)
    {
      EmitCode($"mov r{scalarResultReg}, r{ScalarReturnRegister}");
    }
    else
    {
      EmitCode($"mov r{2 * leftDouble}, r{2 * WideReturnDouble}");
      EmitCode($"mov r{2 * leftDouble + 1}, r{2 * WideReturnDouble + 1}");
    }

    for (int k = saved.Count - 1; k >= 0; k--)
    {
      EmitCode($"rpop r{saved[k]}");
    }
  }

  private static string LongComparisonHelper(CBinaryOp op, bool isUnsigned) => (op, isUnsigned) switch
  {
    (CBinaryOp.Less, false) => "__llts",
    (CBinaryOp.LessOrEqual, false) => "__lles",
    (CBinaryOp.Greater, false) => "__lgts",
    (CBinaryOp.GreaterOrEqual, false) => "__lges",
    (CBinaryOp.Less, true) => "__lltu",
    (CBinaryOp.LessOrEqual, true) => "__lleu",
    (CBinaryOp.Greater, true) => "__lgtu",
    _ => "__lgeu",
  };

  /// <summary>A comparison with a long on at least one side: both operands are converted to long. <c>==</c>
  /// and <c>!=</c> are inline; the ordered comparisons call the exact library predicates.</summary>
  private void EmitLongComparisonInto(CBinaryExpr comparison, CType leftStaticType, CType rightStaticType, int reg, int depth)
  {
    bool isUnsigned = leftStaticType.Kind == CTypeKind.UnsignedLong || rightStaticType.Kind == CTypeKind.UnsignedLong;
    bool spillNeeded = MayCallOrUseLibrary(comparison.Right);
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;

    int leftDouble = WideReg(depth);
    EmitLongValueInto(comparison.Left, leftDouble, depth);
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * leftDouble}, {spillLow}");
      EmitCode($"stf r{2 * leftDouble + 1}, {spillLow + 1}");
    }

    int rightDouble = WideReg(depth + 1);
    EmitLongValueInto(comparison.Right, rightDouble, depth + 1);
    if (spillLow is not null)
    {
      EmitCode($"ldf r{2 * leftDouble}, {spillLow}");
      EmitCode($"ldf r{2 * leftDouble + 1}, {spillLow + 1}");
    }

    if (IsOrderedComparison(comparison.Op))
    {
      EmitLongLibraryCall(LongComparisonHelper(comparison.Op, isUnsigned), leftDouble, rightDouble, -1, depth, reg);
      return;
    }

    int helperReg = ScalarReg(depth + 1);
    EmitCode($"mov r{reg}, r{2 * leftDouble}");
    EmitCode($"xor r{reg}, r{2 * rightDouble}");
    EmitCode($"mov r{helperReg}, r{2 * leftDouble + 1}");
    EmitCode($"xor r{helperReg}, r{2 * rightDouble + 1}");
    EmitCode($"or r{reg}, r{helperReg}");
    EmitCode($"cond r{reg}, {(comparison.Op == CBinaryOp.NotEqual ? "!=0" : "==0")}");
    EmitCode($"rpop r{reg}");
  }

  /// <summary>"target op= value" where the target is a long. The target's current value is loaded into the
  /// wide scratch register of <paramref name="baseDepth"/>, combined with the converted right-hand side, and
  /// stored back.</summary>
  private CType EmitLongCompoundAssign(CCompoundAssignExpr compoundAssign, CAddr addr, int? addressSpill, int wideDest, int baseDepth, bool isWide)
  {
    int currentDouble = WideReg(baseDepth);
    EmitLoadValueFromAddr(addr, 0, currentDouble, baseDepth);

    bool isShift = compoundAssign.Op is CBinaryOp.ShiftLeft or CBinaryOp.ShiftRight;
    bool spillNeeded = MayCallOrUseLibrary(compoundAssign.Value);
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * currentDouble}, {spillLow}");
      EmitCode($"stf r{2 * currentDouble + 1}, {spillLow + 1}");
    }

    bool isUnsigned = addr.Type.Kind == CTypeKind.UnsignedLong;
    if (isShift)
    {
      int countReg = ScalarReg(baseDepth + 1);
      EmitIntExprInto(compoundAssign.Value, countReg, baseDepth + 1);
      RestorePointerAddress(addr, addressSpill);
      if (spillLow is not null)
      {
        EmitCode($"ldf r{2 * currentDouble}, {spillLow}");
        EmitCode($"ldf r{2 * currentDouble + 1}, {spillLow + 1}");
      }

      EmitLongBinaryOperation(compoundAssign.Op, isUnsigned, currentDouble, -1, countReg, baseDepth);
    }
    else
    {
      int rightDouble = WideReg(baseDepth + 1);
      CType valueType = EmitLongValueInto(compoundAssign.Value, rightDouble, baseDepth + 1);
      RestorePointerAddress(addr, addressSpill);
      if (spillLow is not null)
      {
        EmitCode($"ldf r{2 * currentDouble}, {spillLow}");
        EmitCode($"ldf r{2 * currentDouble + 1}, {spillLow + 1}");
      }

      EmitLongBinaryOperation(compoundAssign.Op, isUnsigned || valueType.Kind == CTypeKind.UnsignedLong, currentDouble, rightDouble, -1, baseDepth);
    }

    EmitStoreValueToAddr(addr, 0, currentDouble);
    if (isWide && currentDouble != wideDest)
    {
      EmitCode($"mov r{2 * wideDest}, r{2 * currentDouble}");
      EmitCode($"mov r{2 * wideDest + 1}, r{2 * currentDouble + 1}");
    }

    return addr.Type;
  }

  /// <summary>`(float)`/pointer-type casts. float-to-float and pointer-to-pointer are free
  /// reinterpretations; int-to-pointer assumes page 0 (there is no general way to recover a page number
  /// from a plain integer -- flagged as an assumption, not a confirmed rule, same as this class's other
  /// documented gaps) except the exact literal 0, which <see cref="EmitWideExprInto"/>'s own null-
  /// pointer-literal case already turns into a true NULL. int&lt;-&gt;float and float-to-pointer are
  /// rejected.</summary>
  private CType EmitWideCast(CCastExpr cast, int destDouble, int depth)
  {
    TryGetStaticType(cast.Operand, out CType operandType);

    if (cast.TargetType.IsLong && operandType.IsFloat)
    {
      EmitFloatToLongInto(cast.Operand, destDouble, depth, cast.TargetType.Kind == CTypeKind.UnsignedLong);
      return cast.TargetType;
    }

    if (cast.TargetType.IsLong)
    {
      // ADDED 2026-11-08: (long) / (unsigned long) of an integer (sign- or zero-extended according to the
      // operand's own signedness) or of another long (a plain reinterpretation of the same 32 bits).
      if (!operandType.IsIntegral)
      {
        Error(cast.Location, $"casting \"{operandType}\" to \"{cast.TargetType}\" is not supported by this compiler");
      }

      EmitLongValueInto(cast.Operand, destDouble, depth);
      return cast.TargetType;
    }

    if (cast.TargetType.IsFloat)
    {
      // (float) of an integer / long (__ltof / __ultof, or folded for a constant) or of another float.
      EmitFloatValueInto(cast.Operand, destDouble, depth);
      return cast.TargetType;
    }

    if (operandType.IsPointer)
    {
      EmitWideExprInto(cast.Operand, destDouble, depth);
      return cast.TargetType;
    }

    if (operandType.IsFloat)
    {
      Error(cast.Location, "casting a 'float' to a pointer type is not supported by this compiler");
      EmitCode($"dlit d{destDouble}, 0, 0");
      return cast.TargetType;
    }

    int addressReg = ScalarReg(depth);
    EmitIntExprInto(cast.Operand, addressReg, depth);
    EmitCode($"rlit r{2 * destDouble + 1}, 0"); // assumed page 0 -- see this method's own doc comment.
    EmitCode($"mov r{2 * destDouble}, r{addressReg}");
    return cast.TargetType;
  }

  /// <summary>Pointer arithmetic (`pointer + int`, `int + pointer`, `pointer - int`) and float `+`/`-`,
  /// the two cases <see cref="EmitWideExprInto"/>'s own `Add`/`Subtract` case defers to (claude/cvm-
  /// abi.md section 3.3's "pointer arithmetic only ever touches the address half" rule, and section
  /// 3.8's `fadd`/`fsub`).</summary>
  private CType EmitWidePointerOrFloatAddSub(CBinaryExpr addSub, int destDouble, int depth)
  {
    TryGetStaticType(addSub.Left, out CType leftStaticType);
    TryGetStaticType(addSub.Right, out CType rightStaticType);

    if (!leftStaticType.IsPointer && rightStaticType.IsPointer && addSub.Op == CBinaryOp.Add)
    {
      // "int + pointer" -- the only shape where the pointer is the RIGHT operand (TryGetStaticType's own
      // rule: a left-pointer wins whenever either side is a pointer, except this one case).
      CType pointerType = EmitWideExprInto(addSub.Right, destDouble, depth);
      int elementWords = Math.Max(pointerType.ElementType!.SizeInWords, 1);

      // guaranteedSamePage: IsBoundedPointerValue(addSub.Right) -- true when the pointer operand just
      // evaluated into destDouble decayed from a compile-time-bounded array (local or global); false
      // when it's a general pointer VALUE (parameter, heap, dereference, cast, ...).
      EmitAddIndexScaledToPointer(destDouble, addSub.Left, elementWords, depth + 1, guaranteedSamePage: IsBoundedPointerValue(addSub.Right));
      return pointerType;
    }

    if (!leftStaticType.IsPointer && !rightStaticType.IsPointer)
    {
      // ADDED 2026-11-08: float arithmetic, with an int / long operand converted to float first.
      return EmitFloatBinary(addSub, destDouble, depth);
    }

    bool spillNeeded = MayCallOrUseLibrary(addSub.Right);
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;
    CType leftType = EmitWideExprInto(addSub.Left, destDouble, depth);
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * destDouble}, {spillLow}");
      EmitCode($"stf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    if (leftType.IsPointer && rightStaticType.IsPointer)
    {
      // "pointer - pointer" -- NOT supported: real C's "p - q" counts ELEMENTS, which needs a divide by
      // the element size that this ISA has no opcode for. Flagged with an error rather than silently
      // emitting a raw (and almost certainly wrong) address subtraction.
      if (spillLow is not null)
      {
        EmitCode($"ldf r{2 * destDouble}, {spillLow}");
        EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
      }

      Error(addSub.Location, "pointer-minus-pointer (\"p - q\") is not supported by this compiler -- no divide opcode exists to scale the raw address difference down to an element count");
      return leftType;
    }

    if (leftType.IsPointer)
    {
      if (spillLow is not null)
      {
        EmitCode($"ldf r{2 * destDouble}, {spillLow}");
        EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
      }

      int elementWords = Math.Max(leftType.ElementType!.SizeInWords, 1);

      // guaranteedSamePage: IsBoundedPointerValue(addSub.Left) -- same reasoning as the "int + pointer"
      // branch above, mirrored onto the left operand (the pointer side in "pointer +/- int").
      EmitAddIndexScaledToPointer(destDouble, addSub.Right, addSub.Op == CBinaryOp.Subtract ? -elementWords : elementWords, depth + 1, guaranteedSamePage: IsBoundedPointerValue(addSub.Left));
      return leftType;
    }

    // Neither operand is a pointer -- both float, the only remaining case EmitWideExprInto's own binary
    // case forwards here (TryGetStaticType's own Add/Subtract rule).
    int rightFloatDouble = WideReg(depth + 1);
    CType rightFloatType = EmitWideExprInto(addSub.Right, rightFloatDouble, depth + 1);
    if (spillLow is not null)
    {
      EmitCode($"ldf r{2 * destDouble}, {spillLow}");
      EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    if (!leftType.IsFloat || !rightFloatType.IsFloat)
    {
      Error(addSub.Location, $"\"{(addSub.Op == CBinaryOp.Add ? "+" : "-")}\" between \"{leftType}\" and \"{rightFloatType}\" is not supported");
      return CType.Float;
    }

    EmitCode($"{(addSub.Op == CBinaryOp.Add ? "fadd" : "fsub")} f{destDouble}, f{rightFloatDouble}"); // fN, not dN (CHANGED 2026-11-08)
    return CType.Float;
  }

  // ---------------------------------------------------------------------------------------------------
  // float <-> integer conversions and float comparisons (ADDED 2026-11-08, see
  // claude/cvm-float-support-2026-11-08.md). + - * / are the coprocessor opcodes (fadd fsub fmul fdiv); the rest
  // calls into libs/cvm/asm/cvmfloat.asm, all __fastcall like the long routines (first wide argument d4 =
  // r8/r9, second d5 = r10/r11, wide result d0 = r0/r1, int result r0), through EmitLongLibraryCall:
  //   int / long -> float     widened to long, then __ltof (__ultof for an unsigned long); an integer CONSTANT is
  //                           folded into a float literal instead
  //   float -> long / int     __ftol (__ftoul for an unsigned target); truncates toward zero; an int keeps the low
  //                           word of the long
  //   < <= > >= == !=         __flt __fle __fgt __fge __feq __fne (IEEE: false for a NaN, != true)
  //   if (f), !f, f && g      inline: the high word without its sign bit, or'd with the low word
  // Mixed int/float arithmetic and comparisons convert the integer side to float (C's usual arithmetic
  // conversions). Every holder of a live wide value spills it around an operand that needs such a call
  // (NeedsFloatConversionCall / MayCallOrUseLibrary).

  /// <summary>Evaluates <paramref name="expr"/> as a float into <paramref name="destDouble"/>, converting an
  /// int / unsigned / long / unsigned long expression.</summary>
  private CType EmitFloatValueInto(CExpr expr, int destDouble, int depth)
  {
    TryGetStaticType(expr, out CType type);
    if (type.IsFloat)
    {
      return EmitWideExprInto(expr, destDouble, depth);
    }

    if (type.IsPointer)
    {
      Error(expr.Location, $"cannot convert \"{type}\" to a float (cast explicitly)");
      EmitCode($"dlit d{destDouble}, 0, 0");
      return CType.Float;
    }

    if (TryGetSimpleConstant(expr, out long constant, out _))
    {
      EmitFloatLiteralInto((float)constant, destDouble);
      return CType.Float;
    }

    EmitLongValueInto(expr, destDouble, depth);
    EmitLongLibraryCall(type.Kind == CTypeKind.UnsignedLong ? "__ultof" : "__ltof", destDouble, -1, -1, depth, -1);
    return CType.Float;
  }

  /// <summary>A float-typed expression converted to a long / unsigned long in <paramref name="destDouble"/>.</summary>
  private CType EmitFloatToLongInto(CExpr expr, int destDouble, int depth, bool unsignedTarget)
  {
    EmitWideExprInto(expr, destDouble, depth);
    EmitLongLibraryCall(unsignedTarget ? "__ftoul" : "__ftol", destDouble, -1, -1, depth, -1);
    return unsignedTarget ? CType.UnsignedLong : CType.Long;
  }

  /// <summary>A float-typed expression used where a 16-bit int is wanted: converted like a long, low word kept.</summary>
  private CType EmitFloatTruncatedInto(CExpr expr, int reg, int depth, bool unsignedTarget)
  {
    int wide = WideReg(depth);
    EmitWideExprInto(expr, wide, depth);
    EmitLongLibraryCall(unsignedTarget ? "__ftoul" : "__ftol", wide, -1, -1, depth, reg);
    return unsignedTarget ? CType.UnsignedInt : CType.Int;
  }

  /// <summary>Evaluates <paramref name="expr"/> for assignment to / passing as <paramref name="targetType"/>, a
  /// wide type (long, unsigned long, float, pointer), applying the implicit conversion to it.</summary>
  private CType EmitValueForTarget(CType targetType, CExpr expr, int destDouble, int depth)
  {
    if (targetType.IsLong)
    {
      if (targetType.Kind == CTypeKind.UnsignedLong && IsFloatTyped(expr))
      {
        return EmitFloatToLongInto(expr, destDouble, depth, true);
      }

      return EmitLongValueInto(expr, destDouble, depth);
    }

    if (targetType.IsFloat)
    {
      return EmitFloatValueInto(expr, destDouble, depth);
    }

    return EmitWideExprInto(expr, destDouble, depth);
  }

  /// <summary><c>left + - * / right</c> where at least one operand is a float: both are converted to float and
  /// combined with the coprocessor opcode.</summary>
  private CType EmitFloatBinary(CBinaryExpr binary, int destDouble, int depth)
  {
    TryGetStaticType(binary.Left, out CType leftType);
    TryGetStaticType(binary.Right, out CType rightType);
    if (!leftType.IsFloat && !rightType.IsFloat)
    {
      Error(binary.Location, $"\"{binary.Op}\" between \"{leftType}\" and \"{rightType}\" is not supported");
      EmitCode($"dlit d{destDouble}, 0, 0");
      return CType.Float;
    }

    string op = binary.Op switch
    {
      CBinaryOp.Add => "fadd",
      CBinaryOp.Subtract => "fsub",
      CBinaryOp.Multiply => "fmul",
      _ => "fdiv",
    };

    bool spillNeeded = FloatOperandUsesLibrary(binary.Right);
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;
    EmitFloatValueInto(binary.Left, destDouble, depth);
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * destDouble}, {spillLow}");
      EmitCode($"stf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    int rightDouble = WideReg(depth + 1);
    EmitFloatValueInto(binary.Right, rightDouble, depth + 1);
    if (spillLow is not null)
    {
      EmitCode($"ldf r{2 * destDouble}, {spillLow}");
      EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    EmitCode($"{op} f{destDouble}, f{rightDouble}"); // fN, not dN (CHANGED 2026-11-08)
    return CType.Float;
  }

  private static string FloatComparisonHelper(CBinaryOp op) => op switch
  {
    CBinaryOp.Less => "__flt",
    CBinaryOp.LessOrEqual => "__fle",
    CBinaryOp.Greater => "__fgt",
    CBinaryOp.GreaterOrEqual => "__fge",
    CBinaryOp.Equal => "__feq",
    _ => "__fne",
  };

  /// <summary>A comparison with a float on at least one side: both operands are converted to float and the
  /// IEEE predicate of cvmfloat.asm is called; the 0/1 result lands in <paramref name="reg"/>.</summary>
  private void EmitFloatComparisonInto(CBinaryExpr comparison, int reg, int depth)
  {
    bool spillNeeded = FloatOperandUsesLibrary(comparison.Right);
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;

    int leftDouble = WideReg(depth);
    EmitFloatValueInto(comparison.Left, leftDouble, depth);
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * leftDouble}, {spillLow}");
      EmitCode($"stf r{2 * leftDouble + 1}, {spillLow + 1}");
    }

    int rightDouble = WideReg(depth + 1);
    EmitFloatValueInto(comparison.Right, rightDouble, depth + 1);
    if (spillLow is not null)
    {
      EmitCode($"ldf r{2 * leftDouble}, {spillLow}");
      EmitCode($"ldf r{2 * leftDouble + 1}, {spillLow + 1}");
    }

    EmitLongLibraryCall(FloatComparisonHelper(comparison.Op), leftDouble, rightDouble, -1, depth, reg);
  }

  /// <summary>Bit-splits a `float` literal's IEEE-754 pattern into a `dlit dX, hi, lo` -- the same hi/lo
  /// operand order (confirmed 2026-10-16) <see cref="EmitAddressOfInto"/>'s own Global case and every
  /// other `dlit` emission in this file already uses.</summary>
  private void EmitFloatLiteralInto(float value, int destDouble)
  {
    int bits = BitConverter.SingleToInt32Bits(value);
    int hi = (int)((uint)bits >> 16);
    int lo = bits & 0xFFFF;
    EmitCode($"dlit d{destDouble}, 0x{hi:X4}, 0x{lo:X4}");
  }

  /// <summary>The `?:` operator. <paramref name="scalarDest"/>/<paramref name="wideDest"/> follow this
  /// file's usual "-1 sentinel for the unused side" convention, selected by <paramref
  /// name="isWide"/>.</summary>
  private CType EmitConditionalInto(CConditionalExpr conditional, int scalarDest, int wideDest, int depth, bool isWide)
  {
    string falseLabel = NewLabel("condfalse");
    string endLabel = NewLabel("condend");

    EmitConditionTestInto(conditional.Condition, ScalarReg(depth), depth);
    EmitCode($"if r{ScalarReg(depth)} ==0 then {falseLabel}");

    bool longResult = isWide && IsLongTyped(conditional);
    bool floatResult = isWide && IsFloatTyped(conditional);
    CType trueType = longResult
        ? EmitLongValueInto(conditional.WhenTrue, wideDest, depth)
        : floatResult
            ? EmitFloatValueInto(conditional.WhenTrue, wideDest, depth)
            : isWide
                ? EmitWideExprInto(conditional.WhenTrue, wideDest, depth)
                : EmitIntExprInto(conditional.WhenTrue, scalarDest, depth);
    EmitCode($"br {endLabel}");

    EmitLabel(falseLabel);
    CType falseType = longResult
        ? EmitLongValueInto(conditional.WhenFalse, wideDest, depth)
        : floatResult
            ? EmitFloatValueInto(conditional.WhenFalse, wideDest, depth)
            : isWide
                ? EmitWideExprInto(conditional.WhenFalse, wideDest, depth)
                : EmitIntExprInto(conditional.WhenFalse, scalarDest, depth);

    EmitLabel(endLabel);
    if (longResult)
    {
      return trueType.Kind == CTypeKind.UnsignedLong || falseType.Kind == CTypeKind.UnsignedLong ? CType.UnsignedLong : CType.Long;
    }

    return trueType.IsFloat || falseType.IsFloat ? CType.Float : trueType;
  }

  private static bool IsFloatBuiltinName(string name) => name is "fabsf" or "fminf" or "fmaxf";

  /// <summary>`fabsf`/`fminf`/`fmaxf` -- compiler-recognized builtins against `mask15`/`fmin`/`fmax`
  /// directly (claude/cvm-abi.md section 3.8/3.9: `__optimize` is retired as a SEPARATE mechanism, so
  /// these three names are substituted unconditionally whenever their signature matches, whether or not
  /// the declaration used `__optimize`). `fabsf` clears the IEEE sign bit on the HIGH r-alias of the
  /// `d0`-`d3` register holding the value -- the "r-alias trick" (section 3.4), valid because
  /// <paramref name="destDouble"/> is always in that range here.</summary>
  private CType EmitFloatBuiltinCall(CCallExpr call, int scalarDest, int wideDest, int depth, bool isWide)
  {
    if (!isWide)
    {
      Error(call.Location, $"internal: \"{call.FunctionName}\" returns 'float', which needs a wide destination");
    }

    int destDouble = isWide ? wideDest : WideReg(depth);

    if (call.FunctionName == "fabsf")
    {
      if (call.Arguments.Count != 1)
      {
        Error(call.Location, "\"fabsf\" takes exactly one argument");
      }

      EmitFloatValueInto(call.Arguments.Count > 0 ? call.Arguments[0] : new CFloatLiteralExpr(call.Location, 0f), destDouble, depth);
      EmitCode($"mask15 r{2 * destDouble + 1}");
      return CType.Float;
    }

    if (call.Arguments.Count != 2)
    {
      Error(call.Location, $"\"{call.FunctionName}\" takes exactly two arguments");
    }

    bool spillNeeded = call.Arguments.Count > 1 && (ContainsCall(call.Arguments[1]) || FloatOperandUsesLibrary(call.Arguments[1]));
    int? spillLow = spillNeeded ? AllocateFrameWords(2) : null;
    EmitFloatValueInto(call.Arguments.Count > 0 ? call.Arguments[0] : new CFloatLiteralExpr(call.Location, 0f), destDouble, depth);
    if (spillLow is not null)
    {
      EmitCode($"stf r{2 * destDouble}, {spillLow}");
      EmitCode($"stf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    int rightDouble = WideReg(depth + 1);
    EmitFloatValueInto(call.Arguments.Count > 1 ? call.Arguments[1] : new CFloatLiteralExpr(call.Location, 0f), rightDouble, depth + 1);
    if (spillLow is not null)
    {
      EmitCode($"ldf r{2 * destDouble}, {spillLow}");
      EmitCode($"ldf r{2 * destDouble + 1}, {spillLow + 1}");
    }

    EmitCode($"{(call.FunctionName == "fminf" ? "fmin" : "fmax")} f{destDouble}, f{rightDouble}"); // fN, not dN (CHANGED 2026-11-08)
    return CType.Float;
  }

  private void CheckArgumentType(CSourceLocation location, int argumentIndex, CType expectedType, CType actualType)
  {
    if (expectedType.IsFloat != actualType.IsFloat)
    {
      if (!(expectedType.IsFloat ? actualType : expectedType).IsIntegral)
      {
        Error(location, $"argument {argumentIndex + 1}: cannot pass \"{actualType}\" where \"{expectedType}\" is expected (float/non-float mismatch -- cast explicitly)");
      }
    }
    else if (expectedType.IsLong && !actualType.IsIntegral || actualType.IsLong && expectedType.IsPointer)
    {
      Error(location, $"argument {argumentIndex + 1}: cannot pass \"{actualType}\" where \"{expectedType}\" is expected (cast explicitly)");
    }
  }

  /// <summary>
  /// A function call. Functions are never mangled (unlike globals, which get a leading "_" --
  /// <see cref="MangleExternalSymbol"/>): the old compiler already called library routines like
  /// "__mul" directly by their plain name, and this one keeps that. Every argument, whatever its final
  /// destination, is evaluated LEFT TO RIGHT into its own dedicated frame scratch slot first (claude/
  /// cvm-abi.md section 3.4's own "spilling across a call" idea, generalized to every argument of a
  /// call, not just a binary operator's one operand) -- this decouples evaluation order from the order
  /// values must finally be placed in without ever needing to keep more than one argument's value live
  /// in a shared scratch register while evaluating another, possibly call-containing, argument. Once
  /// every argument is safely parked in its own frame slot: stack-passed arguments are reloaded and
  /// pushed RIGHT TO LEFT (so the first-declared one ends up closest to the new frame's own <c>fp</c> --
  /// section 3.5), then register-passed (fastcall) arguments are reloaded directly into their final
  /// <c>r0</c>-<c>r3</c>/<c>d4</c>-<c>d7</c> register, deferred until immediately before the <c>call</c>
  /// itself (section 3.4) -- and only then is <c>call</c> emitted. After it returns, the caller drops
  /// every stack-passed argument word it pushed (one <c>drop</c> per word -- there is no callee-cleanup
  /// convention in this ABI, unlike the old "leave" instruction) and reads the return value out of
  /// <c>r0</c>/<c>d0</c> directly.
  /// </summary>
  private CType EmitCallInto(CCallExpr call, int scalarDest, int wideDest, int depth, bool isWide)
  {
    if (!_functions.TryGetValue(call.FunctionName, out CFunctionSignature? signature))
    {
      Error(call.Location, $"\"{call.FunctionName}\" is not declared");
      if (isWide)
      {
        EmitCode($"dlit d{wideDest}, 0, 0");
      }
      else
      {
        EmitCode($"rlit r{scalarDest}, 0");
      }

      return isWide ? CType.PointerTo(CType.Void) : CType.Int;
    }

    if (IsFloatBuiltinName(call.FunctionName) && signature.ReturnType.IsFloat
        && signature.ParameterTypes.Count is 1 or 2 && signature.ParameterTypes.All(t => t.IsFloat))
    {
      return EmitFloatBuiltinCall(call, scalarDest, wideDest, depth, isWide);
    }

    if (call.Arguments.Count != signature.ParameterTypes.Count)
    {
      Error(call.Location, $"\"{call.FunctionName}\" expects {signature.ParameterTypes.Count} argument(s), got {call.Arguments.Count}");
    }

    IReadOnlyList<CParamSlot> slots = ComputeParameterSlots(signature.ParameterTypes, signature.IsFastcall);
    int argumentCount = Math.Min(call.Arguments.Count, slots.Count);

    var argumentSpillOffsets = new int[argumentCount];
    for (int i = 0; i < argumentCount; i++)
    {
      CType expectedType = slots[i].Type;
      bool argIsWide = expectedType.IsWideValue;
      int size = Math.Max(expectedType.SizeInWords, 1);
      int spillOffset = AllocateFrameWords(size);
      argumentSpillOffsets[i] = spillOffset;

      if (argIsWide)
      {
        int argDouble = WideReg(0);
        CType actualType = EmitValueForTarget(expectedType, call.Arguments[i], argDouble, 0);
        CheckArgumentType(call.Arguments[i].Location, i, expectedType, actualType);
        EmitCode($"stf r{2 * argDouble}, {spillOffset}");
        EmitCode($"stf r{2 * argDouble + 1}, {spillOffset + 1}");
      }
      else
      {
        int argReg = ScalarReg(0);
        CType actualType = EmitIntExprInto(call.Arguments[i], argReg, 0);
        CheckArgumentType(call.Arguments[i].Location, i, expectedType, actualType);
        EmitCode($"stf r{argReg}, {spillOffset}");
      }
    }

    for (int i = argumentCount - 1; i >= 0; i--)
    {
      CParamSlot slot = slots[i];
      if (slot.IsRegister)
      {
        continue;
      }

      int size = Math.Max(slot.Type.SizeInWords, 1);
      if (size == 2)
      {
        // WideReg(0), not GlobalAddressTempDouble (d8): `ldf`'s register field is 4 bits (r0-r15 only),
        // so d8's own r-aliases, r16/r17, are not reachable here -- confirmed by Stefan 2026-10-18, "r16
        // and r17 are not accessible with opcodes using rx". WideReg(0) is safe to reuse as transient
        // scratch at this point: the loop just above already consumed every argument (including this
        // one, at the SAME depth 0) into its own spill slot before this second loop runs, so nothing is
        // still live in WideReg(0)/ScalarReg(0) here -- the same precedent this loop's own `else` branch
        // already relies on for ScalarReg(0).
        int scratchDouble = WideReg(0);
        EmitCode($"ldf r{2 * scratchDouble}, {argumentSpillOffsets[i]}");
        EmitCode($"ldf r{2 * scratchDouble + 1}, {argumentSpillOffsets[i] + 1}");
        EmitCode($"dpush d{scratchDouble}");
      }
      else
      {
        EmitCode($"ldf r{ScalarReg(0)}, {argumentSpillOffsets[i]}");
        EmitCode($"rpush r{ScalarReg(0)}");
      }
    }

    for (int i = 0; i < argumentCount; i++)
    {
      CParamSlot slot = slots[i];
      if (!slot.IsRegister)
      {
        continue;
      }

      if (slot.IsWide)
      {
        EmitCode($"ldf r{2 * slot.RegisterIndex}, {argumentSpillOffsets[i]}");
        EmitCode($"ldf r{2 * slot.RegisterIndex + 1}, {argumentSpillOffsets[i] + 1}");
      }
      else
      {
        EmitCode($"ldf r{slot.RegisterIndex}, {argumentSpillOffsets[i]}");
      }
    }

    EmitCode($"call {call.FunctionName}");

    int stackWordsToDrop = slots.Where(s => !s.IsRegister).Sum(s => Math.Max(s.Type.SizeInWords, 1));
    for (int i = 0; i < stackWordsToDrop; i++)
    {
      EmitCode("drop");
    }

    if (signature.ReturnType.IsVoid)
    {
      if (isWide)
      {
        EmitCode($"dlit d{wideDest}, 0, 0");
      }
      else
      {
        EmitCode($"rlit r{scalarDest}, 0");
      }

      return signature.ReturnType;
    }

    if (isWide)
    {
      if (wideDest != WideReturnDouble)
      {
        EmitCode($"mov r{2 * wideDest}, r{2 * WideReturnDouble}");
        EmitCode($"mov r{2 * wideDest + 1}, r{2 * WideReturnDouble + 1}");
      }
    }
    else
    {
      if (scalarDest != ScalarReturnRegister)
      {
        EmitCode($"mov r{scalarDest}, r{ScalarReturnRegister}");
      }
    }

    return signature.ReturnType;
  }

  // ---------------------------------------------------------------------------------------------------
  // Statements and control flow. Every "branch when false"/"loop while true" test goes through
  // EmitConditionTestInto + `if rN ==0/!=0 then label` -- CONFIRMED 2026-10-03 against real hardware
  // (see claude/cvm-br-if-cond-verification.md): `if` branches when its own condition is TRUE, so
  // testing the COMPLEMENT condition directly gets "skip when false" with no separate negation step
  // (unlike the old, retired `cbr`, which branched on r == 0 unconditionally and needed an explicit
  // `eq0` first for e.g. do-while's "loop while true").

  private void EmitSwitchLabel(CStmt labelNode, CStmt inner)
  {
    if (_switchLabelsByNode is null || !_switchLabelsByNode.TryGetValue(labelNode, out string? label))
    {
      Error(labelNode.Location, "\"case\"/\"default\" is not within a switch");
      EmitStatement(inner);
      return;
    }

    EmitLabel(label);
    EmitStatement(inner);
  }

  private string MangleLabel(string name) => $"{_currentFunction!.Name}__label_{name}";

  private void EmitStatement(CStmt stmt)
  {
    if (stmt is not (CCompoundStmt or CEmptyStmt))
    {
      EmitSourceLineComment(stmt.Location);
    }

    switch (stmt)
    {
      case CCompoundStmt compound:
        EnterScope();
        foreach (CStmt inner in compound.Statements)
        {
          EmitStatement(inner);
        }

        ExitScope();
        break;

      case CEmptyStmt:
        break;

      case CExprStmt exprStmt:
        EmitStatementExprForEffect(exprStmt.Expression, 0);
        break;

      case CLocalVarDecl localDecl:
        DeclareLocal(localDecl);
        break;

      case CIfStmt ifStmt:
        {
          string elseLabel = NewLabel("else");
          string endLabel = NewLabel("endif");
          EmitConditionTestInto(ifStmt.Condition, ScalarReg(0), 0);
          EmitCode($"if r{ScalarReg(0)} ==0 then {elseLabel}");
          EmitStatement(ifStmt.Then);
          EmitCode($"br {endLabel}");
          EmitLabel(elseLabel);
          if (ifStmt.Else is not null)
          {
            EmitStatement(ifStmt.Else);
          }

          EmitLabel(endLabel);
          break;
        }

      case CWhileStmt whileStmt:
        {
          string startLabel = NewLabel("while");
          string endLabel = NewLabel("endwhile");
          _currentFunction!.LoopLabels.Add((endLabel, startLabel));
          EmitLabel(startLabel);
          EmitConditionTestInto(whileStmt.Condition, ScalarReg(0), 0);
          EmitCode($"if r{ScalarReg(0)} ==0 then {endLabel}");
          EmitStatement(whileStmt.Body);
          EmitCode($"br {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.LoopLabels.RemoveAt(_currentFunction.LoopLabels.Count - 1);
          break;
        }

      case CDoWhileStmt doWhileStmt:
        {
          string startLabel = NewLabel("dowhile");
          string continueLabel = NewLabel("dowhile_cont");
          string endLabel = NewLabel("enddowhile");
          _currentFunction!.LoopLabels.Add((endLabel, continueLabel));
          EmitLabel(startLabel);
          EmitStatement(doWhileStmt.Body);
          EmitLabel(continueLabel);
          EmitConditionTestInto(doWhileStmt.Condition, ScalarReg(0), 0);
          EmitCode($"if r{ScalarReg(0)} !=0 then {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.LoopLabels.RemoveAt(_currentFunction.LoopLabels.Count - 1);
          break;
        }

      case CForStmt forStmt:
        {
          string startLabel = NewLabel("for");
          string continueLabel = NewLabel("for_cont");
          string endLabel = NewLabel("endfor");

          EnterScope();
          if (forStmt.Init is not null)
          {
            EmitStatement(forStmt.Init);
          }

          _currentFunction!.LoopLabels.Add((endLabel, continueLabel));
          EmitLabel(startLabel);
          if (forStmt.Condition is not null)
          {
            EmitConditionTestInto(forStmt.Condition, ScalarReg(0), 0);
            EmitCode($"if r{ScalarReg(0)} ==0 then {endLabel}");
          }

          EmitStatement(forStmt.Body);
          EmitLabel(continueLabel);
          if (forStmt.Update is not null)
          {
            EmitStatementExprForEffect(forStmt.Update, 0);
          }

          EmitCode($"br {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.LoopLabels.RemoveAt(_currentFunction.LoopLabels.Count - 1);
          ExitScope();
          break;
        }

      case CReturnStmt returnStmt:
        {
          if (returnStmt.Value is not null)
          {
            if (_currentFunction!.ReturnType.IsVoid)
            {
              Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" returns void and cannot return a value");
            }
            else if (IsUnsupportedWholeValueType(_currentFunction.ReturnType))
            {
              ReportUnsupportedValueRead(returnStmt.Location, _currentFunction.ReturnType, $"function \"{_currentFunction.Name}\"'s return value");
            }
            else if (_currentFunction.ReturnType.IsLong)
            {
              // ADDED 2026-11-08: the value is computed in the ordinary wide scratch pool and copied to
              // d0 (r0 = low word, r1 = high word) last, because d0 is also the in-flight pointer
              // arithmetic scratch and a library call's result register.
              int returnScratch = WideReg(0);
              CType valueType = EmitValueForTarget(_currentFunction.ReturnType, returnStmt.Value, returnScratch, 0);
              CheckAssignmentType(returnStmt.Location, _currentFunction.ReturnType, valueType);
              EmitCode($"mov r{2 * WideReturnDouble}, r{2 * returnScratch}");
              EmitCode($"mov r{2 * WideReturnDouble + 1}, r{2 * returnScratch + 1}");
            }
            else if (_currentFunction.ReturnType.IsFloat)
            {
              // ADDED 2026-11-08: evaluated in WideReg(0) (conversions call the library, whose result lands in
              // d0) and moved into d0 last, exactly like a long.
              int returnScratch = WideReg(0);
              CType valueType = EmitFloatValueInto(returnStmt.Value, returnScratch, 0);
              CheckAssignmentType(returnStmt.Location, _currentFunction.ReturnType, valueType);
              EmitCode($"mov r{2 * WideReturnDouble}, r{2 * returnScratch}");
              EmitCode($"mov r{2 * WideReturnDouble + 1}, r{2 * returnScratch + 1}");
            }
            else if (_currentFunction.ReturnType.IsPointer)
            {
              CType valueType = EmitWideExprInto(returnStmt.Value, WideReturnDouble, 0);
              if (valueType.IsFloat != _currentFunction.ReturnType.IsFloat)
              {
                Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" returns \"{_currentFunction.ReturnType}\", not \"{valueType}\"");
              }
            }
            else if (IsPlainScalarExpression(returnStmt.Value))
            {
              EmitIntExprInto(returnStmt.Value, ScalarReturnRegister, 0);
            }
            else
            {
              // ADDED 2026-11-08: r0 is also the low half of d0, the scratch register pair pointer
              // arithmetic (EmitAddIndexScaledToPointer / EmitAddConstantToRegister) works in, and the register
              // every call and runtime-library call returns its result in. An expression that can use any of
              // those must therefore NOT hold its partial result in r0: evaluate it in the ordinary scratch
              // register and copy the final value across. (Before this, "return p[i*3] + p[j*3];" lost its
              // left operand to the index scaling of the right one.)
              EmitIntExprInto(returnStmt.Value, ScalarReg(0), 0);
              EmitCode($"mov r{ScalarReturnRegister}, r{ScalarReg(0)}");
            }
          }
          else if (!_currentFunction!.ReturnType.IsVoid)
          {
            Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" must return a value of type \"{_currentFunction.ReturnType}\"");
          }

          EmitCode("unlink");
          EmitCode("ret");
          break;
        }

      case CBreakStmt breakStmt:
        if (_currentFunction!.LoopLabels.Count == 0)
        {
          Error(breakStmt.Location, "\"break\" is not within a loop or switch");
          break;
        }

        EmitCode($"br {_currentFunction.LoopLabels[^1].BreakLabel}");
        break;

      case CContinueStmt continueStmt:
        {
          int i = _currentFunction!.LoopLabels.Count - 1;
          while (i >= 0 && _currentFunction.LoopLabels[i].ContinueLabel is null)
          {
            i--; // skip a switch's own break-only scope -- it has no continue target of its own.
          }

          if (i < 0)
          {
            Error(continueStmt.Location, "\"continue\" is not within a loop");
            break;
          }

          EmitCode($"br {_currentFunction.LoopLabels[i].ContinueLabel}");
          break;
        }

      case CGotoStmt gotoStmt:
        EmitCode($"br {MangleLabel(gotoStmt.Label)}");
        break;

      case CLabelStmt labelStmt:
        EmitLabel(MangleLabel(labelStmt.Label));
        EmitStatement(labelStmt.Inner);
        break;

      case CCaseLabelStmt caseLabelStmt:
        EmitSwitchLabel(caseLabelStmt, caseLabelStmt.Inner);
        break;

      case CDefaultLabelStmt defaultLabelStmt:
        EmitSwitchLabel(defaultLabelStmt, defaultLabelStmt.Inner);
        break;

      case CSwitchStmt switchStmt:
        EmitSwitch(switchStmt);
        break;

      default:
        Error(stmt.Location, $"statement kind \"{stmt.GetType().Name}\" is not yet supported by the code generator");
        break;
    }
  }

  /// <summary>
  /// Compiles a `switch` as an if-chain (claude/cvm-abi.md section 3.9 -- no `tjmp`-style jump table has
  /// ever been confirmed for this project). The selector is evaluated exactly once into a frame scratch
  /// slot, then a chain of subtract+`if ... ==0 then label` comparisons (in source order, "default" last
  /// regardless of where it was written) jumps into the right position; the body then runs as ordinary
  /// statements, giving real C fall-through for free.
  /// </summary>
  private void EmitSwitch(CSwitchStmt switchStmt)
  {
    string endLabel = NewLabel("endswitch");
    var labels = new Dictionary<CStmt, string>(ReferenceEqualityComparer.Instance);
    var cases = new List<(long Value, string Label)>();
    string? defaultLabel = null;
    CollectSwitchLabels(switchStmt.Body, labels, cases, ref defaultLabel, switchStmt.Location);

    TryGetStaticType(switchStmt.Selector, out CType selectorType);
    if (selectorType.IsFloat || selectorType.IsPointer || selectorType.IsLong)
    {
      Error(switchStmt.Location, "a \"switch\" selector must be an integer expression");
    }

    int selectorReg = ScalarReg(0);
    EmitIntExprInto(switchStmt.Selector, selectorReg, 0);
    int selectorSlot = AllocateFrameWords(1);
    EmitCode($"stf r{selectorReg}, {selectorSlot}");

    bool isUnsigned = selectorType.IsUnsigned;
    foreach ((long value, string label) in cases)
    {
      int compareReg = ScalarReg(0);
      int literalReg = ScalarReg(1);
      EmitCode($"ldf r{compareReg}, {selectorSlot}");
      EmitCode($"rlit r{literalReg}, {(int)(value & 0xFFFF)}");
      if (isUnsigned)
      {
        int biasReg = ScalarReg(2);
        EmitCode($"rlit r{biasReg}, 0x8000");
        EmitCode($"xor r{compareReg}, r{biasReg}");
        EmitCode($"xor r{literalReg}, r{biasReg}");
      }

      EmitCode($"sub r{compareReg}, r{literalReg}");
      EmitCode($"if r{compareReg} ==0 then {label}");
    }

    EmitCode($"br {defaultLabel ?? endLabel}");

    Dictionary<CStmt, string>? outerSwitchLabels = _switchLabelsByNode;
    _switchLabelsByNode = labels;
    _currentFunction!.LoopLabels.Add((endLabel, null)); // break-only scope -- "continue" skips past it.
    EmitStatement(switchStmt.Body);
    _currentFunction.LoopLabels.RemoveAt(_currentFunction.LoopLabels.Count - 1);
    _switchLabelsByNode = outerSwitchLabels;

    EmitLabel(endLabel);
  }

  /// <summary>Walks a switch's body collecting (constant value, label) pairs for every
  /// <see cref="CCaseLabelStmt"/> and the label for a single <see cref="CDefaultLabelStmt"/>, WITHOUT
  /// descending into a nested switch's own body (those cases belong to the inner switch, not this
  /// one).</summary>
  private void CollectSwitchLabels(CStmt node, Dictionary<CStmt, string> labels, List<(long Value, string Label)> cases, ref string? defaultLabel, CSourceLocation switchLocation)
  {
    switch (node)
    {
      case CCaseLabelStmt caseLabel:
        {
          if (!TryEvaluateConstantLong(caseLabel.Value, out long value))
          {
            Error(caseLabel.Location, "a \"case\" label must be a compile-time integer constant");
            value = 0;
          }

          if (cases.Any(c => c.Value == value))
          {
            Error(caseLabel.Location, $"duplicate \"case {value}\" label");
          }

          string label = NewLabel($"case_{value}");
          labels[caseLabel] = label;
          cases.Add((value, label));
          CollectSwitchLabels(caseLabel.Inner, labels, cases, ref defaultLabel, switchLocation);
          break;
        }

      case CDefaultLabelStmt defaultLabelStmt:
        {
          if (defaultLabel is not null)
          {
            Error(defaultLabelStmt.Location, "a \"switch\" may have only one \"default\" label");
          }

          string label = NewLabel("default");
          labels[defaultLabelStmt] = label;
          defaultLabel = label;
          CollectSwitchLabels(defaultLabelStmt.Inner, labels, cases, ref defaultLabel, switchLocation);
          break;
        }

      case CCompoundStmt compound:
        foreach (CStmt inner in compound.Statements)
        {
          CollectSwitchLabels(inner, labels, cases, ref defaultLabel, switchLocation);
        }

        break;

      case CIfStmt ifStmt:
        CollectSwitchLabels(ifStmt.Then, labels, cases, ref defaultLabel, switchLocation);
        if (ifStmt.Else is not null)
        {
          CollectSwitchLabels(ifStmt.Else, labels, cases, ref defaultLabel, switchLocation);
        }

        break;

      case CWhileStmt whileStmt:
        CollectSwitchLabels(whileStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CDoWhileStmt doWhileStmt:
        CollectSwitchLabels(doWhileStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CForStmt forStmt:
        CollectSwitchLabels(forStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CLabelStmt labelStmt:
        CollectSwitchLabels(labelStmt.Inner, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CSwitchStmt:
        break; // a nested switch's own case/default labels belong to it, not this one.

      default:
        break;
    }
  }
}
