namespace Ga144.C.Toolchain;

/// <summary>Base of every expression AST node <see cref="CParser"/> builds. Purely syntactic --
/// nothing here carries a resolved type; <see cref="CCodeGenerator"/> computes (and checks) each
/// expression's type as it walks the tree, in the same single pass that emits code for it, rather than
/// annotating the tree in a separate pass.</summary>
public abstract record CExpr(CSourceLocation Location);

public sealed record CIntLiteralExpr(CSourceLocation Location, long Value, bool IsUnsigned) : CExpr(Location);

/// <summary>A string literal. Codegen allocates it as an anonymous global <c>char[]</c> (NUL-terminated)
/// the first time it's seen and reuses the same label for an identical literal seen again.</summary>
public sealed record CStringLiteralExpr(CSourceLocation Location, string Value) : CExpr(Location);

/// <summary>A bare identifier -- a variable, a parameter, or (only as the callee of a <see
/// cref="CCallExpr"/>) a function name. Never resolved to a symbol here; <see cref="CCodeGenerator"/>
/// looks it up in whichever scope is active at the point it's compiled.</summary>
public sealed record CNameExpr(CSourceLocation Location, string Name) : CExpr(Location);

public enum CUnaryOp
{
  Plus,
  Minus,
  LogicalNot,
  BitwiseNot,
  AddressOf,
  Dereference,
  PreIncrement,
  PreDecrement,
  PostIncrement,
  PostDecrement,
}

public sealed record CUnaryExpr(CSourceLocation Location, CUnaryOp Op, CExpr Operand) : CExpr(Location);

public enum CBinaryOp
{
  Add,
  Subtract,
  Multiply,
  Divide,
  Modulo,
  BitwiseAnd,
  BitwiseOr,
  BitwiseXor,
  ShiftLeft,
  ShiftRight,
  Equal,
  NotEqual,
  Less,
  Greater,
  LessOrEqual,
  GreaterOrEqual,
  LogicalAnd,
  LogicalOr,
}

public sealed record CBinaryExpr(CSourceLocation Location, CBinaryOp Op, CExpr Left, CExpr Right) : CExpr(Location);

/// <summary>Plain "target = value". A compound form ("+=" and friends) is <see
/// cref="CCompoundAssignExpr"/> instead, kept separate because it needs its target's address computed
/// only once (see the design doc's codegen notes) rather than desugared into a second read of an
/// arbitrary target expression, which could re-run side effects.</summary>
public sealed record CAssignExpr(CSourceLocation Location, CExpr Target, CExpr Value) : CExpr(Location);

public sealed record CCompoundAssignExpr(CSourceLocation Location, CBinaryOp Op, CExpr Target, CExpr Value) : CExpr(Location);

public sealed record CConditionalExpr(CSourceLocation Location, CExpr Condition, CExpr WhenTrue, CExpr WhenFalse) : CExpr(Location);

public sealed record CCallExpr(CSourceLocation Location, string FunctionName, IReadOnlyList<CExpr> Arguments) : CExpr(Location);

/// <summary>"Base[Index]" -- Base may be an array or a pointer (after decay); see the design doc for
/// why no scaling multiplication is needed for any type this compiler supports.</summary>
public sealed record CIndexExpr(CSourceLocation Location, CExpr Base, CExpr Index) : CExpr(Location);

public sealed record CCastExpr(CSourceLocation Location, CType TargetType, CExpr Operand) : CExpr(Location);

public sealed record CSizeOfTypeExpr(CSourceLocation Location, CType Type) : CExpr(Location);

public sealed record CSizeOfExprExpr(CSourceLocation Location, CExpr Operand) : CExpr(Location);

/// <summary>The comma operator: evaluates Left and discards it, then evaluates and yields Right.
/// </summary>
public sealed record CCommaExpr(CSourceLocation Location, CExpr Left, CExpr Right) : CExpr(Location);
