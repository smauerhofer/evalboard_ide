using System.Text;

namespace Ga144.C.Toolchain;

/// <summary>
/// Walks a <see cref="CTranslationUnit"/> and emits CVM assembly text (the same <c>.casm</c> syntax
/// <c>CvmAssembler</c> accepts) implementing it. There is no separate type-checking pass and no
/// annotated tree: <see cref="EmitExpr"/> computes (and checks) each expression's <see cref="CType"/>
/// in the same walk that emits its code.
///
/// <para><b>CVM C ABI v1 -- read this before touching any codegen method below.</b> None of the
/// following is confirmed against real CVM hardware or a full node-506/606/607 simulation; it is this
/// compiler's own necessary, documented hypothesis about how the primitives it emits actually behave at
/// runtime, chosen as the most standard interpretation for a stack machine of this shape. If any part of
/// it turns out wrong once CVM2's frame/interpreter nodes are simulated in full, the fix is localized to
/// the handful of methods named in each bullet:</para>
/// <list type="bullet">
/// <item><description><c>pushlit N</c> pushes literal N as a new stack top (stack depth +1).</description></item>
/// <item><description><c>pop</c> pops the stack top into register r (stack -1). <c>push</c> pushes r
/// back onto the stack non-destructively, leaving r unchanged (stack +1) -- so "pop; push; push"
/// duplicates the top of stack ([...,v] -&gt; pop: r:=v,[...] -&gt; push: [...,v] -&gt; push: [...,v,v]).
/// See <see cref="EmitDup"/>.</description></item>
/// <item><description>Every binary ALU/comparison op (add, sub, and, xor, or, usl, ssr, usr, eq, ne, lt,
/// gt, le, ge, ult, ugt, ule, uge) consumes r (set by an immediately preceding <c>pop</c>) and the
/// current stack top, and replaces the stack top with the result -- so "EmitExpr(lhs); EmitExpr(rhs);
/// pop; &lt;op&gt;" computes "lhs OP rhs" with the correct operand order and leaves exactly one result
/// word. See <see cref="EmitBinaryOperation"/>.</description></item>
/// <item><description>Every unary op (inv, negate, eq0) acts on r alone: "pop; &lt;op&gt;; push" pops the
/// operand into r, transforms r in place, and pushes the result back (net: pop 1, push 1). See
/// <see cref="EmitExpr"/>'s <see cref="CUnaryExpr"/> case.</description></item>
/// <item><description><c>xt</c>/<c>ldt</c>/<c>stt</c> are this ABI's generic pointer/memory primitives:
/// <c>xt</c> pops an address off the stack into a "t" register; <c>ldt</c> pushes the value stored at
/// address t without consuming t; <c>stt</c> pops a value off the stack and stores it at address t.
/// Node 606's own frame ops -- <c>ldl</c>/<c>ldp</c>/<c>lal</c>/<c>lap</c> and <c>stl</c>/<c>stp</c> --
/// are assumed to behave the same way, directly against the data stack (push a value / pop-and-store a
/// value), never through register r -- a separate mechanism from the generic ALU ops' r-mediated
/// pop+op+push above, but one consistent with a dedicated "local/parameter access" instruction needing
/// no extra register hop. IMPORTANT: "t" has no save/restore of its own, so this compiler never assumes
/// it survives arbitrary code emitted between resolving an Indirect lvalue's address and using it --
/// every Indirect lvalue's address is cached into its own dedicated local slot the moment it's computed,
/// and <c>xt</c> is re-issued from that cached slot immediately before every single load or store
/// through it, however many other things (including further "xt"s of their own) ran in between. See
/// <see cref="CLvalue"/>/<see cref="ResolveLvalue"/>/<see cref="CacheIndirectAddress"/>/
/// <see cref="EmitLoad"/>/<see cref="EmitStore"/>.</description></item>
/// <item><description><c>ifbr &lt;label&gt;</c> branches when the condition (loaded into r by an
/// immediately preceding <c>pop</c>) is NONZERO (true). Every control-flow codegen method below is built
/// around this polarity.</description></item>
/// <item><description><b>Uniform codegen invariant:</b> every non-void expression's codegen leaves
/// EXACTLY one word on the data stack representing its value; a void-typed expression (a call to a void
/// function) leaves nothing. An expression used as a statement emits the expression, then a bare
/// <c>pop</c> to discard the leftover value when its type isn't void. This single invariant is what lets
/// every expression case in <see cref="EmitExpr"/> compose with every other one with no "is this value
/// needed" threading anywhere in the compiler, at the modest cost of an occasional extra
/// <c>pop</c>/dup.</description></item>
/// <item><description><b>Function calling convention:</b> the caller evaluates and pushes each argument
/// left-to-right, then <c>call</c>s; the callee's <c>enter &lt;n&gt;</c> reserves n local slots and is
/// assumed to make the caller's pushed arguments addressable via <c>ldp</c>/<c>stp</c>/<c>lap</c>
/// (parameter offset 0 = the first declared parameter); <c>return expr;</c> evaluates expr (leaving its
/// value on the stack) and branches to the function's epilogue label; the epilogue's <c>leave</c> is
/// assumed to deallocate all locals AND the caller's pushed arguments together, leaving only the single
/// return value (if any) on top -- a Pascal-style callee-cleanup convention -- followed by <c>ret</c>.
/// See <see cref="EmitFunction"/>.</description></item>
/// <item><description><b>ABI v2 addition (2026-09-06): address-register pointer parameters.</b> Per
/// Stefan's own node 306 source ("In 306 there are 4 32-bit address register to access the whole memory
/// range... for the ABI the address register are volatile and are not saved on the stack when a
/// function is entered. they are also used for the first 4 pointers as arguments to a function"): among
/// a function's PARAMETERS, in declaration order, the first four whose TYPE is a pointer (an array
/// parameter already decays to pointer by the time <see cref="CParser"/> hands it here -- see
/// <see cref="CParameter"/>) are passed via node 306's address registers 0-3 instead of the ordinary
/// push-onto-the-stack convention above; every OTHER parameter (a 5th-or-later pointer, or any
/// non-pointer parameter, wherever it falls among the four) keeps ABI v1's stack convention completely
/// unchanged, just renumbered to skip the register-passed ones (see
/// <see cref="ComputeParameterRegisterSlots"/>). This is a pure CALLING-CONVENTION change, nothing else:
/// pointer TYPE and SIZE are UNCHANGED (still exactly one CVM word each -- this compiler does not yet
/// support the "whole memory range"/far/32-bit addressing node 306's own registers make possible; every
/// pointer this compiler emits still addresses only the current node's own local page, with an implicit
/// page word of 0 -- a known, deliberate gap, not yet resolved with Stefan), and pointer DEREFERENCE
/// (<c>*p</c>, <c>p[i]</c>) still goes through the existing <c>xt</c>/<c>ldt</c>/<c>stt</c> mechanism
/// above, completely untouched.
///
/// Mechanically: at a CALL SITE (<see cref="EmitCall"/>), a register-eligible argument is still
/// evaluated in its normal left-to-right position (leaving one stack word, per the uniform invariant
/// below) but is then immediately popped into r, paired with a literal page word of 0, and loaded into
/// its assigned address register via <c>lda</c> -- never left on the stack for the callee. At the
/// CALLEE'S OWN PROLOGUE (<see cref="EmitFunction"/>), right after <c>enter &lt;n&gt;</c>, each
/// register-eligible parameter is immediately spilled OUT of its address register (<c>sta</c>) into an
/// ordinary LOCAL slot (not a Parameter-kind frame offset -- it never arrived via the stack, so it is
/// allocated and referenced exactly like any other <see cref="CLvalueKind.Local"/> variable for the rest
/// of the function body, needing no new <see cref="CLvalueKind"/> or <see cref="CVarKind"/> at all).
/// Address registers are NEVER trusted to persist beyond that one transient moment (call-site transmit,
/// or prologue spill) -- exactly the same discipline this ABI already applies to the "t" register above,
/// for the same reason (volatile, no save/restore of its own, and here explicitly confirmed by Stefan:
/// "not saved on the stack when a function is entered"). See <see cref="ComputeParameterRegisterSlots"/>,
/// <see cref="EmitFunction"/>, and <see cref="EmitCall"/> for the exact instruction sequences.
/// </description></item>
/// <item><description><c>*</c>/<c>/</c>/<c>%</c> have no hardware primitive, so they compile to calls
/// into runtime helper routines Stefan implements by hand in the "cvm" assembly library:
/// <c>__mul</c>, <c>__divs</c>/<c>__divu</c> (signed/unsigned divide), <c>__mods</c>/<c>__modu</c>
/// (signed/unsigned modulo). Calling convention for these leaf helpers: caller pushes lhs then rhs
/// (left-to-right), <c>call</c>s, callee consumes both top-of-stack words and pushes exactly one result
/// word, then <c>ret</c>s -- no frame needed since they're pure leaves. See
/// <see cref="EmitLibraryBinary"/>.</description></item>
/// <item><description><c>&lt;&lt;</c>/<c>&gt;&gt;</c> need no library call: node 507's <c>usl</c>/
/// <c>ssr</c>/<c>usr</c> are binary (value, count), so they use the ordinary pop+op pattern
/// directly.</description></item>
/// <item><description>Every scalar and pointer type this compiler supports is exactly one CVM word
/// (see <see cref="CType.SizeInWords"/>'s own remarks) -- so pointer arithmetic and array indexing never
/// need a scaling multiplication; <c>arr[i]</c> is simply "base address + i" via a plain
/// <c>add</c>.</description></item>
/// <item><description><c>switch</c>/<c>case</c> does NOT use a real <c>tjmp</c> (table jump) instruction:
/// unlike nodes 507/508/506/407/606, <c>tjmp</c>'s exact hardware encoding is not confirmed anywhere in
/// the project's documentation, and this compiler follows the project's own established practice of never
/// guessing an unconfirmed hardware encoding. Instead, a switch compiles to a straightforward,
/// always-correct chain of equality comparisons against the selector (evaluated once into a
/// compiler-allocated temporary local). This is flagged here as a future optimization pending Stefan
/// supplying <c>tjmp</c>'s real encoding -- see <see cref="EmitStatement"/>'s <see cref="CSwitchStmt"/>
/// case.</description></item>
/// </list>
/// </summary>
public sealed class CCodeGenerator
{
  private enum CVarKind
  {
    Local,
    Parameter,
    Global,
  }

