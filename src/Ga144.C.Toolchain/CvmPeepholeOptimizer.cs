namespace Ga144.C.Toolchain;

/// <summary>
/// Optimizer step 2 of 2, added 2026-09-10 alongside <see cref="CConstantFolder"/> (see that class's own
/// remarks for the full request and the division of labor between the two). Where the constant folder
/// cleans up arithmetic the PERSON wrote, in the C source, before code generation even starts, this pass
/// cleans up redundant instruction sequences the CODE GENERATOR ITSELF introduces while lowering an ABI
/// construct -- run AFTER <see cref="CCodeGenerator"/> has already emitted every line, over its own CODE
/// section (<c>_codeLines</c>, exposed to this pass as a plain line list, never over the DATA section).
///
/// The motivating example, straight from a hand-walked review of this compiler's own output for
/// <c>d[0] = a;</c> where <c>d</c> is a local array: computing <c>d</c>'s own address is <c>fpush;
/// pushlit &lt;offset&gt;; pop; sub</c> (see <see cref="CCodeGenerator.EmitLocalOrParameterAddress"/>),
/// then the array index contributes a SECOND, separately-emitted <c>pushlit &lt;index * elementSize&gt;;
/// pop; add</c> -- and for <c>d[0]</c> specifically, that second literal is always zero, so the whole
/// three-line group computes nothing at all. This sequence is never a <see cref="CBinaryExpr"/> node
/// anywhere in the parsed tree (it's synthesized directly as instruction text by codegen's own lowering
/// helpers) -- no amount of AST-level folding (<see cref="CConstantFolder"/>) can reach it, since there
/// is no AST node left to fold by the time it's emitted. Only a pass over the instructions THEMSELVES,
/// after they exist, can.
///
/// <b>Scope and safety.</b> Every rule below is justified purely from this ABI's own instruction
/// semantics as <see cref="CCodeGenerator"/>'s own class-level ABI doc comment already states them --
/// push/pop's stack effect, and add/sub's stack-top-vs-r operand order and commutativity for a zero
/// operand -- except for the two <c>stl</c>/<c>stp</c> forwarding rules, which rely on the SAME
/// "a store leaves r unchanged, like push does" assumption <see cref="CCodeGenerator.EmitFunction"/>'s
/// own ABI v2 prologue already leans on (see that method's own remarks) -- not a new, separately
/// unverified assumption introduced here, but flagged the same way that one is: NOT yet confirmed
/// against real hardware. Every rule matches a small, fixed run of ADJACENT lines and either deletes it
/// outright or shortens it. A CVM branch/label target is always its own separate entry in the line list
/// (see <see cref="CCodeGenerator.EmitLabel"/>) -- a bare instruction line never carries a label of its
/// own -- so a label can never sit hidden INSIDE a matched run; a rule can therefore never delete or
/// skip past something another instruction branches into. Rules are applied to a fixed point (repeated
/// left-to-right passes until one full pass makes no further change), since removing one matched run can
/// bring two previously non-adjacent lines together into a brand new match.
///
/// Deliberately NOT attempted here: anything that needs to reason about a value's flow across more than
/// this one small window -- general dead-store elimination, register/stack lifetime analysis across
/// branches, or dead-branch removal. Those need real data-flow analysis, not a fixed local pattern match,
/// and are left as a flagged future extension.
/// </summary>
public static class CvmPeepholeOptimizer
{
  /// <summary>Rewrites <paramref name="codeLines"/> (exactly the shape <see
  /// cref="CCodeGenerator"/>'s own <c>_codeLines</c> already has -- a label line is bare text ending in
  /// <c>":"</c>, an instruction line is <c>"  " + mnemonic[ operand]</c>) to a fixed point. Never mutates
  /// the input list; always returns a new one.</summary>
  public static List<string> Optimize(IReadOnlyList<string> codeLines)
  {
    var lines = new List<string>(codeLines);
    bool changed;
    do
    {
      changed = false;
      for (int i = 0; i < lines.Count; i++)
      {
        int consumed = TryMatchAndRewrite(lines, i, out string? replacementLine);
        if (consumed == 0)
        {
          continue;
        }

        lines.RemoveRange(i, consumed);
        if (replacementLine is not null)
        {
          lines.Insert(i, replacementLine);
        }

        changed = true;
        i--; // re-examine this same position -- a new match may now start exactly here
      }
    }
    while (changed);

    return lines;
  }

