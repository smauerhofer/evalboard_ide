namespace Ga144.C.Toolchain;

public enum CTypeKind
{
  Void,
  Int,
  UnsignedInt,
  Char,
  UnsignedChar,
  Float,
  Pointer,
  Array,
  Function,
}

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

  public required CTypeKind Kind { get; init; }

  /// <summary>Pointer/Array only: what it points to / holds.</summary>
  public CType? ElementType { get; init; }

  /// <summary>Array only: element count, or -1 for an incomplete ("T[]") array type.</summary>
  public int ArrayLength { get; init; } = -1;

  /// <summary>Function only.</summary>
  public CType? ReturnType { get; init; }

  /// <summary>Function only.</summary>
  public IReadOnlyList<CType> ParameterTypes { get; init; } = [];

  public static CType PointerTo(CType element) => new() { Kind = CTypeKind.Pointer, ElementType = element };

  public static CType ArrayOf(CType element, int length) => new() { Kind = CTypeKind.Array, ElementType = element, ArrayLength = length };

  public static CType FunctionOf(CType returnType, IReadOnlyList<CType> parameterTypes) =>
      new() { Kind = CTypeKind.Function, ReturnType = returnType, ParameterTypes = parameterTypes };

  public bool IsVoid => Kind == CTypeKind.Void;
  public bool IsPointer => Kind == CTypeKind.Pointer;
  public bool IsArray => Kind == CTypeKind.Array;
  public bool IsFunction => Kind == CTypeKind.Function;
  public bool IsIntegral => Kind is CTypeKind.Int or CTypeKind.UnsignedInt or CTypeKind.Char or CTypeKind.UnsignedChar;
  public bool IsFloat => Kind == CTypeKind.Float;
  public bool IsScalar => IsIntegral || IsPointer || IsFloat;

  /// <summary>Whether arithmetic on this type is unsigned -- used to pick between signed and unsigned
  /// comparison/shift/divide operations. A pointer is treated as unsigned for this purpose (address
  /// comparisons and shifts are not meaningful anyway; only division of a pointer never happens).
  /// </summary>
  public bool IsUnsigned => Kind is CTypeKind.UnsignedInt or CTypeKind.UnsignedChar or CTypeKind.Pointer;

  /// <summary>Size in CVM words. See the type's own doc comment for why every scalar/pointer is 1 --
  /// except <see cref="CTypeKind.Float"/> (added 2026-09-26), which is 2 (see <see cref="Float"/>'s own
  /// remarks).</summary>
  public int SizeInWords => Kind switch
  {
    CTypeKind.Void or CTypeKind.Function => 0,
    CTypeKind.Float => 2,
    CTypeKind.Array => Math.Max(ArrayLength, 0) * (ElementType?.SizeInWords ?? 1),
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
      _ => true,
    };
  }

  public override bool Equals(object? obj) => Equals(obj as CType);

  public override int GetHashCode() => HashCode.Combine(Kind, ElementType, Kind == CTypeKind.Array ? ArrayLength : 0);

  public override string ToString() => Kind switch
  {
    CTypeKind.Void => "void",
    CTypeKind.Int => "int",
    CTypeKind.UnsignedInt => "unsigned int",
    CTypeKind.Char => "char",
    CTypeKind.UnsignedChar => "unsigned char",
    CTypeKind.Float => "float",
    CTypeKind.Pointer => $"{ElementType} *",
    CTypeKind.Array => ArrayLength >= 0 ? $"{ElementType}[{ArrayLength}]" : $"{ElementType}[]",
    CTypeKind.Function => $"{ReturnType} ({string.Join(", ", ParameterTypes)})",
    _ => Kind.ToString(),
  };
}