  private sealed record CVarSymbol(CVarKind Kind, int Index, CType Type, string? GlobalLabel = null);

  private sealed class CFunctionSignature
  {
    public required CType ReturnType { get; init; }

    public required IReadOnlyList<CType> ParameterTypes { get; init; }

    /// <summary>ABI v2: one entry per parameter, parallel to <see cref="ParameterTypes"/> -- the
    /// address-register index (0-3) a register-eligible pointer parameter is passed in, or null for
    /// every other parameter (which keeps ABI v1's stack convention). See
    /// <see cref="ComputeParameterRegisterSlots"/> and this class's own ABI doc comment.</summary>
    public required IReadOnlyList<int?> ParameterRegisterSlots { get; init; }

    public bool IsDefined { get; set; }
  }

  private sealed class CGlobalSymbol
  {
    public required CType Type { get; init; }

    public required string Label { get; init; }

    public required bool IsStatic { get; init; }
  }

  private enum CLvalueKind
  {
    Local,
    Parameter,
    Indirect,
  }

  /// <summary>
  /// The resolved location of an assignable expression. Local/Parameter carry no stack side-effect --
  /// <see cref="Index"/> is just the frame offset for <see cref="EmitLoad"/>/<see cref="EmitStore"/> to
  /// use directly (<c>ldl</c>/<c>stl</c>/<c>ldp</c>/<c>stp</c> take the offset right in the opcode
  /// word). Indirect (a global, <c>*ptr</c>, or an array element) means <see cref="ResolveLvalue"/> has
  /// already computed the address ONCE and cached it into the local slot number <see cref="Index"/>
  /// refers to (via <see cref="CacheIndirectAddress"/>) -- NOT left sitting only in the CVM's "t"
  /// register, which has no save/restore and does not survive arbitrary code running in between (see
  /// this class's own doc comment). <see cref="EmitLoad"/>/<see cref="EmitStore"/> each reload that
  /// cached address and re-issue <c>xt</c> immediately before their own <c>ldt</c>/<c>stt</c>, so they
  /// stay correct no matter what runs between resolving the lvalue and using it.
  /// </summary>
  private sealed record CLvalue(CLvalueKind Kind, int Index, CType Type);

  private sealed class CFunctionContext
  {
    public required string Name { get; init; }

    public required CType ReturnType { get; init; }

    public required string EpilogueLabel { get; init; }

    public int NextLocalSlot { get; set; }

    public readonly List<Dictionary<string, CVarSymbol>> Scopes = [];

    public readonly Stack<string> BreakLabels = new();

    public readonly Stack<string> ContinueLabels = new();
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

  /// <summary>Generates CVM assembly text for <paramref name="unit"/>. Returns null (with at least one
  /// diagnostic) if any error was reported anywhere during generation; warnings alone still return the
  /// generated text.</summary>
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

    var text = new StringBuilder();
    text.AppendLine("; Generated by the GA144 C compiler -- do not edit by hand.");

    foreach (string import in _imports)
    {
      text.AppendLine($".import {import}");
    }

    text.AppendLine(".section DATA");
    foreach (string line in _dataLines)
    {
      text.AppendLine(line);
    }

    text.AppendLine(".section CODE");
    foreach (string line in _codeLines)
    {
      text.AppendLine(line);
    }

    return (text.ToString(), _diagnostics);
  }

  private void Error(CSourceLocation location, string message)
  {
    _diagnostics.Add(location.FormatDiagnostic("error", message));
    _hasError = true;
  }

  private string NewLabel(string hint) => $"__L{_labelCounter++}_{hint}";

  private void EmitCode(string line) => _codeLines.Add("  " + line);

  private void EmitLabel(string label) => _codeLines.Add($"{label}:");

  // ---------------------------------------------------------------------------------------------
  // Signature collection -- a first pass over the whole translation unit so a function or global may
  // be referenced before its own declaration appears (ordinary C forward-reference rules), without
  // this compiler needing a separate header/prototype mechanism.
  // ---------------------------------------------------------------------------------------------

