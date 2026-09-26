namespace Ga144.C.Toolchain;

/// <summary>Base of every expression AST node <see cref="CParser"/> builds. Purely syntactic --
/// nothing here carries a resolved type; <see cref="CCodeGenerator"/> computes (and checks) each
/// expression's type as it walks the tree, in the same single pass that emits code for it, rather than
/// annotating the tree in a separate pass.</summary>
public abstract record CExpr(CSourceLocation Location);

public sealed record CIntLiteralExpr(CSourceLocation Location, long Value, bool IsUnsigned) : CExpr(Location);

/// <summary>A <c>float</c> literal ("3.14", "1.5f", "1e-3") -- added 2026-09-26 alongside basic
/// <c>float</c> support (see <see cref="CType.Float"/>'s own remarks). <see cref="Value"/> is stored as a
/// C# <c>float</c> (32-bit IEEE-754), matching the CVM's own <c>float</c> exactly, so
/// <see cref="CCodeGenerator"/> can take its bit pattern directly via
/// <c>System.BitConverter.SingleToInt32Bits</c> with no further rounding.</summary>
public sealed record CFloatLiteralExpr(CSourceLocation Location, float Value) : CExpr(Location);

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

/// <summary>"Base.Member" (<paramref name="IsArrow"/> false) or "Base->Member" (<paramref
/// name="IsArrow"/> true) -- struct member access, added 2026-09-26 alongside basic <c>struct</c>
/// support (see <see cref="CType.StructOf"/>'s own remarks). Purely syntactic, like every other node
/// here: whether <see cref="Base"/> actually has a struct type, which member offset "Member" resolves
/// to, and whether <paramref name="IsArrow"/> is even the right operator for <see cref="Base"/>'s type
/// are all resolved by <see cref="CCodeGenerator"/> during codegen, not here.</summary>
public sealed record CMemberAccessExpr(CSourceLocation Location, CExpr Base, string Member, bool IsArrow) : CExpr(Location);