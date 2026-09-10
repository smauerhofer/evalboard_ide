namespace Ga144.C.Toolchain;

/// <summary>
/// Optimizer step 1 of 2, added 2026-09-10 per Stefan's own request ("can you add an optimizer step
/// before generating the assembler code?" / "let's build an optimizer for the C compiler"): AST-level
/// constant folding. Rewrites the parsed <see cref="CTranslationUnit"/> BEFORE <see
/// cref="CCodeGenerator"/> ever sees it, replacing every compile-time-constant arithmetic/bitwise/shift
/// sub-expression it finds with a single <see cref="CIntLiteralExpr"/>. See <see
/// cref="CvmPeepholeOptimizer"/> (optimizer step 2, run AFTER code generation instead) for the other
/// half of this feature and for why both are needed -- in short, a lot of the redundant arithmetic this
/// compiler emits (e.g. adding a zero index offset while computing an array element's address) is
/// synthesized directly by <see cref="CCodeGenerator"/>'s own lowering helpers as raw instruction text,
/// never as a <see cref="CExpr"/> node at all, so this pass alone cannot reach it -- only genuine
/// C-source arithmetic (`2 + 3`, `a[1+1]`, `case 1+1:`, ...) is in scope here.
///
/// <b>Scope, deliberately narrow.</b> This folds exactly the same operator set <see
/// cref="CCodeGenerator.TryEvaluateConstantLong"/> already recognizes as a compile-time integer constant
/// for ITS OWN narrower callers (array bounds, <c>case</c> labels) -- plain arithmetic, bitwise, and
/// shift operations on two already-constant operands, plus unary <c>+</c>/<c>-</c>/<c>~</c> on one --
/// via the exact same evaluator (<see cref="TryEvaluateBinaryOp"/>), so the two can never quietly
/// disagree about what "constant" means or how it's computed. Deliberately NOT folded here:
/// <list type="bullet">
/// <item><description>Comparisons (<c>==</c>, <c>&lt;</c>, ...) and the logical operators (<c>&amp;&amp;</c>,
/// <c>||</c>) -- this compiler picks a signed or unsigned comparison/shift instruction based on each
/// operand's own resolved TYPE (see <see cref="CCodeGenerator"/>'s own ABI doc comment), a decision this
/// pass has no way to reproduce correctly, since it runs on the bare, untyped syntax tree <see
/// cref="CParser"/> produces, before <see cref="CCodeGenerator"/> ever resolves a single type. Folding a
/// comparison here would risk silently picking the wrong (signed vs. unsigned) result for a
/// mismatched-signedness expression -- left as a flagged future extension for once this pass (or the
/// tree itself) carries resolved types.</description></item>
/// <item><description>A <c>?:</c> conditional's own condition, even when it folds to a known constant --
/// collapsing the ternary down to whichever branch is taken would be a valid, sound optimization (only
/// one branch of a ternary ever runs), but it starts to shade into control-flow/dead-code elimination
/// rather than plain expression folding, so it is left out of this first pass. Both branches, and the
/// condition itself, are still recursed into so a constant sub-expression INSIDE any of the three still
/// folds.</description></item>
/// <item><description>Anything with a side effect (a call, an assignment's own target, increment/
/// decrement, address-of, dereference) -- these are never folded away themselves, only recursed into so
/// a constant sub-expression inside one of their operands still folds.</description></item>
/// </list>
///
/// Purely a tree rewrite: no diagnostics, no side effects on <see cref="CCodeGenerator"/>'s own symbol
/// tables (this runs before code generation even starts), and safe to skip entirely -- see <see
/// cref="CCompiler"/>'s own <c>enableConstantFolding</c> option. Skipping it only means the generated
/// code contains extra, but still correct, runtime arithmetic for an expression this pass would
/// otherwise have evaluated ahead of time.
/// </summary>
public static class CConstantFolder
{
  public static CTranslationUnit Fold(CTranslationUnit unit) =>
      new(unit.Functions.Select(FoldFunction).ToList(), unit.Globals.Select(FoldGlobal).ToList());

  private static CFunctionDecl FoldFunction(CFunctionDecl function) =>
      function.Body is null ? function : function with { Body = (CCompoundStmt)FoldStmt(function.Body) };

  private static CGlobalVarDecl FoldGlobal(CGlobalVarDecl global) =>
      global.Initializer is null ? global : global with { Initializer = FoldExpr(global.Initializer) };

  private static CStmt FoldStmt(CStmt stmt) => stmt switch
  {
    CExprStmt s => s with { Expression = FoldExpr(s.Expression) },
    CCompoundStmt s => s with { Statements = s.Statements.Select(FoldStmt).ToList() },
    CLocalVarDecl s => s with { Initializer = s.Initializer is null ? null : FoldExpr(s.Initializer) },
    CIfStmt s => s with { Condition = FoldExpr(s.Condition), Then = FoldStmt(s.Then), Else = s.Else is null ? null : FoldStmt(s.Else) },
    CWhileStmt s => s with { Condition = FoldExpr(s.Condition), Body = FoldStmt(s.Body) },
    CDoWhileStmt s => s with { Body = FoldStmt(s.Body), Condition = FoldExpr(s.Condition) },
    CForStmt s => s with
    {
      Init = s.Init is null ? null : FoldStmt(s.Init),
      Condition = s.Condition is null ? null : FoldExpr(s.Condition),
      Update = s.Update is null ? null : FoldExpr(s.Update),
      Body = FoldStmt(s.Body),
    },
    CReturnStmt s => s with { Value = s.Value is null ? null : FoldExpr(s.Value) },
    CLabelStmt s => s with { Inner = FoldStmt(s.Inner) },
    CCaseLabelStmt s => s with { Value = FoldExpr(s.Value), Inner = FoldStmt(s.Inner) },
    CDefaultLabelStmt s => s with { Inner = FoldStmt(s.Inner) },
    CSwitchStmt s => s with { Selector = FoldExpr(s.Selector), Body = FoldStmt(s.Body) },
    // CEmptyStmt / CBreakStmt / CContinueStmt / CGotoStmt: no sub-expressions, nothing to fold.
    _ => stmt,
  };

