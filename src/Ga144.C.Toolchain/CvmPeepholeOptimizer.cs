namespace Ga144.C.Toolchain;

/// <summary>
/// Optimizer step 2 of 2 (see <see cref="CConstantFolder"/> for step 1 and the original division of
/// labor between the two). Where the constant folder cleans up arithmetic the PERSON wrote, in the C
/// source, before code generation even starts, this pass cleans up redundant instruction sequences the
/// CODE GENERATOR ITSELF introduces while lowering an ABI construct -- run AFTER <see
/// cref="CCodeGenerator"/> has already emitted every line, over its own CODE section (<c>_codeLines</c>,
/// exposed to this pass as a plain line list, never over the DATA section).
///
/// <b>REWRITTEN 2026-10-17 against the final, hardware-confirmed <c>CVM_pipeline</c> 79-mnemonic opcode
/// set</b> (see <see cref="CCodeGenerator"/>'s own top doc comment and claude/cvm-abi.md section 3) --
/// the previous version of this file matched <c>push</c>/<c>pop</c>/<c>pushlit</c>/<c>ldl</c>/<c>stl</c>/
/// <c>ldp</c>/<c>stp</c>, none of which the rewritten <see cref="CCodeGenerator"/> emits anywhere
/// anymore. The four rules below match the NEW code generator's own equally-redundant sequences instead:
///
/// <list type="bullet">
/// <item><description><c>rpush rN</c> immediately followed by <c>rpop rN</c> (same N) -- a full no-op:
/// <c>rpush</c> pushes <c>rN</c>'s CURRENT value onto the VM (page-1) stack without altering it, and the
/// very next <c>rpop</c> then pops that exact value straight back into the SAME register and off the
/// stack.</description></item>
/// <item><description><c>dpush dN</c> immediately followed by <c>dpop dN</c> (same N) -- the same idea,
/// for a whole double register.</description></item>
/// <item><description><c>ldf rX, O</c> immediately followed by <c>stf rX, O</c> (same X, same O) -- load
/// frame slot O into rX, then immediately store rX right back into the SAME slot: a no-op, since nothing
/// ran in between to change either the slot or rX.</description></item>
/// <item><description><c>stf rX, O</c> immediately followed by <c>ldf rX, O</c> (same X, same O) -- the
/// reload is redundant. <c>stf</c> stores rX's CURRENT value into frame slot O without altering rX (the
/// same "a store leaves its source register unchanged" assumption the OLD version of this file already
/// leaned on for <c>stl</c>/<c>stp</c>, carried over rather than re-derived -- NOT yet confirmed against
/// real hardware), so rX already holds exactly what the <c>ldf</c> would reload immediately
/// afterward.</description></item>
/// <item><description>(ADDED 2026-11-08) <c>inc rN</c>/<c>dec rN</c>, <c>neg rN</c>/<c>neg rN</c>,
/// <c>inv rN</c>/<c>inv rN</c>, <c>dinc dN</c>/<c>ddec dN</c> and <c>dinc2</c>/<c>ddec2</c> pairs on the same
/// register (either order) -- exact inverses, so the pair is deleted.</description></item>
/// <item><description>(ADDED 2026-11-08) <c>rpush rA</c> immediately followed by <c>rpop rB</c> with
/// A != B -- replaced by the single <c>mov rB, rA</c>.</description></item>
/// <item><description>(ADDED 2026-11-08) <c>mov rA, rB</c> immediately followed by <c>mov rB, rA</c> -- the
/// second move is a no-op and is deleted; a self-move <c>mov rA, rA</c> is deleted too.</description></item>
/// </list>
///
/// <b>Scope and safety</b> -- unchanged from the previous version. Every rule matches a small, fixed run
/// of ADJACENT lines and either deletes it outright or shortens it. A CVM branch/label target is always
/// its own separate entry in the line list (see <see cref="CCodeGenerator.EmitLabel"/>) -- a bare
/// instruction line never carries a label of its own -- so a label can never sit hidden INSIDE a matched
/// run; a rule can therefore never delete or skip past something another instruction branches into.
/// Rules are applied to a fixed point (repeated left-to-right passes until one full pass makes no
/// further change), since removing one matched run can bring two previously non-adjacent lines together
/// into a brand new match.
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
  /// <c>":"</c>, an instruction line is <c>"  " + mnemonic[ operand[, operand]]</c>) to a fixed point.
  /// Never mutates the input list; always returns a new one.</summary>
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

    // Rule: "rpush rN; rpop rN" (same N) -- a full no-op on both the VM stack and the register.
    if (remaining >= 2
        && TryParseRegisterInstruction(lines[i], "rpush", "r", out int pushedReg)
        && TryParseRegisterInstruction(lines[i + 1], "rpop", "r", out int poppedReg)
        && pushedReg == poppedReg)
    {
      return 2;
    }

    // Rule: "dpush dN; dpop dN" (same N) -- the same idea, for a whole double register.
    if (remaining >= 2
        && TryParseRegisterInstruction(lines[i], "dpush", "d", out int pushedDouble)
        && TryParseRegisterInstruction(lines[i + 1], "dpop", "d", out int poppedDouble)
        && pushedDouble == poppedDouble)
    {
      return 2;
    }

    // Rule: "ldf rX, O; stf rX, O" (same X, same O) -- load then immediately store back the exact same
    // value to the exact same frame slot: a no-op.
    if (remaining >= 2
        && TryParseFrameInstruction(lines[i], "ldf", out int loadedReg, out int loadedOffset)
        && TryParseFrameInstruction(lines[i + 1], "stf", out int storedReg, out int storedOffset)
        && loadedReg == storedReg && loadedOffset == storedOffset)
    {
      return 2;
    }

    // Rule: "stf rX, O; ldf rX, O" (same X, same O) -- the reload is redundant (see this class's own
    // doc comment for the "a store leaves its source register unchanged" assumption this relies on).
    if (remaining >= 2
        && TryParseFrameInstruction(lines[i], "stf", out int storedReg2, out int storedOffset2)
        && TryParseFrameInstruction(lines[i + 1], "ldf", out int reloadedReg, out int reloadedOffset)
        && storedReg2 == reloadedReg && storedOffset2 == reloadedOffset)
    {
      replacementLine = lines[i];
      return 2;
    }

    // ADDED 2026-11-08. Rule: two adjacent self-cancelling unary ops on the same register --
    // "inc rN; dec rN", "dec rN; inc rN", "neg rN; neg rN", "inv rN; inv rN" and the double-register
    // "dinc/ddec" and "dinc2/ddec2" pairs (either order). All of these are exact modular inverses, so the
    // pair is a full no-op. They arise when constant-offset pointer/index arithmetic is emitted next to its
    // own undo (for example the "inc rP; rload ...; dec rP" word-1 access sequence directly followed by
    // another access).
    if (remaining >= 2)
    {
      foreach ((string first, string second, string prefix) in CancellingPairs)
      {
        if (TryParseRegisterInstruction(lines[i], first, prefix, out int firstReg)
            && TryParseRegisterInstruction(lines[i + 1], second, prefix, out int secondReg)
            && firstReg == secondReg)
        {
          return 2;
        }
      }
    }

    // ADDED 2026-11-08. Rule: "rpush rA; rpop rB" (A != B, adjacent) -- the value is pushed and popped
    // straight back off, so the VM stack is unchanged and the net effect is "mov rB, rA" (one word and one
    // instruction instead of two). The A == B case is the full no-op rule above.
    if (remaining >= 2
        && TryParseRegisterInstruction(lines[i], "rpush", "r", out int movedFrom)
        && TryParseRegisterInstruction(lines[i + 1], "rpop", "r", out int movedTo)
        && movedFrom != movedTo)
    {
      replacementLine = $"  mov r{movedTo}, r{movedFrom}";
      return 2;
    }

    // ADDED 2026-11-08. Rule: "mov rA, rB; mov rB, rA" -- after the first move both registers hold the same
    // value, so the second is a no-op (this is what "copy the result of a library call to the scratch register,
    // then copy it on to r0" produces). A self-move "mov rA, rA" is deleted as well.
    if (TryParseMove(lines[i], out int moveDest, out int moveSource))
    {
      if (moveDest == moveSource)
      {
        return 1;
      }

      if (remaining >= 2
          && TryParseMove(lines[i + 1], out int backDest, out int backSource)
          && backDest == moveSource && backSource == moveDest)
      {
        replacementLine = lines[i];
        return 2;
      }
    }

    return 0;
  }

  /// <summary>Matches <c>"  mov rA, rB"</c> (two plain registers).</summary>
  private static bool TryParseMove(string line, out int destination, out int source)
  {
    destination = 0;
    source = 0;
    const string prefix = "  mov r";
    if (!line.StartsWith(prefix, StringComparison.Ordinal))
    {
      return false;
    }

    int commaIndex = line.IndexOf(',', prefix.Length);
    if (commaIndex < 0)
    {
      return false;
    }

    string rest = line[(commaIndex + 1)..].Trim();
    return rest.StartsWith('r')
        && int.TryParse(line.AsSpan(prefix.Length, commaIndex - prefix.Length), out destination)
        && int.TryParse(rest.AsSpan(1), out source);
  }

  /// <summary>(first mnemonic, second mnemonic, register prefix) triples that cancel exactly when applied
  /// back to back on the same register -- see the rule in <see cref="TryMatchAndRewrite"/>.</summary>
  private static readonly (string First, string Second, string Prefix)[] CancellingPairs =
  [
    ("inc", "dec", "r"),
    ("dec", "inc", "r"),
    ("neg", "neg", "r"),
    ("inv", "inv", "r"),
    ("dinc", "ddec", "d"),
    ("ddec", "dinc", "d"),
    ("dinc2", "ddec2", "d"),
    ("ddec2", "dinc2", "d"),
  ];

  /// <summary>Matches an emitted instruction line of the shape <c>"  {mnemonic} {registerPrefix}{N}"</c>
  /// (exactly what <see cref="CCodeGenerator.EmitCode"/> produces for <c>rpush</c>/<c>rpop</c>/
  /// <c>dpush</c>/<c>dpop</c>) and extracts its integer register number.</summary>
  private static bool TryParseRegisterInstruction(string line, string mnemonic, string registerPrefix, out int register)
  {
    string prefix = "  " + mnemonic + " " + registerPrefix;
    if (line.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(line.AsSpan(prefix.Length), out register))
    {
      return true;
    }

    register = 0;
    return false;
  }

  /// <summary>Matches an emitted instruction line of the shape <c>"  {mnemonic} r{X}, {offset}"</c>
  /// (exactly what <see cref="CCodeGenerator.EmitCode"/> produces for <c>ldf</c>/<c>stf</c>) and extracts
  /// its register number and signed frame offset.</summary>
  private static bool TryParseFrameInstruction(string line, string mnemonic, out int register, out int offset)
  {
    register = 0;
    offset = 0;

    string prefix = "  " + mnemonic + " r";
    if (!line.StartsWith(prefix, StringComparison.Ordinal))
    {
      return false;
    }

    int commaIndex = line.IndexOf(',', prefix.Length);
    if (commaIndex < 0)
    {
      return false;
    }

    if (!int.TryParse(line.AsSpan(prefix.Length, commaIndex - prefix.Length), out register))
    {
      return false;
    }

    return int.TryParse(line.AsSpan(commaIndex + 1).Trim(), out offset);
  }
}
