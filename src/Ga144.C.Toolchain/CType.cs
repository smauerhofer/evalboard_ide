namespace Ga144.C.Toolchain;

public enum CTypeKind
{
  Void,
  Int,
  UnsignedInt,
  Char,
  UnsignedChar,
  Float,
  Long,
  Pointer,
  Array,
  Function,
  Struct,
}

/// <summary>One member of a <see cref="CTypeKind.Struct"/> type: its name, its own type, and its
/// word-granular <see cref="Offset"/> from the struct's own base address -- this compiler's struct
/// layout has no padding of any kind (see <see cref="CType.StructOf"/>'s own remarks), so <see
/// cref="Offset"/> is always exactly the sum of every earlier member's own <c>SizeInWords</c>.</summary>
public sealed record CStructMember(string Name, CType Type, int Offset);

/// <summary>
/// A C type, sized for the CVM's own word-addressed memory: there is no byte addressing anywhere in
/// this machine, so every scalar and every pointer -- <c>char</c> included -- occupies exactly one
/// CVM word (see <see cref="SizeInWords"/>). This is a deliberate simplification over standard C's
/// usual "char is the smallest addressable unit, int is usually wider" model, made because the target
/// hardware genuinely has no smaller unit to give <c>char</c>; <c>char</c> still exists as its own
/// type purely so a programmer's intent ("this is a small/byte-range value") is preserved and checked,
/// not because it saves any storage.
/// </summary>
public sealed class CType : IEquatable<CType>
{
  public static readonly CType Void = new() { Kind = CTypeKind.Void };
  public static readonly CType Int = new() { Kind = CTypeKind.Int };
  public static readonly CType UnsignedInt = new() { Kind = CTypeKind.UnsignedInt };
  public static readonly CType Char = new() { Kind = CTypeKind.Char };
  public static readonly CType UnsignedChar = new() { Kind = CTypeKind.UnsignedChar };

  /// <summary>
  /// Added 2026-09-26, per Stefan's own float-ABI dictation (<c>claude/cvm-abi.md</c> section 2.3) --
  /// the CVM's 32-bit IEEE-754 <c>float</c>, backed by node 305/306's 8-register floating-point file and
  /// its <c>fadd</c>/<c>fsub</c>/<c>fmul</c>/<c>fdiv</c>/<c>fneg</c>/<c>fabs</c>/<c>fmin</c>/<c>fmax</c>/
  /// <c>fmove</c>/<c>fconst</c>/<c>fpop</c>/<c>fpush</c> opcodes. UNLIKE every other type this class
  /// documents above, <c>float</c> is <b>two</b> CVM words (see <see cref="SizeInWords"/>) -- the first
  /// type in this compiler's history that isn't exactly one. Per Stefan verbatim: "for normal functions
  /// float parameter are put on the stack as a 32-bit value little endian word. so the lower value is on
  /// the lower address. this is also valid for float in memory. low word at memory[adr], high word at
  /// memory[adr+1]." <see cref="CCodeGenerator"/> restricts <c>float</c>, for now, to a plain scalar:
  /// <c>float *</c> (a pointer to it) and <c>float[]</c> (an array of it) are both rejected with a clear
  /// diagnostic at parse time (see <see cref="CParser"/>'s own remarks) rather than silently mishandled,
  /// since neither pointer arithmetic/scaling nor array-element addressing has been generalized for a
  /// multi-word element type yet -- a deliberate SCOPE decision for this first pass ("for now just add
  /// basic float support to the C compiler"), not a hardware hypothesis, and one of the first things to
  /// revisit when full <c>float</c> support (a float-library, conversions, comparisons) is added.
  /// </summary>
  public static readonly CType Float = new() { Kind = CTypeKind.Float };

