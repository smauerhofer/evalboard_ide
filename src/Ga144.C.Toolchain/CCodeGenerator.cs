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
/// IMPORTANT: "t" has no save/restore of its own, so this compiler never assumes it survives arbitrary
/// code emitted between resolving an Indirect lvalue's address and using it -- every Indirect lvalue's
/// address is cached into its own dedicated local slot the moment it's computed, and <c>xt</c> is
/// re-issued from that cached slot immediately before every single load or store through it, however
/// many other things (including further "xt"s of their own) ran in between. See
/// <see cref="CLvalue"/>/<see cref="ResolveLvalue"/>/<see cref="CacheIndirectAddress"/>/
/// <see cref="EmitLoad"/>/<see cref="EmitStore"/>.</description></item>
/// <item><description><c>ldl</c>/<c>ldp</c> load a local's/parameter's value into register r WITHOUT
/// touching the stack; <c>stl</c>/<c>stp</c> store r INTO a local/parameter, also without touching the
/// stack -- CONFIRMED against real hardware 2026-09-09 from Stefan's own opcode bit patterns (<c>ldl</c>
/// <c>1001_111?_????_????</c>, <c>ldp</c> <c>1001_110?_????_????</c>, <c>stl</c>
/// <c>1001_101?_????_????</c>, <c>stp</c> <c>1001_100?_????_????</c>, each a 9-bit frame-relative
/// offset): "locals and parameters are stored on the stack relative to the frame pointer and are NOT
/// accessed with push and pop." This OVERTURNS this class's former, never-confirmed and now-known-wrong
/// assumption that these four worked directly against the data stack like <c>xt</c>/<c>ldt</c>/<c>stt</c>
/// do. They behave like the generic ALU/unary ops instead: every load site must follow with an explicit
/// <c>push</c> to move the loaded value from r onto the stack, and every store site must precede with an
/// explicit <c>pop</c> to move the value-to-store from the stack into r first -- see
/// <see cref="EmitLoad"/>/<see cref="EmitStore"/>/<see cref="EmitName"/>/
/// <see cref="CacheIndirectAddress"/>/<see cref="EmitSwitch"/> and the register-parameter spill in
/// <see cref="EmitFunction"/>, all fixed together for this. <c>lal</c>/<c>lap</c> (load ADDRESS of a
/// local/parameter, a different pair of mnemonics) are RETIRED as of 2026-09-09 ("'lal' &amp; 'lap' are
/// removed. use ''f' or 'fpush' from node 506 and add the offset to calculate the address of a local or
/// parameter.") -- this compiler no longer emits them at all. In their place, computing the address of a
/// local or parameter now emits <c>fpush</c> (push node 506's own frame pointer <c>f</c> directly onto
/// the data stack) followed by <c>pushlit &lt;offset&gt;; pop; sub</c> for a LOCAL (address = f - offset,
/// matching node 506's own <c>f/main</c> dispatch, which negates a local's offset with <c>inv</c> before
/// adding) or <c>pushlit &lt;offset&gt;; pop; add</c> for a PARAMETER (address = f + offset, no
/// negation) -- see <see cref="EmitAddressOf"/>'s <c>CNameExpr</c> case and <see cref="EmitName"/>'s
/// array-decay branches, both updated together for this. This has NOT been confirmed against real
/// hardware (no fresh self-check has been run since the change), so treat it with the same caution as
/// every other "not yet confirmed" item in this list.</description></item>
/// <item><description><c>cbr &lt;label&gt;</c> (formerly named <c>ifbr</c> -- Stefan, 2026-09-09: "ifbr"
/// no longer exist and should be replaced with "cbr". it is basically the same. opcode cbr
/// 1010_11??_????_???? conditional branch to offset if r == 0. the offset is signed 10-bit number.)
/// branches when the condition (loaded into r by an immediately preceding <c>pop</c>) is ZERO (false).
/// CONFIRMED against real hardware, and the OPPOSITE polarity from this class's own former, never-
/// confirmed assumption (the old "ifbr" placeholder was assumed to branch on NONZERO/true) -- every
/// control-flow codegen method below was originally built around that wrong polarity and has been
/// re-derived for this one instead. The net effect actually simplifies most call sites: "branch away
/// from a block when its condition is false" (if/while/for/ternary's else-branch, &amp;&amp;'s short
/// circuit) now needs no extra op at all, since <c>cbr</c> already tests exactly that; "branch when the
/// condition is true" (do-while's loop-back, a switch case match, ||'s short circuit) now needs an
/// explicit <c>eq0</c> first to invert the sense before branching. See <see cref="EmitStatement"/>'s
/// <c>CIfStmt</c>/<c>CWhileStmt</c>/<c>CDoWhileStmt</c>/<c>CForStmt</c> cases, <see cref="EmitLogical"/>,
/// <see cref="EmitConditional"/>, and <see cref="EmitSwitch"/>, all re-derived together for
/// this.</description></item>
/// <item><description><b>Uniform codegen invariant:</b> every non-void expression's codegen leaves
/// EXACTLY one word on the data stack representing its value; a void-typed expression (a call to a void
/// function) leaves nothing. An expression used as a statement emits the expression, then a bare
/// <c>pop</c> to discard the leftover value when its type isn't void. This single invariant is what lets
/// every expression case in <see cref="EmitExpr"/> compose with every other one with no "is this value
/// needed" threading anywhere in the compiler, at the modest cost of an occasional extra
/// <c>pop</c>/dup.</description></item>
/// <item><description><b>Function calling convention:</b> the caller evaluates and pushes each argument
/// left-to-right, then <c>call</c>s; the callee's <c>enter &lt;n&gt;</c> reserves n local slots and is
/// assumed to make the caller's pushed arguments addressable via <c>ldp</c>/<c>stp</c> (or, for a
/// parameter's ADDRESS rather than its value, the <c>fpush</c>-based sequence described above, now that
/// <c>lap</c> is retired) (parameter offset 0 = the first declared parameter); <c>return expr;</c> evaluates expr (leaving its
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
/// <item><description><b>Optimizer addition (2026-09-10): constant-indexed local array elements skip
/// address computation entirely.</b> <c>arr[K]</c> for a compile-time-constant <c>K</c> on a genuinely
/// LOCAL array is just the local at slot <c>arr's own base slot + K</c> -- loadable/storable with a plain
/// <c>ldl</c>/<c>stl</c>, no runtime address, no node-306 indirection, no <see
/// cref="CvmPeepholeOptimizer"/> toggle needed (it relies on nothing but this compiler's own local-slot
/// layout and the already-confirmed <c>ldl</c>/<c>stl</c> addressing every other local already uses, so it
/// is unconditional). See <see cref="TryGetConstantLocalArrayElementSlot"/>'s own remarks for the full
/// reasoning and scope.</description></item>
/// <item><description><b>Optimizer addition (2026-09-10): global-scalar access uses <c>gld</c>/<c>gst</c>
/// directly, no address-register dance.</b> A plain module-scope or <c>static</c> global used to be
/// treated exactly like a runtime pointer -- push its label, cache the "address" into a spilled local
/// slot, then indirect through node 306's <c>arst</c>/<c>lda</c>/<c>sta</c> on every access -- even
/// though the label is already a fixed, compile-time-constant reference the assembler/linker resolve via
/// a relocation. It now goes straight through <c>gld</c> (fetch: r := global) / <c>gst</c> (assign:
/// global := r), named and behaving exactly like an ordinary "load"/"store" -- see <see
/// cref="EmitGlobalFetch"/>/<see cref="EmitGlobalAssign"/>'s own remarks for the real hardware bug this
/// direction was hiding (node 508's own <c>g/@</c>/<c>g/!</c> primitives really were swapped) and
/// Stefan's 2026-09-10 hardware trace that caught and corrected it. Unconditional, like the array
/// optimization above: no case this recognizes (a compile-time-known global label) can ever need the
/// general address-computation
/// path.</description></item>
/// <item><description><c>switch</c>/<c>case</c> does NOT use a real <c>tjmp</c> (table jump) instruction:
/// its LOCATION is now confirmed (node 507, "'tjmp is in node 507", 2026-09-09 -- the same
/// "1000_1???" local-execute tag family as <c>nop</c>/<c>push</c>/<c>pop</c>/<c>ret</c>/<c>halt</c>, per
/// <see cref="CvmInstructionSet.TableJumpMnemonic"/>'s own remarks), but its calling convention -- how a
/// case table is laid out in memory, how the selector is bounds-checked and turned into an index, and
/// whether/how out-of-range selectors fall through to a default -- is still not confirmed anywhere in the
/// project's documentation, and this compiler follows the project's own established practice of never
/// guessing an unconfirmed hardware/table convention. Instead, a switch still compiles to a
/// straightforward, always-correct chain of equality comparisons against the selector (evaluated once
/// into a compiler-allocated temporary local). This is flagged here as a future optimization pending
/// Stefan supplying <c>tjmp</c>'s real calling convention -- see <see cref="EmitStatement"/>'s
/// <see cref="CSwitchStmt"/> case.</description></item>
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
    Global,
    Indirect,
  }

  /// <summary>
  /// The resolved location of an assignable expression. Local/Parameter carry no stack side-effect --
  /// <see cref="Index"/> is just the frame offset for <see cref="EmitLoad"/>/<see cref="EmitStore"/> to
  /// use directly (<c>ldl</c>/<c>stl</c>/<c>ldp</c>/<c>stp</c> take the offset right in the opcode
  /// word). Indirect (<c>*ptr</c> or a non-constant-indexed array element) means <see
  /// cref="ResolveLvalue"/> has already computed the address ONCE and cached it into the local slot
  /// number <see cref="Index"/> refers to (via <see cref="CacheIndirectAddress"/>) -- NOT left sitting
  /// only in the CVM's "r" register, which has no save/restore and does not survive arbitrary code
  /// running in between (see this class's own doc comment). <see cref="EmitLoad"/>/<see
  /// cref="EmitStore"/> each reload that cached address and re-run the full node-306 dereference
  /// sequence (<see cref="EmitDereferenceLoad"/>/<see cref="EmitDereferenceStore"/>) immediately before
  /// using it, so they stay correct no matter what runs between resolving the lvalue and using it.
  ///
  /// Global (added 2026-09-10) is a plain module-scope or <c>static</c> global's own scalar/pointer
  /// storage, addressed directly by its own compile-time-constant assembly <see cref="Label"/> via
  /// <c>gld</c>/<c>gst</c> (see <see cref="EmitGlobalFetch"/>/<see cref="EmitGlobalAssign"/>'s own
  /// remarks) -- unlike Indirect, there is nothing to cache into a local slot at all, because the
  /// label itself (not a runtime-computed address) is already a fixed, permanently-valid reference the
  /// assembler/linker resolve once at link time; <see cref="EmitLoad"/>/<see cref="EmitStore"/> just
  /// re-emit <c>gld</c>/<c>gst</c> against that same label text every time.
  /// </summary>
  private sealed record CLvalue(CLvalueKind Kind, int Index, CType Type, string? Label = null);

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

  /// <summary>Optimizer step 2 of 2, added 2026-09-10 -- see <see cref="CvmPeepholeOptimizer"/>'s own
  /// remarks. Off by default: unlike <see cref="CConstantFolder"/> (pure AST arithmetic, the same
  /// already-used evaluator as this class's own <see cref="TryEvaluateConstantLong"/>), two of this
  /// pass's rules lean on an assumption this codebase has flagged but not yet confirmed against real
  /// hardware (see <see cref="EmitFunction"/>'s own ABI v2 prologue remarks on "stl"/"sta" and register
  /// r) -- so it stays opt-in per project (<c>Ga144.Evb.Ide.Models.CProjectMetadata.EnablePeepholeOptimization</c>)
  /// until Stefan has had a chance to verify it.</summary>
  private readonly bool _enablePeepholeOptimization;

  public CCodeGenerator(bool enablePeepholeOptimization = false)
  {
    _enablePeepholeOptimization = enablePeepholeOptimization;
  }

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

    // Optimizer step 2 of 2 (see CvmPeepholeOptimizer's own remarks) -- runs over the already-fully-
    // emitted CODE lines, after every "enter N" placeholder above has already been patched to its real
    // value (EmitFunction patches _codeLines by INDEX while a function's body is still being emitted;
    // by the time Generate reaches this point every function is done, so there is no patch left for this
    // pass to disturb). DATA-section lines are never touched -- there is nothing peephole-optimizable in
    // a plain ".word" declaration.
    IReadOnlyList<string> codeLines = _enablePeepholeOptimization ? CvmPeepholeOptimizer.Optimize(_codeLines) : _codeLines;

    text.AppendLine(".section CODE");
    foreach (string line in codeLines)
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

  /// <summary>
  /// The CVM C ABI's external-symbol naming convention (per Stefan, 2026-09-07: "an external symbol
  /// should prepend an addition '_' character to the label"): every assembly-level symbol that IS
  /// directly a user-written C identifier -- a function's own name, or a global variable's own name --
  /// gets one additional leading underscore prepended on top of whatever the person wrote. A C
  /// function named <c>start</c> becomes the CVM label/export <c>_start</c>; a C function literally
  /// named <c>_start</c> becomes <c>__start</c>. This mirrors the classic C toolchain convention of
  /// prefixing every C-linkage symbol, so a compiled C name can never collide with a hand-written CVM
  /// assembly label that doesn't carry the prefix.
  ///
  /// Applied at every point a function's or global's OWN name becomes literal assembly text: its own
  /// label/".export" line, a ".import" for one only declared (not defined) in this file, a "call"
  /// target, and a "pushlit" reference to a global's address.
  ///
  /// <b>Deliberately NOT applied to:</b>
  /// <list type="bullet">
  /// <item><description>this compiler's own internal, already-mangled labels -- loop/branch labels
  /// (<see cref="NewLabel"/>), the string-literal pool's "__strN" (<see cref="InternStringLiteral"/>),
  /// a "static" local's "__static_&lt;function&gt;_&lt;name&gt;_&lt;n&gt;" (see
  /// <see cref="DeclareLocal"/>) -- none of which is a direct re-emission of a user identifier, so none
  /// needs (or gets) this prefix;</description></item>
  /// <item><description>the hand-written CVM runtime library routine names ("__mul"/"__divs"/
  /// "__divu"/"__mods"/"__modu", see <see cref="EmitLibraryBinary"/>) -- these are this compiler's own
  /// fixed, reserved call targets, not something the person wrote in their C source, so they are
  /// emitted exactly as chosen. <b>Flagged:</b> Stefan has not said whether these should ALSO gain an
  /// extra leading underscore (i.e. call sites emitting "___mul" to match a "cvm" library that defines
  /// it under that name) -- left unprefixed until confirmed, since changing this later is a one-line
  /// fix localized to <see cref="EmitLibraryBinary"/>'s three call sites.</description></item>
  /// </list>
  /// </summary>
  private static string MangleExternalSymbol(string name) => "_" + name;

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
      _imports.Add(MangleExternalSymbol(global.Name));
      return;
    }

    if (!global.IsStatic)
    {
      _dataLines.Add($".export {MangleExternalSymbol(global.Name)}");
    }

    List<string> words = ComputeGlobalInitialWords(global.Type, global.Initializer, global.Location);
    _dataLines.Add($"{MangleExternalSymbol(global.Name)}: .word {string.Join(", ", words)}");
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
          word = MangleExternalSymbol(name.Name);
          return true;
        }

        word = null;
        return false;

      case CNameExpr arrayName when _globals.TryGetValue(arrayName.Name, out CGlobalSymbol? symbol) && symbol.Type.IsArray:
        // A bare array name decays to its own address, same as everywhere else in C.
        word = MangleExternalSymbol(arrayName.Name);
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
  /// callers can report their own, more specific diagnostic.
  ///
  /// The binary-operator arithmetic itself is factored out into <see
  /// cref="CConstantFolder.TryEvaluateBinaryOp"/> (added 2026-09-10 alongside that class), shared with
  /// the AST-level constant-folding optimizer pass so the two can never quietly disagree about what a
  /// compile-time integer operation computes -- this method's own job is purely walking the handful of
  /// node shapes ITS OWN narrower callers need (a literal, a unary +/-/~ on one, sizeof, or a binary op
  /// whose own operands are themselves already constant), not the arithmetic underneath a binary
  /// op.</summary>
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
      _codeLines.Add($".export {MangleExternalSymbol(function.Name)}");
    }

    _codeLines.Add($"{MangleExternalSymbol(function.Name)}:");

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
      // 2026-09-09: "stl" stores r directly (see this class's own ABI doc comment) -- no "push" needed
      // to shuttle the address through the stack first. "sta" leaves the address in r AND pushes a page
      // word onto the stack as a side effect; "stl" reads r (assumed to leave r unchanged, like "push"
      // does) so the trailing "pop" only needs to clean up that leftover page word (always 0 -- far
      // pointers are not supported yet, see this class's own ABI doc comment).
      EmitCode($"sta {registerIndex}"); // r := this register's address word; stack: [..., page]
      EmitCode($"stl {localSlot}");     // localSlot := r (the address); stack unchanged
      EmitCode("pop");                  // r := the leftover page word, discarded; stack: [...]
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
  // local slot, not left sitting only in t. EmitLoad/EmitStore then each independently reload that
  // cached address and re-issue "xt" right before their own ldt/stt -- "ldl addressSlot; push; xt" is a
  // pure, side-effect-free re-derivation of t from the cache (2026-09-09: "ldl" itself only sets r, per
  // this class's own ABI doc comment -- the "push" is what actually puts the address back on the stack
  // for "xt" to consume), not a re-evaluation of the original target expression, so this is safe to
  // repeat as many times as needed (compound assignment loads once and stores once; increment/decrement
  // do the same) no matter what runs in between, or what "r" holds by the time it runs.
  // ---------------------------------------------------------------------------------------------

  /// <summary>Optimizer addition, 2026-09-10 -- Stefan's own observation on <c>d[0] = a;</c> (<c>d</c> a
  /// local array, <c>a</c> a local scalar): "d[0] is a local variable, a is a local variable. so a simple
  /// 'ldl d[0]; stl a' with the offset calculated for d[0] and a would have done the job." He's right, and
  /// unlike <see cref="CvmPeepholeOptimizer"/>'s rules this needs no unconfirmed hardware assumption to
  /// exploit: local-array element storage is entirely this compiler's OWN decision, not a hardware fact
  /// still awaiting confirmation. Local-slot allocation (<see cref="DeclareLocal"/>) already reserves
  /// <c>Type.SizeInWords</c> CONSECUTIVE local slots per declaration, so an array's <c>K</c>-th element
  /// (every element type here is exactly one CVM word -- see <see cref="CType"/>'s own remarks) always
  /// lives at local slot <c>arrayBaseSlot + K</c>. When <c>K</c> is itself known at compile time, that slot
  /// number is just as loadable/storable with a single <c>ldl</c>/<c>stl</c> as any plain scalar local --
  /// the exact same, already-confirmed addressing (see "ldl/ldp/stl/stp go through register r, confirmed
  /// (2026-09-09)" in the C-compiler design doc) every OTHER local already uses. No need to ever compute a
  /// runtime address, cache it, or indirect through it via node 306's address-register mechanism (<see
  /// cref="EmitDereferenceLoad"/>/<see cref="EmitDereferenceStore"/>) at all.
  ///
  /// This only ever fires for a genuinely LOCAL array (<see cref="CVarKind.Local"/>) referenced directly
  /// by its own bare name -- never a parameter (a parameter of array type has already decayed to a plain
  /// runtime pointer value by the time it reaches a <see cref="CVarSymbol"/>, so there is no fixed frame
  /// slot per element to fold into), never a global/static-local (those are addressed by a data-section
  /// label, not a frame offset -- <see cref="CVarKind.Global"/>), and never through a further expression
  /// (a dereferenced pointer, a function's return value, another index) -- anything this can't prove safe
  /// at compile time falls through unchanged to the general, always-correct address-computation path
  /// below. Because it can only ever produce EQUAL-OR-CHEAPER, equally-correct code compared to that
  /// general path for the one narrow case it recognizes, this fast path is unconditional -- unlike
  /// <see cref="CvmPeepholeOptimizer"/>, it is not gated behind a project optimizer toggle.</summary>
  private bool TryGetConstantLocalArrayElementSlot(CIndexExpr index, out int slot, out CType elementType)
  {
    slot = 0;
    elementType = CType.Int;

    if (index.Base is not CNameExpr name)
    {
      return false;
    }

    CVarSymbol? symbol = LookupVariable(name.Name);
    if (symbol is not { Kind: CVarKind.Local, Type.Kind: CTypeKind.Array })
    {
      return false;
    }

    if (symbol.Type.ArrayLength < 0)
    {
      return false;
    }

    if (!TryEvaluateConstantLong(index.Index, out long constIndex) || constIndex < 0 || constIndex >= symbol.Type.ArrayLength)
    {
      return false;
    }

    slot = symbol.Index + (int)constIndex;
    elementType = symbol.Type.ElementType!;
    return true;
  }

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

          string label = symbol?.GlobalLabel ?? MangleExternalSymbol(name.Name);
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

          // 2026-09-10: a global's own label is already a fixed, compile-time-constant reference (the
          // assembler/linker resolve it once, via a relocation exactly like pushlit's -- see
          // EmitGlobalFetch/EmitGlobalAssign's own remarks) -- there is nothing to compute or cache into
          // a local slot the way *ptr's or a non-constant array index's runtime address needs. This
          // used to unconditionally do "EmitCode($\"pushlit {label}\"); return CacheIndirectAddress(type);",
          // spending a whole address-computation-and-cache sequence (and burning a local slot) on
          // something already known at compile time; gld/gst make that unnecessary. See
          // "Global-scalar addressing: gld/gst replace the address-register dance (2026-09-10)" in
          // c-compiler-design.md for the full story, including the hardware trace that caught and fixed
          // a real bug in node 508's own g/@/g/! primitives underlying gld/gst.
          return new CLvalue(CLvalueKind.Global, 0, type, label);
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
          if (TryGetConstantLocalArrayElementSlot(index, out int directSlot, out CType directElementType))
          {
            return new CLvalue(CLvalueKind.Local, directSlot, directElementType);
          }

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
  /// section's own remarks), leaving the stack exactly as it was before the address was pushed.
  ///
  /// REWRITTEN 2026-09-09: this used to also "prime" node 508's pointer register "t" (via <c>xt</c>) for
  /// whatever load/store came next -- but <see cref="EmitLoad"/>/<see cref="EmitStore"/>'s own
  /// <c>default</c> case always re-derives the address from <paramref name="type"/>'s own cached slot
  /// and re-runs the full dereference sequence anyway, so that priming was already dead work even before
  /// node 508's <c>xt</c>/<c>ldt</c>/<c>stt</c> stopped resolving against any live node. Dropped outright
  /// rather than ported to node 306's replacement sequence (see <see cref="EmitDereferenceLoad"/>'s own
  /// remarks) -- there is nothing left here that needs it.
  /// </summary>
  private CLvalue CacheIndirectAddress(CType type)
  {
    int addressSlot = _currentFunction!.NextLocalSlot++;
    EmitCode("pop");                    // r := address; stack: [] (one word consumed off the incoming stack)
    EmitCode($"stl {addressSlot}");     // addressSlot := r (the address)
    return new CLvalue(CLvalueKind.Indirect, addressSlot, type);
  }

  /// <summary>Dereferences the address already sitting in register r, pushing the value read from
  /// memory at that address.
  ///
  /// ADDED 2026-09-09, replacing node 508's now-dead <c>xt</c>/<c>ldt</c> (deleted outright from
  /// <see cref="CvmInstructionSet"/> the same day, once this codegen stopped emitting them -- see
  /// that class's own remarks on the 2026-09-09 CVM1-opcode purge). The replacement uses node 306's address-register mechanism instead (see
  /// <see cref="Node306Program"/>'s own remarks for the F18-level design): <c>'arst</c> ("store address
  /// register") loads node 306's address register 0 from (address = r, page = the stack's own top word),
  /// and <c>'lda</c> ("load r from address in address register") then reads memory at that address back
  /// into r. Every C pointer in this compiler is a plain 16-bit CVM address (there is no notion of a
  /// non-zero page anywhere else in this ABI), so the page pushed here is always a literal 0.
  ///
  /// FLAGGED for Stefan: this always uses address register 0 -- node 306 actually has four (see
  /// Node306Program's own remarks) but wiring more than one would need a real assembler feature (an
  /// operand that selects which register to embed) that the command-line assembler/linker pipeline does
  /// not have today (see CvmAssembler's own remarks on why node 511's rld/rst/rpop/rpush were never
  /// assemblable there either). Also NOT YET CONFIRMED ON REAL HARDWARE -- this sequence has not been
  /// run against a physical GA144 board.
  /// </summary>
  private void EmitDereferenceLoad()
  {
    EmitCode("pushlit 0");   // stack: [..., 0] (page = 0)
    EmitCode("arst");        // node 306 address register 0 := (address = r, page = pop 0); stack: [...]
    EmitCode("lda");         // r := memory[address register 0]
    EmitCode("push");        // stack: [..., value]
  }

  /// <summary>Dereferences the address already sitting in register r, storing the value already on top
  /// of the stack to memory at that address. See <see cref="EmitDereferenceLoad"/>'s own remarks for the
  /// node-306 mechanism this uses and what's flagged about it -- <c>'sta</c> ("store r to address in
  /// address register") is <c>'lda</c>'s write-side counterpart.</summary>
  private void EmitDereferenceStore()
  {
    EmitCode("pushlit 0");   // stack: [..., value, 0] (page = 0)
    EmitCode("arst");        // node 306 address register 0 := (address = r, page = pop 0); stack: [..., value]
    EmitCode("pop");         // r := value; stack: [...]
    EmitCode("sta");         // memory[address register 0] := r
  }

  /// <summary>Fetches a global's value into r, via <c>gld</c> -- named and behaving exactly like an
  /// ordinary CPU "load": r := the global at <paramref name="label"/>.
  ///
  /// CORRECTED 2026-09-10 (same day as this whole feature, superseding an earlier, WRONG resolution of
  /// the same question). This class, <see cref="Ga144.Cvm.Toolchain.CvmInstructionSet"/>'s own
  /// <c>LoadGlobalMnemonic</c>/<c>StoreGlobalMnemonic</c> remarks, and <c>Node508Program</c>'s own
  /// remarks had all flagged an apparent naming/body cross-wire in node 508's F18 source (<c>'gld</c>
  /// calling a primitive whose body ends in a remote STORE; <c>'gst</c> calling one whose body ends in a
  /// remote FETCH). Asked directly, Stefan's FIRST answer ("gld loads a global from r" / "gst stores a
  /// global into r") was read as confirming that backwards direction was intentional -- <c>gld</c> :=
  /// store, <c>gst</c> := fetch -- and this method and <see cref="EmitGlobalAssign"/> were wired that
  /// way. That reading was WRONG: Stefan then ran an actual hardware/simulation test (<c>lit 1; gst 2;
  /// gld 2; gst 4; nop</c>) and the trace shows a WRITE to global address 2 on the <c>gst 2</c>
  /// instruction (storing r's value 1 there) and a READ of that same address back into r on the
  /// following <c>gld 2</c> -- i.e. <c>gst</c> genuinely STORES (global := r) and <c>gld</c> genuinely
  /// LOADS (r := global), exactly as their names ordinarily would suggest, with no reversal at all. The
  /// earlier "cross-wire" was a REAL bug in node 508's original F18 source (g/@'s and g/!'s own bodies
  /// really were swapped relative to their names), not a naming-convention quirk -- Stefan fixed it by
  /// swapping the two bodies and re-wiring <c>'gld</c>/<c>'gst</c> to alias <c>g/@</c>/<c>g/!</c>
  /// directly (see <c>Node508Program.Source</c>'s own updated text). Call sites should always go
  /// through this helper (and <see cref="EmitGlobalAssign"/>), never emit <c>gld</c>/<c>gst</c>
  /// directly, so a future correction (should one ever be needed again) has exactly one place to
  /// change.</summary>
  private void EmitGlobalFetch(string label) => EmitCode($"gld {label}");

  /// <summary>Stores r's value into a global, via <c>gst</c> -- named and behaving exactly like an
  /// ordinary CPU "store": the global at <paramref name="label"/> := r. See <see
  /// cref="EmitGlobalFetch"/>'s own remarks for the full history, including the earlier WRONG
  /// resolution this corrects and the hardware trace that corrected it.</summary>
  private void EmitGlobalAssign(string label) => EmitCode($"gst {label}");

  private void EmitLoad(CLvalue lvalue)
  {
    switch (lvalue.Kind)
    {
      case CLvalueKind.Local:
        EmitCode($"ldl {lvalue.Index}");
        EmitCode("push");
        break;
      case CLvalueKind.Parameter:
        EmitCode($"ldp {lvalue.Index}");
        EmitCode("push");
        break;
      case CLvalueKind.Global:
        EmitGlobalFetch(lvalue.Label!);   // r := the global's value -- no address computation at all
        EmitCode("push");
        break;
      default:
        EmitCode($"ldl {lvalue.Index}");   // r := the cached address
        EmitDereferenceLoad();
        break;
    }
  }

  private void EmitStore(CLvalue lvalue)
  {
    switch (lvalue.Kind)
    {
      case CLvalueKind.Local:
        EmitCode("pop");                   // r := value to store; stack: [...] (one word consumed)
        EmitCode($"stl {lvalue.Index}");   // the local := r
        break;
      case CLvalueKind.Parameter:
        EmitCode("pop");                   // r := value to store; stack: [...] (one word consumed)
        EmitCode($"stp {lvalue.Index}");   // the parameter := r
        break;
      case CLvalueKind.Global:
        EmitCode("pop");                   // r := value to store; stack: [...] (one word consumed)
        EmitGlobalAssign(lvalue.Label!);   // the global := r -- no address computation at all
        break;
      default:
        EmitCode($"ldl {lvalue.Index}");   // r := the cached address
        EmitDereferenceStore();
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

  /// <summary>Pushes the address of a local (or, since 2026-09-09, a parameter) at frame-relative
  /// <paramref name="offset"/> -- replaces the retired <c>lal</c>/<c>lap</c> mnemonics ("'lal' &amp;
  /// 'lap' are removed. use ''f' or 'fpush' from node 506 and add the offset to calculate the address of
  /// a local or parameter."). <c>fpush</c> pushes node 506's own frame pointer <c>f</c> onto the data
  /// stack; a LOCAL's address is <c>f - offset</c> (matching node 506's own <c>f/main</c> dispatch, which
  /// negates a local's offset with <c>inv</c> before adding it to <c>f</c> for <c>ldl</c>/<c>stl</c>),
  /// while a PARAMETER's address is <c>f + offset</c> (no negation, matching <c>ldp</c>/<c>stp</c>). Not
  /// yet confirmed against real hardware -- see this class's own remarks.</summary>
  private void EmitLocalOrParameterAddress(int offset, bool isParameter)
  {
    EmitCode("fpush");
    EmitCode($"pushlit {offset}");
    EmitCode("pop");
    EmitCode(isParameter ? "add" : "sub");
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
            EmitLocalOrParameterAddress(symbol.Index, isParameter: false);
            return CType.PointerTo(symbol.Type);
          }

          if (symbol is { Kind: CVarKind.Parameter })
          {
            EmitLocalOrParameterAddress(symbol.Index, isParameter: true);
            return CType.PointerTo(symbol.Type);
          }

          string label = symbol?.GlobalLabel ?? MangleExternalSymbol(name.Name);
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
  /// -- see this class's own doc comment) and returns the element's type. When <c>Base[Index]</c> is a
  /// compile-time-constant-indexed element of a local array, shares <see
  /// cref="TryGetConstantLocalArrayElementSlot"/> with <see cref="ResolveLvalue"/> to compute the address
  /// directly from the combined slot number in one <see cref="EmitLocalOrParameterAddress"/> call, rather
  /// than evaluating the array's own base address and the index separately and adding them at
  /// runtime.</summary>
  private CType EmitAddressOfIndex(CIndexExpr index)
  {
    if (TryGetConstantLocalArrayElementSlot(index, out int directSlot, out CType directElementType))
    {
      EmitLocalOrParameterAddress(directSlot, isParameter: false);
      return directElementType;
    }

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

  /// <summary>Assignment used purely for its side effect -- a bare <c>a = expr;</c> statement, or a
  /// plain declaration's initializer -- where nothing downstream needs the assignment's own result
  /// value (C's rule that "a = expr" is itself an expression only matters when something actually
  /// consumes that value, e.g. chained assignment or "if ((a = f()))"). Skips <see cref="EmitDup"/>
  /// entirely: <see cref="EmitStore"/> already nets to "-1 word" on its own (it pops the value it
  /// stores), which exactly cancels the "+1 word" <see cref="EmitExpr"/> just produced, so the net
  /// stack effect of the whole assignment is zero with no discard <c>pop</c> needed either. 2026-09-09:
  /// this is what makes <c>int a = 3;</c> compile to just <c>pushlit 3; pop; stl 0</c> instead of
  /// dup-ing the value and immediately throwing the extra copy away. See <see cref="EmitAssign"/> for
  /// the expression-context version that keeps the residual value.</summary>
  private void EmitAssignForEffect(CAssignExpr assign)
  {
    CLvalue target = ResolveLvalue(assign.Target);
    EmitExpr(assign.Value);
    EmitStore(target);
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

  /// <summary>The for-effect twin of <see cref="EmitCompoundAssign"/> -- see
  /// <see cref="EmitAssignForEffect"/>'s own remarks for why skipping the dup is safe whenever nothing
  /// needs the expression's own result (a bare <c>a += expr;</c> statement).</summary>
  private void EmitCompoundAssignForEffect(CCompoundAssignExpr expr)
  {
    CLvalue target = ResolveLvalue(expr.Target);
    EmitLoad(target);
    CType rhsType = EmitExpr(expr.Value);
    EmitBinaryOperation(expr.Op, target.Type, rhsType, expr.Location);
    EmitStore(target);
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

  /// <summary>The for-effect twin of <see cref="EmitIncrementOrDecrement"/> -- a bare <c>i++;</c> or
  /// <c>--i;</c> statement, where pre- vs. post- makes no observable difference (both just add or
  /// subtract one from memory) since nothing reads the expression's own result. See
  /// <see cref="EmitAssignForEffect"/>'s own remarks for why skipping the dup is safe here.</summary>
  private void EmitIncrementOrDecrementForEffect(CUnaryExpr expr)
  {
    bool isIncrement = expr.Op is CUnaryOp.PreIncrement or CUnaryOp.PostIncrement;
    CBinaryOp op = isIncrement ? CBinaryOp.Add : CBinaryOp.Subtract;

    CLvalue target = ResolveLvalue(expr.Operand);
    EmitLoad(target);          // stack: [old]
    EmitCode("pushlit 1");     // stack: [old, 1]
    EmitBinaryOperation(op, target.Type, CType.Int, expr.Location); // stack: [new]
    EmitStore(target);         // stack: []
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

  /// <summary>Short-circuit "&amp;&amp;"/"||", both branching on <c>cbr</c>'s CONFIRMED
  /// branches-if-r-equals-0 polarity documented on this class. "a &amp;&amp; b": evaluate a; if a is
  /// false, short-circuit straight to a result of 0 without ever evaluating b; otherwise evaluate b and
  /// coerce it to exactly 0 or 1 (matching C's own &amp;&amp;/|| result type). "a || b" mirrors this: if
  /// a is true, short-circuit straight to 1; otherwise evaluate and coerce b the same way. 2026-09-09:
  /// since <c>cbr</c> itself already tests "r == 0", "&amp;&amp;" (short-circuits exactly on that same
  /// condition, left == 0) needs no extra op before it; "||" (short-circuits on the OPPOSITE condition,
  /// left != 0) needs an explicit <c>eq0</c> first to invert the sense so <c>cbr</c> fires at the right
  /// moment -- see this class's own ABI doc comment.</summary>
  private CType EmitLogical(CBinaryExpr expr)
  {
    bool isAnd = expr.Op == CBinaryOp.LogicalAnd;
    string shortCircuitLabel = NewLabel(isAnd ? "and_false" : "or_true");
    string doneLabel = NewLabel(isAnd ? "and_done" : "or_done");

    EmitExpr(expr.Left);
    EmitCode("pop");
    if (!isAnd)
    {
      EmitCode("eq0"); // || short-circuits when left is true (!=0); invert first so cbr's own "r==0" test fires on that.
    }

    EmitCode($"cbr {shortCircuitLabel}"); // && needs no inversion: it short-circuits exactly when left == 0, which is what cbr already tests.

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
        // 2026-09-09: a bare assignment/compound-assignment/increment-decrement statement is the
        // single most common expression statement there is, and its own result value is NEVER read
        // (there is nowhere for a statement's "value" to go) -- so these three go through their
        // "ForEffect" twins, which skip the dup this class's own uniform stack invariant would
        // otherwise require, rather than the generic EmitExpr-then-pop path below. See
        // EmitAssignForEffect's own remarks.
        switch (exprStmt.Expression)
        {
          case CAssignExpr assign:
            EmitAssignForEffect(assign);
            break;

          case CCompoundAssignExpr compoundAssign:
            EmitCompoundAssignForEffect(compoundAssign);
            break;

          case CUnaryExpr { Op: CUnaryOp.PreIncrement or CUnaryOp.PostIncrement or CUnaryOp.PreDecrement or CUnaryOp.PostDecrement } incDec:
            EmitIncrementOrDecrementForEffect(incDec);
            break;

          default:
            {
              CType type = EmitExpr(exprStmt.Expression);
              if (!type.IsVoid)
              {
                EmitCode("pop");
              }

              break;
            }
        }

        break;

      case CLocalVarDecl localDecl:
        DeclareLocal(localDecl);
        if (localDecl.Initializer is not null && !localDecl.IsStatic)
        {
          // Same "ForEffect" reasoning as CExprStmt above: a declaration's initializer value is never
          // read either, so this goes straight to EmitAssignForEffect rather than round-tripping
          // through a dup it would just have to discard again.
          EmitAssignForEffect(new CAssignExpr(localDecl.Location, new CNameExpr(localDecl.Location, localDecl.Name), localDecl.Initializer));
        }

        break;

      case CIfStmt ifStmt:
        {
          string elseLabel = NewLabel("else");
          string endLabel = NewLabel("endif");
          EmitExpr(ifStmt.Condition);
          EmitCode("pop");
          // 2026-09-09: cbr branches when r == 0 (CONFIRMED, see this class's own ABI doc comment) --
          // exactly the "condition is false, skip to else" test we need, so no eq0 is needed first any
          // more (the old "ifbr" placeholder branched on nonzero, which is why this used to negate with
          // eq0 first).
          EmitCode($"cbr {elseLabel}");
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
          EmitCode($"cbr {endLabel}"); // 2026-09-09: cbr already branches on r == 0 -- see EmitStatement's CIfStmt case remarks.
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
          // 2026-09-09: do-while needs to loop back when the condition is TRUE, the opposite of what
          // cbr tests directly (r == 0) -- so, unlike if/while/for, this needs an explicit eq0 first to
          // invert the sense before branching (see this class's own ABI doc comment). The old "ifbr"
          // placeholder branched on nonzero, so it needed no such inversion here; cbr does.
          EmitCode("eq0");
          EmitCode($"cbr {startLabel}");
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
            EmitCode($"cbr {endLabel}"); // 2026-09-09: cbr already branches on r == 0 -- see EmitStatement's CIfStmt case remarks.
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

    // Evaluate the selector exactly once and store it. 2026-09-09: "stl" stores r, not the stack top
    // (see this class's own ABI doc comment), so the selector has to be popped into r before it can be
    // stashed away, and reloaded (ldl; push) at the top of every comparison since "eq"'s own pop
    // clobbers r on each iteration.
    EmitExpr(switchStmt.Selector);        // stack: [selector]
    EmitCode("pop");                       // r := selector; stack: []
    int selectorSlot = _currentFunction!.NextLocalSlot++;
    EmitCode($"stl {selectorSlot}");      // selectorSlot := r (the selector)

    foreach ((long value, string label) in cases)
    {
      EmitCode($"ldl {selectorSlot}");   // r := selector
      EmitCode("push");                   // stack: [selector]
      EmitCode($"pushlit {value}");      // stack: [selector, value]
      EmitCode("pop");                    // r := value; stack: [selector]
      EmitCode("eq");                     // stack: [selector == value]
      EmitCode("pop");                    // r := comparison result; stack: []
      // 2026-09-09: need to branch into this case when the comparison is TRUE, the opposite of what cbr
      // tests directly (r == 0) -- so, like do-while, this needs an explicit eq0 first to invert the
      // sense before branching (see this class's own ABI doc comment).
      EmitCode("eq0");
      EmitCode($"cbr {label}");
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
          // RENAMED 2026-09-09: node 508's own "negate" no longer resolved against any live CVM2 node,
          // and was deleted outright from CvmInstructionSet the same day once this codegen stopped
          // emitting it (see that class's own remarks on the 2026-09-09 CVM1-opcode purge) -- node 509's
          // "neg" (a genuinely different mnemonic string, live and node-resolved) computes the identical
          // two's-complement result ("inv" then "+1", see Node509Program's own remarks) and is used here
          // instead.
          CType type = EmitExpr(unary.Operand);
          EmitCode("pop");
          EmitCode("neg");
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
        EmitLocalOrParameterAddress(symbol.Index, isParameter: false);
        return symbol.Type.Decay();
      }

      EmitCode($"ldl {symbol.Index}");
      EmitCode("push");
      return symbol.Type;
    }

    if (symbol is { Kind: CVarKind.Parameter })
    {
      if (symbol.Type.IsArray)
      {
        EmitLocalOrParameterAddress(symbol.Index, isParameter: true);
        return symbol.Type.Decay();
      }

      EmitCode($"ldp {symbol.Index}");
      EmitCode("push");
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

      EmitGlobalFetch(staticLabel);   // r := the global's value -- no address computation at all
      EmitCode("push");
      return symbol.Type;
    }

    if (_globals.TryGetValue(name.Name, out CGlobalSymbol? global))
    {
      string globalLabel = MangleExternalSymbol(name.Name);
      if (global.Type.IsArray)
      {
        EmitCode($"pushlit {globalLabel}");
        return global.Type.Decay();
      }

      EmitGlobalFetch(globalLabel);   // r := the global's value -- no address computation at all
      EmitCode("push");
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
    string importedLabel = MangleExternalSymbol(name.Name);
    _imports.Add(importedLabel);
    EmitGlobalFetch(importedLabel);   // r := the global's value -- no address computation at all
    EmitCode("push");
    return CType.Int;
  }

  private CType EmitConditional(CConditionalExpr conditional)
  {
    string elseLabel = NewLabel("condfalse");
    string endLabel = NewLabel("condend");

    EmitExpr(conditional.Condition);
    EmitCode("pop");
    EmitCode($"cbr {elseLabel}"); // 2026-09-09: cbr already branches on r == 0 -- see EmitStatement's CIfStmt case remarks.
    CType trueType = EmitExpr(conditional.WhenTrue);
    EmitCode($"br {endLabel}");
    EmitLabel(elseLabel);
    CType falseType = EmitExpr(conditional.WhenFalse);
    EmitLabel(endLabel);

    return trueType.IsPointer ? trueType : falseType.IsPointer ? falseType : ComputeCommonType(trueType, falseType);
  }

  private CType EmitCall(CCallExpr call)
  {
    string mangledName = MangleExternalSymbol(call.FunctionName);

    if (!_functions.TryGetValue(call.FunctionName, out CFunctionSignature? signature))
    {
      Error(call.Location, $"\"{call.FunctionName}\" is not declared");
      _imports.Add(mangledName);
      foreach (CExpr argument in call.Arguments)
      {
        // Arguments stay on the stack for the callee, exactly like a normal call -- do not pop them.
        EmitExpr(argument);
      }

      EmitCode($"call {mangledName}");
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
      _imports.Add(mangledName);
    }

    EmitCode($"call {mangledName}");
    return signature.ReturnType;
  }
}