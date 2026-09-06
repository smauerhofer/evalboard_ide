namespace Ga144.C.Toolchain;

public enum CTypeKind
{
  Void,
  Int,
  UnsignedInt,
  Char,
  UnsignedChar,
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
  public bool IsScalar => IsIntegral || IsPointer;

  /// <summary>Whether arithmetic on this type is unsigned -- used to pick between signed and unsigned
  /// comparison/shift/divide operations. A pointer is treated as unsigned for this purpose (address
  /// comparisons and shifts are not meaningful anyway; only division of a pointer never happens).
  /// </summary>
  public bool IsUnsigned => Kind is CTypeKind.UnsignedInt or CTypeKind.UnsignedChar or CTypeKind.Pointer;

  /// <summary>Size in CVM words. See the type's own doc comment for why every scalar/pointer is 1.
  /// </summary>
  public int SizeInWords => Kind switch
  {
    CTypeKind.Void or CTypeKind.Function => 0,
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
    CTypeKind.Pointer => $"{ElementType} *",
    CTypeKind.Array => ArrayLength >= 0 ? $"{ElementType}[{ArrayLength}]" : $"{ElementType}[]",
    CTypeKind.Function => $"{ReturnType} ({string.Join(", ", ParameterTypes)})",
    _ => Kind.ToString(),
  };
}