  private void CollectSignatures(CTranslationUnit unit)
  {
    foreach (CFunctionDecl function in unit.Functions)
    {
      if (_functions.TryGetValue(function.Name, out CFunctionSignature? existing))
      {
        if (existing.IsDefined && function.Body is not null)
        {
          Error(function.Location, $"function \"{function.Name}\" is already defined");
          continue;
        }

        if (function.Body is not null)
        {
          existing.IsDefined = true;
        }

        continue;
      }

      IReadOnlyList<CType> parameterTypes = function.Parameters.Select(p => p.Type).ToList();
      _functions[function.Name] = new CFunctionSignature
      {
        ReturnType = function.ReturnType,
        ParameterTypes = parameterTypes,
        ParameterRegisterSlots = ComputeParameterRegisterSlots(parameterTypes),
        IsDefined = function.Body is not null,
      };
    }

    foreach (CGlobalVarDecl global in unit.Globals)
    {
      if (_globals.ContainsKey(global.Name))
      {
        Error(global.Location, $"global \"{global.Name}\" is already declared");
        continue;
      }

      _globals[global.Name] = new CGlobalSymbol { Type = global.Type, Label = global.Name, IsStatic = global.IsStatic };
    }
  }

  /// <summary>How many of a function's own pointer-typed parameters get an address register (ABI v2) --
  /// node 306 has exactly four (see <see cref="CvmInstructionSet"/>'s own <c>ldar</c>/<c>star</c>/
  /// <c>inca</c>/<c>deca</c>/<c>lda</c>/<c>sta</c> remarks).</summary>
  private const int MaxAddressRegisterParameters = 4;

  /// <summary>
  /// ABI v2: among <paramref name="parameterTypes"/>, in order, assigns the first
  /// <see cref="MaxAddressRegisterParameters"/> POINTER-typed ones an address-register index (0, 1, 2,
  /// 3, in the order their own pointer encountered) -- every other parameter (a later pointer, or any
  /// non-pointer parameter, wherever it falls) gets null, keeping ABI v1's stack convention. Computed
  /// once per signature, from parameter TYPES alone, so a call site can rely on it even for a function
  /// only declared (not yet defined) -- see this class's own ABI doc comment.
  /// </summary>
  private static IReadOnlyList<int?> ComputeParameterRegisterSlots(IReadOnlyList<CType> parameterTypes)
  {
    var slots = new int?[parameterTypes.Count];
    int pointerCount = 0;
    for (int i = 0; i < parameterTypes.Count; i++)
    {
      if (!parameterTypes[i].IsPointer)
      {
        continue;
      }

      if (pointerCount < MaxAddressRegisterParameters)
      {
        slots[i] = pointerCount;
      }

      pointerCount++;
    }

    return slots;
  }

  // ---------------------------------------------------------------------------------------------
  // Globals and the string-literal pool.
  // ---------------------------------------------------------------------------------------------

  private void EmitGlobal(CGlobalVarDecl global)
  {
    if (global.IsExtern)
    {
      // Declared here, defined elsewhere -- no storage, just a reference the linker resolves.
      _imports.Add(global.Name);
      return;
    }

    if (!global.IsStatic)
    {
      _dataLines.Add($".export {global.Name}");
    }

    List<string> words = ComputeGlobalInitialWords(global.Type, global.Initializer, global.Location);
    _dataLines.Add($"{global.Name}: .word {string.Join(", ", words)}");
  }

  /// <summary>Computes the DATA-section words for a global (or static-local) variable's initial value:
  /// one word per <see cref="CType.SizeInWords"/> slot, each either a decimal literal or a label name
  /// the assembler will resolve. An array with no initializer is zero-filled; a scalar with no
  /// initializer is a single zero word.</summary>
  private List<string> ComputeGlobalInitialWords(CType type, CExpr? initializer, CSourceLocation location)
  {
    int size = Math.Max(type.SizeInWords, 1);

    if (initializer is null)
    {
      return Enumerable.Repeat("0", size).ToList();
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

  /// <summary>Folds a global initializer expression to a single assembler-level word (a decimal literal
  /// or a label name) at compile time -- the only shapes a hand-written <c>.word</c> line itself
  /// accepts. No general constant-folding is attempted; only the handful of forms real C programs
  /// commonly use for a global's initializer.</summary>
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

      case CUnaryExpr { Op: CUnaryOp.AddressOf, Operand: CNameExpr name }:
        if (_globals.ContainsKey(name.Name))
        {
          word = name.Name;
          return true;
        }

        word = null;
        return false;

      case CNameExpr arrayName when _globals.TryGetValue(arrayName.Name, out CGlobalSymbol? symbol) && symbol.Type.IsArray:
        // A bare array name decays to its own address, same as everywhere else in C.
        word = arrayName.Name;
        return true;

      case CStringLiteralExpr stringLiteral:
        word = InternStringLiteral(stringLiteral.Value);
        return true;

      default:
        word = null;
        return false;
    }
  }

  /// <summary>Evaluates a constant integer expression at compile time (used for array bounds, case
  /// labels, and <c>sizeof</c>'s array-length interplay). Handles literals and the small set of integer
  /// operators macros commonly expand to; anything else is "not constant" rather than a hard error, so
  /// callers can report their own, more specific diagnostic.</summary>
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
        switch (binary.Op)
        {
          case CBinaryOp.Add: value = l + r; return true;
          case CBinaryOp.Subtract: value = l - r; return true;
          case CBinaryOp.Multiply: value = l * r; return true;
          case CBinaryOp.Divide when r != 0: value = l / r; return true;
          case CBinaryOp.Modulo when r != 0: value = l % r; return true;
          case CBinaryOp.BitwiseAnd: value = l & r; return true;
          case CBinaryOp.BitwiseOr: value = l | r; return true;
          case CBinaryOp.BitwiseXor: value = l ^ r; return true;
          case CBinaryOp.ShiftLeft: value = l << (int)r; return true;
          case CBinaryOp.ShiftRight: value = l >> (int)r; return true;
          default: value = 0; return false;
        }