  /// <summary>
  /// Added 2026-09-26, per Stefan verbatim: "support 'long'. a 'long' is represented as a 2 word little
  /// endian unit. do not generate code for handling 'long' yet, because that has to be defined first in
  /// the CVM." Like <see cref="Float"/>, this is a TWO-word type (see <see cref="SizeInWords"/>) --
  /// low word at the lower address, high word at the higher one, the exact same little-endian convention
  /// Stefan already dictated for <c>float</c> (see <see cref="Float"/>'s own remarks) -- but UNLIKE
  /// <c>float</c>, which got a complete dedicated codegen path (its own lvalue kind, load/store, register
  /// save/restore), <c>long</c> deliberately gets NONE yet, per Stefan's own instruction above: this is a
  /// real ISA gap (the CVM itself has no defined <c>long</c> instructions), not a compiler scope choice
  /// the way e.g. <c>float</c> comparisons/int-float conversion are.
  ///
  /// What DOES work today, because none of it needs an actual `long` VALUE operation, only the correct
  /// word count: declaring a <c>long</c> local/global/static/struct-member variable (correctly sized and
  /// laid out via <see cref="SizeInWords"/>, and zero-filled if a global/static has no initializer),
  /// taking its address (<c>&amp;</c>), forming <c>long *</c>/<c>long[]</c>, and pointer arithmetic/
  /// indexing on either (pure address math, already generic over element size -- see <see
  /// cref="CCodeGenerator"/>'s own doc comment). What is explicitly REJECTED with a clear diagnostic,
  /// exactly the way <c>struct</c>'s own "whole value" restriction already is (see <see
  /// cref="StructOf"/>'s own remarks) rather than silently mishandled: reading a <c>long</c> value from
  /// anywhere (a bare variable, a dereferenced <c>long *</c>, an array element, a struct member),
  /// assigning/compound-assigning to one, <c>++</c>/<c>--</c> on one, casting to or from one, passing or
  /// returning one BY VALUE in a function signature (parser-level, mirroring <c>struct</c>'s own
  /// by-value restriction), and initializing a global/static <c>long</c> with anything other than leaving
  /// it zero-filled (a general integer constant would need correct sign-extension into the high word,
  /// which is exactly the kind of "long instruction" this compiler isn't implementing yet).
  /// </summary>
  public static readonly CType Long = new() { Kind = CTypeKind.Long };

  public required CTypeKind Kind { get; init; }

  /// <summary>Pointer/Array only: what it points to / holds.</summary>
  public CType? ElementType { get; init; }

  /// <summary>Array only: element count, or -1 for an incomplete ("T[]") array type.</summary>
  public int ArrayLength { get; init; } = -1;

  /// <summary>Function only.</summary>
  public CType? ReturnType { get; init; }

  /// <summary>Function only.</summary>
  public IReadOnlyList<CType> ParameterTypes { get; init; } = [];

  /// <summary>Struct only: the tag name ("struct Tag") -- see <see cref="StructOf"/>'s own remarks.
  /// This compiler requires every struct to be tagged; there is no anonymous-struct support.</summary>
  public string? Tag { get; init; }

  /// <summary>
  /// Struct only: this tag's own member list, in declaration order. MUTABLE and shared by every <see
  /// cref="CType"/> instance for the same tag (<see cref="CParser"/> keeps exactly one <see
  /// cref="CType"/> per tag, in its own per-file tag table, and hands the SAME instance back every time
  /// "struct Tag" is written again) -- <see cref="StructOf"/> creates it empty (an incomplete/
  /// forward-declared tag) and <see cref="CParser"/> appends to this same list as it parses the tag's
  /// own <c>{ ... }</c> body, which is what makes a SELF-REFERENTIAL struct possible at all: a member
  /// declared as "struct Tag *next" only needs a pointer's own fixed 1-word size, which never depends on
  /// whether Tag's own member list has finished filling in yet (a BY-VALUE self-reference, the one case
  /// that genuinely cannot work, is rejected explicitly -- see <see cref="CParser"/>'s own struct-body
  /// parsing). Empty means either "genuinely no members" (an unusual but not rejected degenerate case)
  /// or "declared but not yet defined" (a forward reference, e.g. "struct Node;") -- <see
  /// cref="CCodeGenerator"/> and <see cref="CParser"/> both treat an empty member list used BY VALUE
  /// (not behind a pointer) as an incomplete type and reject it with a diagnostic, since this compiler
  /// has no separate "declared but incomplete" marker beyond "no members yet."
  /// </summary>
  public List<CStructMember>? Members { get; init; }