  private static CExpr FoldExpr(CExpr expr)
  {
    switch (expr)
    {
      case CUnaryExpr { Op: CUnaryOp.Plus or CUnaryOp.Minus or CUnaryOp.BitwiseNot } unary:
        {
          CExpr operand = FoldExpr(unary.Operand);
          if (operand is CIntLiteralExpr literal)
          {
            long value = unary.Op switch
            {
              CUnaryOp.Plus => literal.Value,
              CUnaryOp.Minus => -literal.Value,
              CUnaryOp.BitwiseNot => ~literal.Value,
              _ => literal.Value, // unreachable given the case's own pattern guard above
            };
            return new CIntLiteralExpr(unary.Location, value, literal.IsUnsigned);
          }

          return unary with { Operand = operand };
        }

      case CUnaryExpr unary:
        // LogicalNot/AddressOf/Dereference/the four increment-decrement forms: never folded (address-of
        // and dereference are not arithmetic at all; LogicalNot's and the incr/decr forms' results
        // depend on a signedness/side-effect this pass doesn't track) -- still recurse into Operand so a
        // constant sub-expression inside one of these still folds.
        return unary with { Operand = FoldExpr(unary.Operand) };

      case CBinaryExpr binary:
        {
          CExpr left = FoldExpr(binary.Left);
          CExpr right = FoldExpr(binary.Right);
          if (left is CIntLiteralExpr l && right is CIntLiteralExpr r && TryEvaluateBinaryOp(binary.Op, l.Value, r.Value, out long result))
          {
            return new CIntLiteralExpr(binary.Location, result, l.IsUnsigned || r.IsUnsigned);
          }

          return binary with { Left = left, Right = right };
        }

      case CAssignExpr assign:
        // Target is an lvalue -- never itself a candidate to fold, but still recursed into so e.g.
        // "arr[2+3] = ..." still folds its own index.
        return assign with { Target = FoldExpr(assign.Target), Value = FoldExpr(assign.Value) };

      case CCompoundAssignExpr compoundAssign:
        return compoundAssign with { Target = FoldExpr(compoundAssign.Target), Value = FoldExpr(compoundAssign.Value) };

      case CConditionalExpr conditional:
        // Deliberately does not collapse down to one branch even when Condition folds to a known
        // constant -- see this class's own remarks on why that's left for a later round.
        return conditional with
        {
          Condition = FoldExpr(conditional.Condition),
          WhenTrue = FoldExpr(conditional.WhenTrue),
          WhenFalse = FoldExpr(conditional.WhenFalse),
        };

      case CCallExpr call:
        return call with { Arguments = call.Arguments.Select(FoldExpr).ToList() };

      case CIndexExpr index:
        return index with { Base = FoldExpr(index.Base), Index = FoldExpr(index.Index) };

      case CCastExpr cast:
        return cast with { Operand = FoldExpr(cast.Operand) };

      case CSizeOfExprExpr sizeOfExpr:
        // sizeof's own operand is never evaluated at runtime, but folding a constant sub-expression
        // inside it is harmless and keeps the tree normalized for anything that inspects it later.
        return sizeOfExpr with { Operand = FoldExpr(sizeOfExpr.Operand) };

      case CCommaExpr comma:
        return comma with { Left = FoldExpr(comma.Left), Right = FoldExpr(comma.Right) };

      default:
        // CIntLiteralExpr / CStringLiteralExpr / CNameExpr / CSizeOfTypeExpr: no sub-expressions.
        return expr;
    }
  }

  /// <summary>The exact same operator set and evaluation <see
  /// cref="CCodeGenerator.TryEvaluateConstantLong"/> already implements for its own, narrower callers --
  /// factored out here so both stay in permanent agreement about what a compile-time integer operation
  /// computes. Comparisons and the logical operators are deliberately absent; see this class's own
  /// remarks.</summary>
  internal static bool TryEvaluateBinaryOp(CBinaryOp op, long left, long right, out long result)
  {
    switch (op)
    {
      case CBinaryOp.Add: result = left + right; return true;
      case CBinaryOp.Subtract: result = left - right; return true;
      case CBinaryOp.Multiply: result = left * right; return true;
      case CBinaryOp.Divide when right != 0: result = left / right; return true;
      case CBinaryOp.Modulo when right != 0: result = left % right; return true;
      case CBinaryOp.BitwiseAnd: result = left & right; return true;
      case CBinaryOp.BitwiseOr: result = left | right; return true;
      case CBinaryOp.BitwiseXor: result = left ^ right; return true;
      case CBinaryOp.ShiftLeft: result = left << (int)right; return true;
      case CBinaryOp.ShiftRight: result = left >> (int)right; return true;
      default: result = 0; return false; // Divide/Modulo by zero, or a comparison/logical op: not folded.
    }
  }
}