  /// <summary>Tries every rule at position <paramref name="i"/>. Returns how many lines starting at
  /// <paramref name="i"/> the matched rule consumes (0 if no rule matched), and, for a rule that
  /// SHORTENS a run rather than deleting it outright, the one line that should replace it.</summary>
  private static int TryMatchAndRewrite(List<string> lines, int i, out string? replacementLine)
  {
    replacementLine = null;
    int remaining = lines.Count - i;

    // Rule: "push" immediately followed by "pop" -- a full no-op. "push" pushes register r's CURRENT
    // value onto the stack without altering r; the very next "pop" then pops that exact value straight
    // back into r and off the stack -- net effect on both the stack and r: none.
    if (remaining >= 2 && lines[i] == "  push" && lines[i + 1] == "  pop")
    {
      return 2;
    }

    // Rule: "pushlit 0; pop; add" or "pushlit 0; pop; sub" -- adding or subtracting a literal zero is a
    // full no-op on the stack top. "pop" sets r := 0 (the literal just pushed) and removes it from the
    // stack; "add"/"sub" then combine r with the (new) stack top and replace it with the result -- since
    // ordinary integer addition/subtraction of zero is an identity regardless of operand order, the
    // stack top comes out exactly as it already was either way.
    if (remaining >= 3 && lines[i] == "  pushlit 0" && lines[i + 1] == "  pop" && (lines[i + 2] == "  add" || lines[i + 2] == "  sub"))
    {
      return 3;
    }

    // Rule: "ldl N; stl N" (same N) -- load local N into r, then immediately store r right back into
    // local N: a no-op, since nothing else ran in between to change either the local or r.
    if (remaining >= 2 && TryParseSlotInstruction(lines[i], "ldl", out int loadedLocal) && TryParseSlotInstruction(lines[i + 1], "stl", out int storedLocal) && loadedLocal == storedLocal)
    {
      return 2;
    }

    // Rule: "ldp N; stp N" (same N) -- the same idea, for a parameter slot.
    if (remaining >= 2 && TryParseSlotInstruction(lines[i], "ldp", out int loadedParam) && TryParseSlotInstruction(lines[i + 1], "stp", out int storedParam) && loadedParam == storedParam)
    {
      return 2;
    }

    // Rule: "stl N; ldl N" (same N) -- the reload is redundant. "stl N" stores r's CURRENT value into
    // local N; per the SAME assumption CCodeGenerator.EmitFunction's own ABI v2 prologue already relies
    // on ("stl" leaves r unchanged, like "push" does -- see that method's own remarks), r already holds
    // exactly what "ldl N" would reload immediately afterward, making the reload redundant. NOT yet
    // confirmed against real hardware, exactly like that existing assumption.
    if (remaining >= 2 && TryParseSlotInstruction(lines[i], "stl", out int storedLocal2) && TryParseSlotInstruction(lines[i + 1], "ldl", out int reloadedLocal) && storedLocal2 == reloadedLocal)
    {
      replacementLine = lines[i];
      return 2;
    }

    // Rule: "stp N; ldp N" (same N) -- the same idea, for a parameter slot.
    if (remaining >= 2 && TryParseSlotInstruction(lines[i], "stp", out int storedParam2) && TryParseSlotInstruction(lines[i + 1], "ldp", out int reloadedParam) && storedParam2 == reloadedParam)
    {
      replacementLine = lines[i];
      return 2;
    }

    return 0;
  }

  /// <summary>Matches an emitted instruction line of the shape <c>"  {mnemonic} {slot}"</c> (exactly
  /// what <see cref="CCodeGenerator.EmitCode"/> produces for <c>ldl</c>/<c>stl</c>/<c>ldp</c>/<c>stp</c>)
  /// and extracts its integer slot operand.</summary>
  private static bool TryParseSlotInstruction(string line, string mnemonic, out int slot)
  {
    string prefix = "  " + mnemonic + " ";
    if (line.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(line.AsSpan(prefix.Length), out slot))
    {
      return true;
    }

    slot = 0;
    return false;
  }
}