  public static CType PointerTo(CType element) => new() { Kind = CTypeKind.Pointer, ElementType = element };

  public static CType ArrayOf(CType element, int length) => new() { Kind = CTypeKind.Array, ElementType = element, ArrayLength = length };

  public static CType FunctionOf(CType returnType, IReadOnlyList<CType> parameterTypes) =>
      new() { Kind = CTypeKind.Function, ReturnType = returnType, ParameterTypes = parameterTypes };

  /// <summary>
  /// Creates a new, empty (incomplete) struct type for <paramref name="tag"/> -- added 2026-09-26, per
  /// Stefan's own "add 'struct' to the C language" instruction (prompted by a real `libc` `heap.c` build
  /// failure -- a free-list heap allocator, the classic C pattern that needs a self-referential struct).
  ///
  /// <b>Word-granular, no-padding layout</b>, consistent with every other type this compiler supports
  /// (see this class's own remarks): a struct's <see cref="SizeInWords"/> is exactly the sum of its own
  /// members' sizes, and each member's own <see cref="CStructMember.Offset"/> is exactly the sum of
  /// every earlier member's size -- there is no alignment padding of any kind, because this machine has
  /// no notion of "misaligned" access to begin with (every access is already a whole-word access).
  ///
  /// <b>Nominal typing, by tag, not structural.</b> Two <see cref="CType"/> instances with the same
  /// <see cref="Tag"/> are considered <see cref="Equals(CType?)"/> regardless of their own <see
  /// cref="Members"/> list's current contents -- in practice this never matters, since <see
  /// cref="CParser"/> only ever creates ONE <see cref="CType"/> instance per tag per file (see <see
  /// cref="Members"/>'s own remarks) and hands that same instance back every time, but nominal-by-tag
  /// comparison is also simply the semantically correct rule for C's own struct-tag namespace (two
  /// DIFFERENTLY-tagged structs with identical member lists are still different types).
  ///
  /// <b>Deliberate scope limits for this first pass (see <see cref="CParser"/>/<see
  /// cref="CCodeGenerator"/>'s own remarks for exactly where each is enforced), mirroring the precedent
  /// <c>float</c> already set (a multi-word type gets the SAME kind of explicit, honest carve-out, not
  /// silently wrong codegen):</b> a struct member's own type may be a scalar/pointer or another struct
  /// BY VALUE (but never <c>float</c>, and never an array -- both rejected with a clear diagnostic, the
  /// same way <c>float *</c>/<c>float[]</c> already are); a struct itself can never be passed or
  /// returned BY VALUE in a function signature (only "struct Tag *" is supported there); there is no
  /// array-of-struct; and a struct POINTER supports "->" member access but not pointer arithmetic/
  /// indexing (`p+1`, `p[i]`, `p++`/`p--`) -- this compiler's pointer arithmetic and array indexing are
  /// multiplication-free throughout (see this class's own remarks), which is exactly why they only ever
  /// worked correctly for a one-word element type; a struct pointee breaks that assumption the same way
  /// a `float` element would. Reading or assigning an ENTIRE struct value in one expression (`s1 = s2;`,
  /// or using a struct-typed variable/member directly as a value) is likewise not yet supported --
  /// access it one member at a time instead, or take its address with `&amp;`.
  /// </summary>
  public static CType StructOf(string tag) => new() { Kind = CTypeKind.Struct, Tag = tag, Members = [] };

  public bool IsVoid => Kind == CTypeKind.Void;
  public bool IsPointer => Kind == CTypeKind.Pointer;
  public bool IsArray => Kind == CTypeKind.Array;
  public bool IsFunction => Kind == CTypeKind.Function;
  public bool IsStruct => Kind == CTypeKind.Struct;
  public bool IsIntegral => Kind is CTypeKind.Int or CTypeKind.UnsignedInt or CTypeKind.Char or CTypeKind.UnsignedChar or CTypeKind.Long;
  public bool IsFloat => Kind == CTypeKind.Float;