      default:
        value = 0;
        return false;
    }
  }

  /// <summary>Interns a string literal as an anonymous global <c>char[]</c>, reusing the same label for
  /// an identical literal seen more than once. Actual DATA-section emission happens once, in
  /// <see cref="EmitStringPool"/>, after every function has been compiled.</summary>
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
      IEnumerable<string> words = bytes.Select(b => ((int)b).ToString()).Append("0");
      _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Functions, scopes, and local variables.
  // ---------------------------------------------------------------------------------------------

  private void EmitFunction(CFunctionDecl function)
  {
    if (!function.IsStatic)
    {
      _codeLines.Add($".export {function.Name}");
    }

    _codeLines.Add($"{function.Name}:");

    var context = new CFunctionContext { Name = function.Name, ReturnType = function.ReturnType, EpilogueLabel = NewLabel("epilogue") };
    context.Scopes.Add(new Dictionary<string, CVarSymbol>(StringComparer.Ordinal));

    // ABI v2: CollectSignatures already ran over the whole translation unit, so this function's own
    // signature (and its ParameterRegisterSlots) is already sitting in _functions -- reuse it rather
    // than recomputing, so a call site and this function's own prologue can never disagree about which
    // parameters are register-eligible.
    CFunctionSignature signature = _functions[function.Name];
    int stackParameterIndex = 0;
    var registerParameters = new List<(int RegisterIndex, int LocalSlot)>();
    for (int i = 0; i < function.Parameters.Count; i++)
    {
      CParameter parameter = function.Parameters[i];
      int? registerIndex = i < signature.ParameterRegisterSlots.Count ? signature.ParameterRegisterSlots[i] : null;
      if (registerIndex is int assignedRegister)
      {
        // Register-eligible: this parameter never arrives via the stack at all, so it gets an ordinary
        // LOCAL slot instead of a Parameter-kind frame offset -- see this class's own ABI doc comment.
        int localSlot = context.NextLocalSlot++;
        context.Scopes[0][parameter.Name] = new CVarSymbol(CVarKind.Local, localSlot, parameter.Type);
        registerParameters.Add((assignedRegister, localSlot));
      }
      else
      {
        // Stack-passed, same as ABI v1 -- but renumbered to count only the parameters that are ACTUALLY
        // pushed, since any register-eligible ones earlier in the declaration are simply absent from the
        // caller's own push sequence.
        context.Scopes[0][parameter.Name] = new CVarSymbol(CVarKind.Parameter, stackParameterIndex, parameter.Type);
        stackParameterIndex++;
      }
    }

    int enterLineIndex = _codeLines.Count;
    _codeLines.Add(string.Empty); // patched below once the body's local-slot count is known.

    // ABI v2 prologue: spill every register-eligible parameter out of its (volatile, caller-set)
    // address register into its own ordinary local slot, immediately -- before the body runs and before
    // this function makes any call of its own that could clobber the register. See this class's own ABI
    // doc comment for why the value's ONLY safe lifetime inside the register is this one moment.
    foreach ((int registerIndex, int localSlot) in registerParameters)
    {
      EmitCode($"sta {registerIndex}"); // r := this register's address word; stack.push(its page word)
      EmitCode("push");                 // stack: [..., page, address] -- duplicates r's value onto the stack
      EmitCode($"stl {localSlot}");     // pops "address" (the value we want) into the parameter's own local slot
      EmitCode("pop");                  // pops the leftover page word into r and discards it (page is always 0 -- see this class's own ABI doc comment on far pointers not being supported yet)
    }

    _currentFunction = context;
    EmitStatement(function.Body!);
    _currentFunction = null;

    _codeLines.Add($"{context.EpilogueLabel}:");
    _codeLines.Add("  leave");
    _codeLines.Add("  ret");
    _codeLines[enterLineIndex] = $"  enter {context.NextLocalSlot}";
  }

  private void EnterScope() => _currentFunction!.Scopes.Add(new Dictionary<string, CVarSymbol>(StringComparer.Ordinal));

  private void ExitScope() => _currentFunction!.Scopes.RemoveAt(_currentFunction.Scopes.Count - 1);

  private CVarSymbol? LookupVariable(string name)
  {
    List<Dictionary<string, CVarSymbol>> scopes = _currentFunction!.Scopes;
    for (int i = scopes.Count - 1; i >= 0; i--)
    {
      if (scopes[i].TryGetValue(name, out CVarSymbol? symbol))
      {
        return symbol;
      }
    }

    return null;
  }

  /// <summary>Declares a local variable in the innermost active scope. A non-static array reserves
  /// <see cref="CType.SizeInWords"/> consecutive local slots (every other type reserves exactly one,
  /// since every scalar/pointer is one word); a "static" local gets the same one-per-program storage and
  /// lifetime as a global, under a mangled name only this function's scope can see, with its initializer
  /// baked into the DATA section once rather than executed as runtime code each time control reaches the
  /// declaration.</summary>
  private void DeclareLocal(CLocalVarDecl decl)
  {
    if (_currentFunction!.Scopes[^1].ContainsKey(decl.Name))
    {
      Error(decl.Location, $"\"{decl.Name}\" is already declared in this scope");
      return;
    }

    if (decl.IsStatic)
    {
      string label = $"__static_{_currentFunction!.Name}_{decl.Name}_{_staticLocalCounter++}";
      List<string> words = ComputeGlobalInitialWords(decl.Type, decl.Initializer, decl.Location);
      _dataLines.Add($"{label}: .word {string.Join(", ", words)}");
      _currentFunction.Scopes[^1][decl.Name] = new CVarSymbol(CVarKind.Global, 0, decl.Type, label);
      return;
    }

    int slot = _currentFunction!.NextLocalSlot;
    _currentFunction.NextLocalSlot += Math.Max(decl.Type.SizeInWords, 1);
    _currentFunction.Scopes[^1][decl.Name] = new CVarSymbol(CVarKind.Local, slot, decl.Type);
  }

  // ---------------------------------------------------------------------------------------------
  // Lvalues: the single mechanism plain assignment, compound assignment, and pre/post increment and
  // decrement all share.
  //
  // IMPORTANT correctness note (found in review, 2026-09-06): the CVM's "t" register has no save/
  // restore of its own, so it is NOT safe to assume it survives arbitrary code emitted between
  // resolving an Indirect lvalue's address and actually loading/storing through it -- e.g. in
  // "globalA = globalB;", resolving globalA's address latches t, but evaluating the RHS globalB reads
  // a global too, which does its own "xt" and silently repoints t at globalB before the store runs.
  // The fix: an Indirect lvalue's address is computed exactly ONCE (preserving CCompoundAssignExpr's
  // own "do not re-run the target's side effects" contract) but immediately cached into a dedicated
  // local slot (ldl/stl never touch t or r), not left sitting only in t. EmitLoad/EmitStore then each
  // independently reload that cached address and re-issue "xt" right before their own ldt/stt --
  // "ldl addressSlot; xt" is a pure, side-effect-free re-derivation of t from the cache, not a
  // re-evaluation of the original target expression, so this is safe to repeat as many times as
  // needed (compound assignment loads once and stores once; increment/decrement do the same) no
  // matter what runs in between.
  // ---------------------------------------------------------------------------------------------

  private CLvalue ResolveLvalue(CExpr expr)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is { Kind: CVarKind.Local })
          {
            return new CLvalue(CLvalueKind.Local, symbol.Index, symbol.Type);
          }

          if (symbol is { Kind: CVarKind.Parameter })
          {
            return new CLvalue(CLvalueKind.Parameter, symbol.Index, symbol.Type);
          }

          string label = symbol?.GlobalLabel ?? name.Name;
          CType type;
          if (symbol is { Kind: CVarKind.Global })
          {
            type = symbol.Type;
          }
          else if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            type = global.Type;
          }
          else if (_functions.ContainsKey(name.Name))
          {
            Error(expr.Location, $"\"{name.Name}\" is a function and cannot be assigned to");
            type = CType.Int;
          }
          else
          {
            Error(expr.Location, $"\"{name.Name}\" is not declared");
            type = CType.Int;
          }

          if (!_globals.ContainsKey(name.Name) && symbol is not { Kind: CVarKind.Global })
          {
            _imports.Add(label);
          }

          EmitCode($"pushlit {label}");
          return CacheIndirectAddress(type);
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        {
          CType pointerType = EmitExpr(deref.Operand);
          CType pointeeType = pointerType.IsPointer ? pointerType.ElementType! : pointerType.Decay() is { IsPointer: true } decayed ? decayed.ElementType! : CType.Int;
          if (!pointerType.IsPointer)
          {
            Error(expr.Location, $"cannot dereference a value of type \"{pointerType}\"");
          }

          return CacheIndirectAddress(pointeeType);
        }

      case CIndexExpr index:
        {
          CType elementType = EmitAddressOfIndex(index);
          return CacheIndirectAddress(elementType);
        }

      default:
        Error(expr.Location, "expression is not assignable");
        EmitCode("pushlit 0");
        return CacheIndirectAddress(CType.Int);
    }
  }

  /// <summary>Given an address already sitting on top of the stack, caches a copy of it into a fresh
  /// local slot (so it can be safely re-derived later regardless of what runs in between -- see this
  /// section's own remarks) and consumes the original into "t" via <c>xt</c>, leaving the stack exactly
  /// as it was before the address was pushed.</summary>
  private CLvalue CacheIndirectAddress(CType type)
  {
    int addressSlot = _currentFunction!.NextLocalSlot++;
    EmitDup();
    EmitCode($"stl {addressSlot}");
    EmitCode("xt");
    return new CLvalue(CLvalueKind.Indirect, addressSlot, type);
  }

  private void EmitLoad(CLvalue lvalue)
  {
    switch (lvalue.Kind)
    {
      case CLvalueKind.Local:
        EmitCode($"ldl {lvalue.Index}");
        break;
      case CLvalueKind.Parameter:
        EmitCode($"ldp {lvalue.Index}");
        break;
      default:
        EmitCode($"ldl {lvalue.Index}");
        EmitCode("xt");
        EmitCode("ldt");
        break;
    }
  }

  private void EmitStore(CLvalue lvalue)
  {
    switch (lvalue.Kind)
    {
      case CLvalueKind.Local:
        EmitCode($"stl {lvalue.Index}");
        break;
      case CLvalueKind.Parameter:
        EmitCode($"stp {lvalue.Index}");
        break;
      default:
        EmitCode($"ldl {lvalue.Index}");
        EmitCode("xt");
        EmitCode("stt");
        break;
    }
  }

  /// <summary>Duplicates the current stack top: "pop" into r, then "push; push" -- the first push
  /// restores the original value, the second leaves a copy above it. Net stack effect: +1.</summary>
  private void EmitDup()
  {
    EmitCode("pop");
    EmitCode("push");
    EmitCode("push");
  }

  /// <summary>Computes and pushes the address of an lvalue expression, WITHOUT latching it into "t"
  /// (unlike <see cref="ResolveLvalue"/>) -- this is the address-of ("&amp;") operator's own codegen, and
  /// also <see cref="CIndexExpr"/>'s helper for computing an element's address before <c>xt</c>.</summary>
  private CType EmitAddressOf(CExpr expr)
  {
    switch (expr)
    {
      case CNameExpr name:
        {
          CVarSymbol? symbol = LookupVariable(name.Name);
          if (symbol is { Kind: CVarKind.Local })
          {
            EmitCode($"lal {symbol.Index}");
            return CType.PointerTo(symbol.Type);
          }

          if (symbol is { Kind: CVarKind.Parameter })
          {
            EmitCode($"lap {symbol.Index}");
            return CType.PointerTo(symbol.Type);
          }

          string label = symbol?.GlobalLabel ?? name.Name;
          CType type;
          if (symbol is { Kind: CVarKind.Global })
          {
            // A static local -- its storage is already emitted under this mangled label, no import needed.
            type = symbol.Type;
          }
          else if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
          {
            // A real global -- EmitGlobal already declared it (as storage or, for "extern", an import).
            type = global.Type;
          }
          else
          {
            Error(expr.Location, $"\"{name.Name}\" is not declared");
            type = CType.Int;
          }

          EmitCode($"pushlit {label}");
          return CType.PointerTo(type);
        }

      case CUnaryExpr { Op: CUnaryOp.Dereference } deref:
        // "&*p" is just "p" -- evaluate the pointer expression directly, no extra indirection.
        return EmitExpr(deref.Operand);

      case CIndexExpr index:
        return CType.PointerTo(EmitAddressOfIndex(index));

      default:
        Error(expr.Location, "cannot take the address of this expression");
        EmitCode("pushlit 0");
        return CType.PointerTo(CType.Int);
    }
  }

  /// <summary>Pushes the address of <c>Base[Index]</c> (base address + index; no scaling multiplication
  /// -- see this class's own doc comment) and returns the element's type.</summary>
  private CType EmitAddressOfIndex(CIndexExpr index)
  {
    CType baseType = EmitExpr(index.Base).Decay();
    if (!baseType.IsPointer)
    {
      Error(index.Location, $"cannot index into a value of type \"{baseType}\"");
      baseType = CType.PointerTo(CType.Int);
    }

    EmitExpr(index.Index);
    EmitCode("pop");
    EmitCode("add");
    return baseType.ElementType!;
  }

  private CType EmitAssign(CAssignExpr assign)
  {
    CLvalue target = ResolveLvalue(assign.Target);
    EmitExpr(assign.Value);
    EmitDup();
    EmitStore(target);
    return target.Type;
  }

  private CType EmitCompoundAssign(CCompoundAssignExpr expr)
  {
    CLvalue target = ResolveLvalue(expr.Target);
    EmitLoad(target);
    CType rhsType = EmitExpr(expr.Value);
    // EmitBinaryOperation itself performs the single "pop" the ABI's binary-op pattern needs (it
    // expects both operands already sitting on the stack, lhs then rhs) -- do not pop again here.
    CType resultType = EmitBinaryOperation(expr.Op, target.Type, rhsType, expr.Location);
    EmitDup();
    EmitStore(target);
    return resultType;
  }

  /// <summary>Pre/post increment and decrement, both built on the same lvalue primitives. Pre- dups the
  /// NEW value (computed, then duplicated, then stored -- result is the value now in memory); post- dups
  /// the OLD value immediately after loading it, before computing the new one, so the expression's own
  /// result is the value memory held before the update. Both leave exactly one word on the stack, per
  /// this class's own uniform codegen invariant.</summary>
  private CType EmitIncrementOrDecrement(CUnaryExpr expr)
  {
    bool isIncrement = expr.Op is CUnaryOp.PreIncrement or CUnaryOp.PostIncrement;
    bool isPre = expr.Op is CUnaryOp.PreIncrement or CUnaryOp.PreDecrement;
    CBinaryOp op = isIncrement ? CBinaryOp.Add : CBinaryOp.Subtract;

    CLvalue target = ResolveLvalue(expr.Operand);

    // EmitBinaryOperation performs its own single "pop" (both operands are already on the stack by the
    // time it runs, lhs then rhs) -- neither branch below pops again first.
    if (isPre)
    {
      EmitLoad(target);          // stack: [old]
      EmitCode("pushlit 1");     // stack: [old, 1]
      EmitBinaryOperation(op, target.Type, CType.Int, expr.Location); // stack: [new]
      EmitDup();                 // stack: [new, new]
      EmitStore(target);         // stack: [new]  -- expression result is the NEW value.
    }
    else
    {
      EmitLoad(target);          // stack: [old]
      EmitDup();                 // stack: [old, old]
      EmitCode("pushlit 1");     // stack: [old, old, 1]
      EmitBinaryOperation(op, target.Type, CType.Int, expr.Location); // stack: [old, new]
      EmitStore(target);         // stack: [old]  -- expression result is the OLD value.
    }

    return target.Type;
  }

  // ---------------------------------------------------------------------------------------------
  // Binary operations.
  // ---------------------------------------------------------------------------------------------

  private static CType ComputeCommonType(CType a, CType b) => a.IsUnsigned || b.IsUnsigned ? CType.UnsignedInt : CType.Int;

  private CType EmitBinaryOperation(CBinaryOp op, CType leftType, CType rightType, CSourceLocation location)
  {
    switch (op)
    {
      case CBinaryOp.Add:
        EmitCode("pop");
        EmitCode("add");
        if (leftType.IsPointer && !rightType.IsPointer)
        {
          return leftType;
        }

        if (rightType.IsPointer && !leftType.IsPointer)
        {
          return rightType;
        }

        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.Subtract:
        EmitCode("pop");
        EmitCode("sub");
        return leftType.IsPointer && rightType.IsPointer ? CType.Int : leftType.IsPointer ? leftType : ComputeCommonType(leftType, rightType);

      case CBinaryOp.Multiply:
        return EmitLibraryBinary(leftType, rightType, "__mul", location);

      case CBinaryOp.Divide:
        return EmitLibraryBinary(leftType, rightType, ComputeCommonType(leftType, rightType).IsUnsigned ? "__divu" : "__divs", location);

      case CBinaryOp.Modulo:
        return EmitLibraryBinary(leftType, rightType, ComputeCommonType(leftType, rightType).IsUnsigned ? "__modu" : "__mods", location);

      case CBinaryOp.BitwiseAnd:
        EmitCode("pop");
        EmitCode("and");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.BitwiseOr:
        EmitCode("pop");
        EmitCode("or");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.BitwiseXor:
        EmitCode("pop");
        EmitCode("xor");
        return ComputeCommonType(leftType, rightType);

      case CBinaryOp.ShiftLeft:
        EmitCode("pop");
        EmitCode("usl");
        return leftType;

      case CBinaryOp.ShiftRight:
        EmitCode("pop");
        EmitCode(leftType.IsUnsigned ? "usr" : "ssr");
        return leftType;

      case CBinaryOp.Equal:
        EmitCode("pop");
        EmitCode("eq");
        return CType.Int;

      case CBinaryOp.NotEqual:
        EmitCode("pop");
        EmitCode("ne");
        return CType.Int;

      case CBinaryOp.Less:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "ult" : "lt");
        return CType.Int;

      case CBinaryOp.Greater:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "ugt" : "gt");
        return CType.Int;

      case CBinaryOp.LessOrEqual:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "ule" : "le");
        return CType.Int;

      case CBinaryOp.GreaterOrEqual:
        EmitCode("pop");
        EmitCode(ComputeCommonType(leftType, rightType).IsUnsigned ? "uge" : "ge");
        return CType.Int;

      default:
        Error(location, $"operator \"{op}\" is not yet supported by the code generator");
        EmitCode("pop");
        return CType.Int;
    }
  }

  /// <summary>Emits a call to one of the hand-written CVM runtime library routines that back
  /// <c>*</c>/<c>/</c>/<c>%</c> (no hardware primitive exists for any of them -- see this class's own
  /// doc comment). By the time this runs, both operands are already on the stack (pushed left-to-right
  /// by <see cref="EmitExpr"/>'s <see cref="CBinaryExpr"/> case or by
  /// <see cref="EmitCompoundAssign"/>); the callee consumes both and pushes exactly one result
  /// word.</summary>
  private CType EmitLibraryBinary(CType leftType, CType rightType, string functionName, CSourceLocation location)
  {
    if (leftType.IsPointer || rightType.IsPointer)
    {
      Error(location, $"\"{functionName}\" is not defined for a pointer operand");
    }

    _imports.Add(functionName);
    EmitCode($"call {functionName}");
    return ComputeCommonType(leftType, rightType);
  }

  /// <summary>Short-circuit "&amp;&amp;"/"||", both branching on the assumed ifbr-branches-if-nonzero
  /// polarity documented on this class. "a &amp;&amp; b": evaluate a; if a is false, short-circuit
  /// straight to a result of 0 without ever evaluating b; otherwise evaluate b and coerce it to exactly
  /// 0 or 1 (matching C's own &amp;&amp;/|| result type). "a || b" mirrors this: if a is true,
  /// short-circuit straight to 1; otherwise evaluate and coerce b the same way.</summary>
  private CType EmitLogical(CBinaryExpr expr)
  {
    bool isAnd = expr.Op == CBinaryOp.LogicalAnd;
    string shortCircuitLabel = NewLabel(isAnd ? "and_false" : "or_true");
    string doneLabel = NewLabel(isAnd ? "and_done" : "or_done");

    EmitExpr(expr.Left);
    EmitCode("pop");
    EmitCode(isAnd ? "eq0" : "ne0"); // && short-circuits when left is false (==0); || when left is true (!=0).
    EmitCode($"ifbr {shortCircuitLabel}");

    EmitExpr(expr.Right);
    EmitCode("pop");
    EmitCode("ne0");
    EmitCode("push");
    EmitCode($"br {doneLabel}");

    EmitLabel(shortCircuitLabel);
    EmitCode($"pushlit {(isAnd ? 0 : 1)}");

    EmitLabel(doneLabel);
    return CType.Int;
  }

  // ---------------------------------------------------------------------------------------------
  // Statements.
  // ---------------------------------------------------------------------------------------------

  private void EmitStatement(CStmt stmt)
  {
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
        {
          CType type = EmitExpr(exprStmt.Expression);
          if (!type.IsVoid)
          {
            EmitCode("pop");
          }

          break;
        }

      case CLocalVarDecl localDecl:
        DeclareLocal(localDecl);
        if (localDecl.Initializer is not null && !localDecl.IsStatic)
        {
          EmitAssign(new CAssignExpr(localDecl.Location, new CNameExpr(localDecl.Location, localDecl.Name), localDecl.Initializer));
          EmitCode("pop");
        }

        break;

      case CIfStmt ifStmt:
        {
          string elseLabel = NewLabel("else");
          string endLabel = NewLabel("endif");
          EmitExpr(ifStmt.Condition);
          EmitCode("pop");
          EmitCode($"eq0");
          EmitCode($"ifbr {elseLabel}");
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
          _currentFunction!.BreakLabels.Push(endLabel);
          _currentFunction.ContinueLabels.Push(startLabel);
          EmitLabel(startLabel);
          EmitExpr(whileStmt.Condition);
          EmitCode("pop");
          EmitCode("eq0");
          EmitCode($"ifbr {endLabel}");
          EmitStatement(whileStmt.Body);
          EmitCode($"br {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.BreakLabels.Pop();
          _currentFunction.ContinueLabels.Pop();
          break;
        }

      case CDoWhileStmt doWhileStmt:
        {
          string startLabel = NewLabel("dowhile");
          string continueLabel = NewLabel("dowhile_cont");
          string endLabel = NewLabel("enddowhile");
          _currentFunction!.BreakLabels.Push(endLabel);
          _currentFunction.ContinueLabels.Push(continueLabel);
          EmitLabel(startLabel);
          EmitStatement(doWhileStmt.Body);
          EmitLabel(continueLabel);
          EmitExpr(doWhileStmt.Condition);
          EmitCode("pop");
          EmitCode($"ifbr {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.BreakLabels.Pop();
          _currentFunction.ContinueLabels.Pop();
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

          _currentFunction!.BreakLabels.Push(endLabel);
          _currentFunction.ContinueLabels.Push(continueLabel);
          EmitLabel(startLabel);
          if (forStmt.Condition is not null)
          {
            EmitExpr(forStmt.Condition);
            EmitCode("pop");
            EmitCode("eq0");
            EmitCode($"ifbr {endLabel}");
          }

          EmitStatement(forStmt.Body);
          EmitLabel(continueLabel);
          if (forStmt.Update is not null)
          {
            CType updateType = EmitExpr(forStmt.Update);
            if (!updateType.IsVoid)
            {
              EmitCode("pop");
            }
          }

          EmitCode($"br {startLabel}");
          EmitLabel(endLabel);
          _currentFunction.BreakLabels.Pop();
          _currentFunction.ContinueLabels.Pop();
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

            EmitExpr(returnStmt.Value);
          }
          else if (!_currentFunction!.ReturnType.IsVoid)
          {
            Error(returnStmt.Location, $"function \"{_currentFunction.Name}\" must return a value of type \"{_currentFunction.ReturnType}\"");
          }

          EmitCode($"br {_currentFunction!.EpilogueLabel}");
          break;
        }

      case CBreakStmt breakStmt:
        if (_currentFunction!.BreakLabels.Count == 0)
        {
          Error(breakStmt.Location, "\"break\" is not within a loop or switch");
          break;
        }

        EmitCode($"br {_currentFunction.BreakLabels.Peek()}");
        break;

      case CContinueStmt continueStmt:
        if (_currentFunction!.ContinueLabels.Count == 0)
        {
          Error(continueStmt.Location, "\"continue\" is not within a loop");
          break;
        }

        EmitCode($"br {_currentFunction.ContinueLabels.Peek()}");
        break;

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

  /// <summary>
  /// Compiles a <c>switch</c> without a real <c>tjmp</c> instruction -- see this class's own doc comment
  /// for why. The selector is evaluated exactly once into a compiler-allocated temporary local, then a
  /// chain of equality comparisons (in source order, "default" last regardless of where it was written,
  /// matching the order real hardware would want to test them in) jumps into the right position; the
  /// body then runs as ordinary statements, with each <see cref="CCaseLabelStmt"/>/
  /// <see cref="CDefaultLabelStmt"/> becoming a label the comparison chain already jumps to -- giving
  /// real C fall-through for free, since nothing here stops execution from running from one case
  /// straight into the next.
  /// </summary>
  private void EmitSwitch(CSwitchStmt switchStmt)
  {
    string endLabel = NewLabel("endswitch");
    var labels = new Dictionary<CStmt, string>(ReferenceEqualityComparer.Instance);
    var cases = new List<(long Value, string Label)>();
    string? defaultLabel = null;
    CollectSwitchLabels(switchStmt.Body, labels, cases, ref defaultLabel, switchStmt.Location);

    // Evaluate the selector exactly once and store it (per this class's own ldl/stl-act-directly-on-the-
    // stack assumption -- see the class doc comment -- "stl" alone both pops and stores, no separate
    // register hop needed).
    EmitExpr(switchStmt.Selector);
    int selectorSlot = _currentFunction!.NextLocalSlot++;
    EmitCode($"stl {selectorSlot}");

    foreach ((long value, string label) in cases)
    {
      EmitCode($"ldl {selectorSlot}");   // stack: [selector]
      EmitCode($"pushlit {value}");      // stack: [selector, value]
      EmitCode("pop");                    // r := value; stack: [selector]
      EmitCode("eq");                     // stack: [selector == value]
      EmitCode("pop");                    // r := comparison result; stack: []
      EmitCode($"ifbr {label}");          // branch into this case when the comparison was true.
    }

    EmitCode($"br {defaultLabel ?? endLabel}");

    // Save/restore rather than clobbering to null: a nested switch inside this one's body recursively
    // calls EmitSwitch, which would otherwise permanently wipe out THIS switch's own label table for
    // any case/default label that textually follows the nested switch.
    Dictionary<CStmt, string>? outerSwitchLabels = _switchLabelsByNode;
    _switchLabelsByNode = labels;
    _currentFunction.BreakLabels.Push(endLabel);
    EmitStatement(switchStmt.Body);
    _currentFunction.BreakLabels.Pop();
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

      case CLabelStmt labelStmt:
        CollectSwitchLabels(labelStmt.Inner, labels, cases, ref defaultLabel, switchLocation);
        break;

      // A case/default label may be nested inside a loop that's itself inside the switch's body (the
      // Duff's-device shape, among others) -- descend into every loop kind's own Body so those are
      // still collected, exactly as CSwitchStmt's own doc comment (CAstStmt.cs) promises.
      case CWhileStmt whileStmt:
        CollectSwitchLabels(whileStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CDoWhileStmt doWhileStmt:
        CollectSwitchLabels(doWhileStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CForStmt forStmt:
        CollectSwitchLabels(forStmt.Body, labels, cases, ref defaultLabel, switchLocation);
        break;

      case CSwitchStmt:
        // A nested switch owns its own case/default labels -- do not descend into it.
        break;

      default:
        break;
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Expressions. Every case leaves exactly one stack word (this class's own uniform codegen
  // invariant), except a call to a void function, which leaves nothing.
  // ---------------------------------------------------------------------------------------------

  private CType EmitExpr(CExpr expr)
  {
    switch (expr)
    {
      case CIntLiteralExpr literal:
        EmitCode($"pushlit {literal.Value}");
        return literal.IsUnsigned ? CType.UnsignedInt : CType.Int;

      case CStringLiteralExpr stringLiteral:
        EmitCode($"pushlit {InternStringLiteral(stringLiteral.Value)}");
        return CType.PointerTo(CType.Char);

      case CNameExpr name:
        return EmitName(name);

      case CUnaryExpr { Op: CUnaryOp.Plus } unary:
        return EmitExpr(unary.Operand);

      case CUnaryExpr { Op: CUnaryOp.Minus } unary:
        {
          CType type = EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("negate");
          EmitCode("push");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.LogicalNot } unary:
        {
          EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("eq0");
          EmitCode("push");
          return CType.Int;
        }

      case CUnaryExpr { Op: CUnaryOp.BitwiseNot } unary:
        {
          CType type = EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("inv");
          EmitCode("push");
          return type;
        }

      case CUnaryExpr { Op: CUnaryOp.AddressOf } unary:
        return EmitAddressOf(unary.Operand);

      case CUnaryExpr { Op: CUnaryOp.Dereference } unary:
        {
          CLvalue lvalue = ResolveLvalue(unary);
          EmitLoad(lvalue);
          return lvalue.Type;
        }

      case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostIncrement or CUnaryOp.PostDecrement } incDec:
        return EmitIncrementOrDecrement(incDec);

      case CBinaryExpr { Op: CBinaryOp.LogicalAnd or CBinaryOp.LogicalOr } logical:
        return EmitLogical(logical);

      case CBinaryExpr binary:
        {
          CType leftType = EmitExpr(binary.Left);
          CType rightType = EmitExpr(binary.Right);
          return EmitBinaryOperation(binary.Op, leftType, rightType, binary.Location);
        }

      case CAssignExpr assign:
        return EmitAssign(assign);

      case CCompoundAssignExpr compoundAssign:
        return EmitCompoundAssign(compoundAssign);

      case CConditionalExpr conditional:
        return EmitConditional(conditional);

      case CCallExpr call:
        return EmitCall(call);

      case CIndexExpr index:
        {
          CLvalue lvalue = ResolveLvalue(index);
          EmitLoad(lvalue);
          return lvalue.Type;
        }

      case CCastExpr cast:
        // Every supported type is one word, so a cast is a pure reinterpretation: emit the operand and
        // relabel its type, no runtime conversion needed.
        EmitExpr(cast.Operand);
        return cast.TargetType;

      case CSizeOfTypeExpr sizeOfType:
        EmitCode($"pushlit {sizeOfType.Type.SizeInWords}");
        return CType.UnsignedInt;

      case CSizeOfExprExpr sizeOfExpr:
        {
          // Deviation from strict C semantics, documented in the design doc: the operand is evaluated at
          // runtime and discarded rather than being purely a compile-time type inspection. Safe as long
          // as the operand has no side effects.
          CType operandType = EmitExpr(sizeOfExpr.Operand);
          if (operandType.IsVoid)
          {
            // Already-invalid C ("sizeof" of a void-returning call) -- report it instead of silently
            // popping whatever value happens to be sitting under a stack slot that was never pushed.
            Error(sizeOfExpr.Location, "\"sizeof\" cannot be applied to a value of type \"void\"");
          }
          else
          {
            EmitCode("pop");
          }

          EmitCode($"pushlit {Math.Max(operandType.SizeInWords, 1)}");
          return CType.UnsignedInt;
        }

      case CCommaExpr comma:
        {
          CType leftType = EmitExpr(comma.Left);
          if (!leftType.IsVoid)
          {
            EmitCode("pop");
          }

          return EmitExpr(comma.Right);
        }

      default:
        Error(expr.Location, $"expression kind \"{expr.GetType().Name}\" is not yet supported by the code generator");
        EmitCode("pushlit 0");
        return CType.Int;
    }
  }

  private CType EmitName(CNameExpr name)
  {
    CVarSymbol? symbol = LookupVariable(name.Name);

    if (symbol is { Kind: CVarKind.Local })
    {
      if (symbol.Type.IsArray)
      {
        EmitCode($"lal {symbol.Index}");
        return symbol.Type.Decay();
      }

      EmitCode($"ldl {symbol.Index}");
      return symbol.Type;
    }

    if (symbol is { Kind: CVarKind.Parameter })
    {
      if (symbol.Type.IsArray)
      {
        EmitCode($"lap {symbol.Index}");
        return symbol.Type.Decay();
      }

      EmitCode($"ldp {symbol.Index}");
      return symbol.Type;
    }

    string? staticLabel = symbol is { Kind: CVarKind.Global } ? symbol.GlobalLabel : null;
    if (staticLabel is not null)
    {
      if (symbol!.Type.IsArray)
      {
        EmitCode($"pushlit {staticLabel}");
        return symbol.Type.Decay();
      }

      EmitCode($"pushlit {staticLabel}");
      EmitCode("xt");
      EmitCode("ldt");
      return symbol.Type;
    }

    if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
    {
      if (global.Type.IsArray)
      {
        EmitCode($"pushlit {name.Name}");
        return global.Type.Decay();
      }

      EmitCode($"pushlit {name.Name}");
      EmitCode("xt");
      EmitCode("ldt");
      return global.Type;
    }

    if (_functions.ContainsKey(name.Name))
    {
      Error(name.Location, $"\"{name.Name}\" is a function -- did you mean to call it?");
      EmitCode("pushlit 0");
      return CType.Int;
    }

    // Not defined in this file -- assume an external global the linker will resolve, per this
    // compiler's "missing pieces become an .import" rule.
    _imports.Add(name.Name);
    EmitCode($"pushlit {name.Name}");
    EmitCode("xt");
    EmitCode("ldt");
    return CType.Int;
  }

  private CType EmitConditional(CConditionalExpr conditional)
  {
    string elseLabel = NewLabel("condfalse");
    string endLabel = NewLabel("condend");

    EmitExpr(conditional.Condition);
    EmitCode("pop");
    EmitCode("eq0");
    EmitCode($"ifbr {elseLabel}");
    CType trueType = EmitExpr(conditional.WhenTrue);
    EmitCode($"br {endLabel}");
    EmitLabel(elseLabel);
    CType falseType = EmitExpr(conditional.WhenFalse);
    EmitLabel(endLabel);

    return trueType.IsPointer ? trueType : falseType.IsPointer ? falseType : ComputeCommonType(trueType, falseType);
  }

  private CType EmitCall(CCallExpr call)
  {
    if (!_functions.TryGetValue(call.FunctionName, out CFunctionSignature? signature))
    {
      Error(call.Location, $"\"{call.FunctionName}\" is not declared");
      _imports.Add(call.FunctionName);
      foreach (CExpr argument in call.Arguments)
      {
        // Arguments stay on the stack for the callee, exactly like a normal call -- do not pop them.
        EmitExpr(argument);
      }

      EmitCode($"call {call.FunctionName}");
      return CType.Int;
    }

    if (signature.ParameterTypes.Count != call.Arguments.Count)
    {
      Error(call.Location, $"\"{call.FunctionName}\" expects {signature.ParameterTypes.Count} argument(s), but {call.Arguments.Count} were given");
    }

    // ABI v2: an argument landing in one of node 306's address registers is evaluated exactly like any
    // other argument (so side effects and evaluation order never change), but its result is then popped
    // back off the stack and loaded into the assigned address register instead of being left there for
    // the callee to find with ldp/stp. The register is loaded with "lda", which per node 306's own
    // opcode table takes the address word from r and the page word from the CVM stack top -- so the
    // sequence is: pop the just-pushed address into r, push a literal page word of 0 (far/32-bit
    // pointers spanning pages are not yet supported -- see this file's own ABI v2 doc-comment remarks),
    // then "lda <register>" consumes both. This mirrors the callee-side prologue spill in EmitFunction,
    // which immediately re-spills the same register back onto the stack/into a local slot -- the
    // register itself is never assumed to survive anything but this one handoff.
    for (int i = 0; i < call.Arguments.Count; i++)
    {
      EmitExpr(call.Arguments[i]);

      int? registerIndex = i < signature.ParameterRegisterSlots.Count ? signature.ParameterRegisterSlots[i] : null;
      if (registerIndex is int assignedRegister)
      {
        EmitCode("pop");
        EmitCode("pushlit 0");
        EmitCode($"lda {assignedRegister}");
      }
    }

    if (!signature.IsDefined)
    {
      _imports.Add(call.FunctionName);
    }

    EmitCode($"call {call.FunctionName}");
    return signature.ReturnType;
  }
}