namespace Ga144.C.Toolchain;

/// <summary>Base of every statement AST node <see cref="CParser"/> builds.</summary>
public abstract record CStmt(CSourceLocation Location);

public sealed record CExprStmt(CSourceLocation Location, CExpr Expression) : CStmt(Location);

public sealed record CEmptyStmt(CSourceLocation Location) : CStmt(Location);

public sealed record CCompoundStmt(CSourceLocation Location, IReadOnlyList<CStmt> Statements) : CStmt(Location);

/// <summary>A local variable declaration used as a statement (C99-style: declarations may appear
/// anywhere a statement can, not only at the top of a block). "static" gives it the same one-per-
/// program storage and lifetime as a global, just with the name only visible inside this function; see
/// the design doc's "static locals" note for how <see cref="CCodeGenerator"/> implements that with a
/// mangled global name rather than any new storage mechanism.</summary>
public sealed record CLocalVarDecl(CSourceLocation Location, string Name, CType Type, CExpr? Initializer, bool IsStatic) : CStmt(Location);

public sealed record CIfStmt(CSourceLocation Location, CExpr Condition, CStmt Then, CStmt? Else) : CStmt(Location);

public sealed record CWhileStmt(CSourceLocation Location, CExpr Condition, CStmt Body) : CStmt(Location);

public sealed record CDoWhileStmt(CSourceLocation Location, CStmt Body, CExpr Condition) : CStmt(Location);

public sealed record CForStmt(CSourceLocation Location, CStmt? Init, CExpr? Condition, CExpr? Update, CStmt Body) : CStmt(Location);

public sealed record CReturnStmt(CSourceLocation Location, CExpr? Value) : CStmt(Location);

public sealed record CBreakStmt(CSourceLocation Location) : CStmt(Location);

public sealed record CContinueStmt(CSourceLocation Location) : CStmt(Location);

public sealed record CGotoStmt(CSourceLocation Location, string Label) : CStmt(Location);

public sealed record CLabelStmt(CSourceLocation Location, string Label, CStmt Inner) : CStmt(Location);

/// <summary>A "case VALUE:" label -- like <see cref="CLabelStmt"/>, attaches to the statement that
/// follows it, and Value must be a compile-time integer constant (a literal, optionally negated;
/// see <see cref="CCodeGenerator"/>'s constant folder). Only meaningful directly or indirectly inside a
/// <see cref="CSwitchStmt"/>'s Body.</summary>
public sealed record CCaseLabelStmt(CSourceLocation Location, CExpr Value, CStmt Inner) : CStmt(Location);

/// <summary>A "default:" label -- see <see cref="CCaseLabelStmt"/>.</summary>
public sealed record CDefaultLabelStmt(CSourceLocation Location, CStmt Inner) : CStmt(Location);

/// <summary>
/// "switch (Selector) Body" with real C's own shape: Body is an ordinary statement (almost always a
/// <see cref="CCompoundStmt"/>) that may contain <see cref="CCaseLabelStmt"/>/<see
/// cref="CDefaultLabelStmt"/> anywhere inside it, including nested inside other statements -- so
/// fall-through (no "break" between two cases) works exactly like standard C. Codegen compiles Body's
/// statements in their natural sequence and generates a chain of equality comparisons up front to jump
/// into the right position; see the design doc for why this compiler does not (yet) use a true jump-
/// table/"tjmp" instruction.
/// </summary>
public sealed record CSwitchStmt(CSourceLocation Location, CExpr Selector, CStmt Body) : CStmt(Location);
