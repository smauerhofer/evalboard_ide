namespace Ga144.C.Toolchain;

public sealed record CParameter(string Name, CType Type);

/// <summary>A function declaration. Body is null for a prototype ("declare only, define elsewhere or
/// in another translation unit") -- <see cref="CCodeGenerator"/> emits a ".import" for a called
/// function that is only ever declared, never defined, in the file being compiled, exactly like a
/// hand-written ".casm" file would for an external symbol.
///
/// <b><c>IsFastcall</c>/<c>IsLower</c> -- added 2026-09-07, per <c>claude/cvm-abi.md</c>'s own
/// "__fastcall"/"__lower" ABI dictation. Parsed and recorded here; NOT YET consumed by
/// <see cref="CCodeGenerator"/>, which still treats every function as the plain by-stack convention
/// regardless of these flags.</b> <c>IsFastcall</c> is true only for a function declared
/// <c>__fastcall</c>. <c>IsLower</c> is the EFFECTIVE "must be placed in the lower half of memory,
/// 0x0000-0x7FFF" requirement -- true if the function was declared <c>__lower</c>, OR if it was
/// declared <c>__fastcall</c> (per Stefan's own words, "__fastcall implies also __lower, so __fastcall
/// includes __lower") -- so a caller only ever needs to check <c>IsLower</c> to decide whether a call
/// to this function can use the compact 1-word <c>call</c> encoding instead of the 2-word <c>lcall</c>;
/// it never needs to check <c>IsFastcall</c> for that specific question. <see cref="CParser"/>'s own
/// parsing of these two keywords also rejects, with a specific diagnostic, a <c>__fastcall</c> function
/// declaring more than 4 pointer parameters (node 306 has only 4 address registers, <c>ar[0..3]</c>) --
/// see <see cref="CParser"/>'s own remarks.</summary>
public sealed record CFunctionDecl(CSourceLocation Location, string Name, CType ReturnType, IReadOnlyList<CParameter> Parameters, CCompoundStmt? Body, bool IsStatic, bool IsFastcall, bool IsLower);

/// <summary>A file-scope variable. IsExtern means "declared here, defined elsewhere" (no storage is
/// emitted, and it becomes an ".import" wherever it's used); IsStatic means internal linkage --
/// visible only within this file's own generated assembly, so its CVM label does not need
/// "".export"".</summary>
public sealed record CGlobalVarDecl(CSourceLocation Location, string Name, CType Type, CExpr? Initializer, bool IsStatic, bool IsExtern);

public sealed record CTranslationUnit(IReadOnlyList<CFunctionDecl> Functions, IReadOnlyList<CGlobalVarDecl> Globals);