  /// <summary>Added 2026-09-26 alongside <see cref="Long"/>'s own remarks -- true for the 2-word integer
  /// type this compiler's general one-word codegen cannot yet actually read/write/compute with as a
  /// value. Named separately from <see cref="IsStruct"/> (the other type sharing that same "no whole-
  /// value codegen yet" restriction -- see <see cref="CCodeGenerator"/>'s own <c>IsUnsupportedWholeValueType</c>
  /// for where both are checked together) purely so each call site can give its own precise diagnostic.
  /// </summary>
  public bool IsLong => Kind == CTypeKind.Long;

  public bool IsScalar => IsIntegral || IsPointer || IsFloat;

  /// <summary>Whether arithmetic on this type is unsigned -- used to pick between signed and unsigned
  /// comparison/shift/divide operations. A pointer is treated as unsigned for this purpose (address
  /// comparisons and shifts are not meaningful anyway; only division of a pointer never happens).
  /// </summary>
  public bool IsUnsigned => Kind is CTypeKind.UnsignedInt or CTypeKind.UnsignedChar or CTypeKind.Pointer;

  /// <summary>Size in CVM words. See the type's own doc comment for why every scalar/pointer is 1 --
  /// except <see cref="CTypeKind.Float"/> (added 2026-09-26), which is 2 (see <see cref="Float"/>'s own
  /// remarks), and <see cref="CTypeKind.Long"/> (added 2026-09-26), which is likewise 2 (see <see
  /// cref="Long"/>'s own remarks) despite <see cref="CCodeGenerator"/> not yet implementing any actual
  /// value-level codegen for it -- the word count itself is exactly what makes declaring/sizing/
  /// addressing a <c>long</c> already correct today, independent of whether its VALUE can be used
  /// yet.</summary>
  public int SizeInWords => Kind switch
  {
    CTypeKind.Void or CTypeKind.Function => 0,
    CTypeKind.Float or CTypeKind.Long => 2,
    CTypeKind.Array => Math.Max(ArrayLength, 0) * (ElementType?.SizeInWords ?? 1),
    CTypeKind.Struct => Members?.Sum(m => Math.Max(m.Type.SizeInWords, 1)) ?? 0,
    _ => 1,
  };

  /// <summary>The type an expression of this type becomes when used as a value rather than named
  /// directly as an array -- an array decays to a pointer to its first element, exactly like standard
  /// C; every other type is unchanged.</summary>
  public CType Decay() => Kind == CTypeKind.Array ? PointerTo(ElementType!) : this;

  public bool Equals(CType? other)
  {
    if (other is null)
    {
      return false;
    }

    if (ReferenceEquals(this, other))
    {
      return true;
    }

    if (Kind != other.Kind)
    {
      return false;
    }

    return Kind switch
    {
      CTypeKind.Pointer => Equals(ElementType, other.ElementType),
      CTypeKind.Array => Equals(ElementType, other.ElementType) && ArrayLength == other.ArrayLength,
      CTypeKind.Function => Equals(ReturnType, other.ReturnType) && ParameterTypes.SequenceEqual(other.ParameterTypes),
      // Nominal, by tag -- see StructOf's own remarks on why this is both harmless in practice (CParser
      // never creates two instances for the same tag) and the semantically correct rule regardless.
      CTypeKind.Struct => Tag == other.Tag,
      _ => true,
    };
  }

  public override bool Equals(object? obj) => Equals(obj as CType);

  public override int GetHashCode() => HashCode.Combine(Kind, ElementType, Kind == CTypeKind.Array ? ArrayLength : 0, Kind == CTypeKind.Struct ? Tag : null);

  public override string ToString() => Kind switch
  {
    CTypeKind.Void => "void",
    CTypeKind.Int => "int",
    CTypeKind.UnsignedInt => "unsigned int",
    CTypeKind.Char => "char",
    CTypeKind.UnsignedChar => "unsigned char",
    CTypeKind.Float => "float",
    CTypeKind.Long => "long",
    CTypeKind.Pointer => $"{ElementType} *",
    CTypeKind.Array => ArrayLength >= 0 ? $"{ElementType}[{ArrayLength}]" : $"{ElementType}[]",
    CTypeKind.Function => $"{ReturnType} ({string.Join(", ", ParameterTypes)})",
    CTypeKind.Struct => $"struct {Tag}",
    _ => Kind.ToString(),
  };
}