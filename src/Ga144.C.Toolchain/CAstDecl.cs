namespace Ga144.C.Toolchain;

public sealed record CParameter(string Name, CType Type);

/// <summary>A function declaration. Body is null for a prototype ("declare only, define elsewhere or
/// in another translation unit") -- <see cref="CCodeGenerator"/> emits a ".import" for a called
/// function that is only ever declared, never defined, in the file being compiled, exactly like a
/// hand-written ".casm" file would for an external symbol.</summary>
public sealed record CFunctionDecl(CSourceLocation Location, string Name, CType ReturnType, IReadOnlyList<CParameter> Parameters, CCompoundStmt? Body, bool IsStatic);

/// <summary>A file-scope variable. IsExtern means "declared here, defined elsewhere" (no storage is
/// emitted, and it becomes an ".import" wherever it's used); IsStatic means internal linkage --
/// visible only within this file's own generated assembly, so its CVM label does not need
/// "".export"".</summary>
public sealed record CGlobalVarDecl(CSourceLocation Location, string Name, CType Type, CExpr? Initializer, bool IsStatic, bool IsExtern);

public sealed record CTranslationUnit(IReadOnlyList<CFunctionDecl> Functions, IReadOnlyList<CGlobalVarDecl> Globals);
