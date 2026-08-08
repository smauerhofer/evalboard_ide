namespace Ga144.Evb.Ide.Compiler;

/// <summary>
/// Small 18-bit FORTH interpreter used while compiling node source.
/// It deliberately models the documented F18A stack capacities: ten data-stack
/// entries and nine return-stack entries. Unlike the physical circular stacks,
/// compiler stack overflow and underflow are diagnosed instead of silently
/// overwriting values; this keeps source-generation errors deterministic.
/// </summary>
internal sealed class F18CompileTimeInterpreter
{
  public const int DataStackCapacity = 10;
  public const int ReturnStackCapacity = 9;

  private readonly List<int> _dataStack = [];
  private readonly List<int> _returnStack = [];
  private readonly Action<F18Diagnostic> _report;

  public F18CompileTimeInterpreter(Action<F18Diagnostic> report)
  {
    _report = report ?? throw new ArgumentNullException(nameof(report));
  }

  public IReadOnlyList<int> DataStack => _dataStack;
  public IReadOnlyList<int> ReturnStack => _returnStack;

  public bool TryPushData(int value, F18Token token) =>
      TryPush(_dataStack, DataStackCapacity, value, "data", token);

  public bool TryPopData(F18Token token, out int value) =>
      TryPop(_dataStack, "data", token, out value);

  public bool TryExecute(string word, F18Token token)
  {
    switch (word.ToLowerInvariant())
    {
      case "dup":
        return UnaryStackCopy(token, static stack => stack[^1]);
      case "drop":
        return Drop(token);
      case "swap":
        return Swap(token);
      case "over":
        return UnaryStackCopy(token, static stack => stack[^2], requiredDepth: 2);
      case "rot":
        return Rotate(token);
      case "nip":
        return Nip(token);
      case "tuck":
        return Tuck(token);
      case ">r":
        return MoveDataToReturn(token);
      case "r>":
        return MoveReturnToData(token);
      case "+":
        return Binary(token, static (left, right) => left + right);
      case "-":
        return Binary(token, static (left, right) => left - right);
      case "and":
        return Binary(token, static (left, right) => left & right);
      case "xor":
        return Binary(token, static (left, right) => left ^ right);
      case "not":
      case "invert":
        return Unary(token, static value => ~value);
      case "2*":
        return Unary(token, static value => value << 1);
      case "2/":
        return Unary(token, ArithmeticShiftRight);
      case "1+":
        return Unary(token, static value => value + 1);
      case "1-":
        return Unary(token, static value => value - 1);
      case "negate":
        return Unary(token, static value => -ToSigned(value));
      case "=":
        return Binary(token, static (left, right) => Truth(left == right));
      case "<>":
        return Binary(token, static (left, right) => Truth(left != right));
      case "<":
        return Binary(token, static (left, right) => Truth(ToSigned(left) < ToSigned(right)));
      case ">":
        return Binary(token, static (left, right) => Truth(ToSigned(left) > ToSigned(right)));
      case "0=":
        return Unary(token, static value => Truth(value == 0));
      case "0<":
        return Unary(token, static value => Truth(ToSigned(value) < 0));
      case "depth":
        TryPushData(_dataStack.Count, token);
        return true;
      case "rdepth":
        TryPushData(_returnStack.Count, token);
        return true;
      case "clear":
        _dataStack.Clear();
        return true;
      case "rclear":
        _returnStack.Clear();
        return true;
      default:
        return false;
    }
  }

  private bool Unary(F18Token token, Func<int, int> operation)
  {
    if (!RequireDepth(_dataStack, 1, "data", token))
    {
      return true;
    }

    _dataStack[^1] = Mask(operation(_dataStack[^1]));
    return true;
  }

  private bool Binary(F18Token token, Func<int, int, int> operation)
  {
    if (!RequireDepth(_dataStack, 2, "data", token))
    {
      return true;
    }

    int right = _dataStack[^1];
    int left = _dataStack[^2];
    _dataStack.RemoveAt(_dataStack.Count - 1);
    _dataStack[^1] = Mask(operation(left, right));
    return true;
  }

  private bool UnaryStackCopy(F18Token token, Func<List<int>, int> selector, int requiredDepth = 1)
  {
    if (!RequireDepth(_dataStack, requiredDepth, "data", token))
    {
      return true;
    }

    TryPushData(selector(_dataStack), token);
    return true;
  }

  private bool Drop(F18Token token)
  {
    if (!RequireDepth(_dataStack, 1, "data", token))
    {
      return true;
    }

    _dataStack.RemoveAt(_dataStack.Count - 1);
    return true;
  }

  private bool Swap(F18Token token)
  {
    if (!RequireDepth(_dataStack, 2, "data", token))
    {
      return true;
    }

    (_dataStack[^2], _dataStack[^1]) = (_dataStack[^1], _dataStack[^2]);
    return true;
  }

  private bool Rotate(F18Token token)
  {
    if (!RequireDepth(_dataStack, 3, "data", token))
    {
      return true;
    }

    int third = _dataStack[^3];
    _dataStack[^3] = _dataStack[^2];
    _dataStack[^2] = _dataStack[^1];
    _dataStack[^1] = third;
    return true;
  }

  private bool Nip(F18Token token)
  {
    if (!RequireDepth(_dataStack, 2, "data", token))
    {
      return true;
    }

    _dataStack.RemoveAt(_dataStack.Count - 2);
    return true;
  }

  private bool Tuck(F18Token token)
  {
    if (!RequireDepth(_dataStack, 2, "data", token))
    {
      return true;
    }

    if (_dataStack.Count >= DataStackCapacity)
    {
      ReportOverflow("data", DataStackCapacity, token);
      return true;
    }

    int top = _dataStack[^1];
    _dataStack.Insert(_dataStack.Count - 2, top);
    return true;
  }

  private bool MoveDataToReturn(F18Token token)
  {
    if (!RequireDepth(_dataStack, 1, "data", token))
    {
      return true;
    }

    if (_returnStack.Count >= ReturnStackCapacity)
    {
      ReportOverflow("return", ReturnStackCapacity, token);
      return true;
    }

    int value = _dataStack[^1];
    _dataStack.RemoveAt(_dataStack.Count - 1);
    _returnStack.Add(value);
    return true;
  }

  private bool MoveReturnToData(F18Token token)
  {
    if (!RequireDepth(_returnStack, 1, "return", token))
    {
      return true;
    }

    if (_dataStack.Count >= DataStackCapacity)
    {
      ReportOverflow("data", DataStackCapacity, token);
      return true;
    }

    int value = _returnStack[^1];
    _returnStack.RemoveAt(_returnStack.Count - 1);
    _dataStack.Add(value);
    return true;
  }

  private bool TryPush(List<int> stack, int capacity, int value, string name, F18Token token)
  {
    if (stack.Count >= capacity)
    {
      ReportOverflow(name, capacity, token);
      return false;
    }

    stack.Add(Mask(value));
    return true;
  }

  private bool TryPop(List<int> stack, string name, F18Token token, out int value)
  {
    if (!RequireDepth(stack, 1, name, token))
    {
      value = 0;
      return false;
    }

    value = stack[^1];
    stack.RemoveAt(stack.Count - 1);
    return true;
  }

  private bool RequireDepth(List<int> stack, int required, string name, F18Token token)
  {
    if (stack.Count >= required)
    {
      return true;
    }

    _report(new F18Diagnostic(
        F18DiagnosticSeverity.Error,
        "F18I001",
        $"Compile-time {name} stack underflow: '{token.Text}' requires {required} value(s), but the stack contains {stack.Count}.",
        token.Location));
    return false;
  }

  private void ReportOverflow(string name, int capacity, F18Token token) =>
      _report(new F18Diagnostic(
          F18DiagnosticSeverity.Error,
          "F18I002",
          $"Compile-time {name} stack overflow: the F18A-compatible limit is {capacity} value(s).",
          token.Location));

  private static int ArithmeticShiftRight(int value)
  {
    int signed = ToSigned(value);
    return signed >> 1;
  }

  private static int ToSigned(int value)
  {
    value &= F18InstructionSet.WordMask;
    return (value & 0x20000) == 0 ? value : value - 0x40000;
  }

  private static int Truth(bool value) => value ? F18InstructionSet.WordMask : 0;
  private static int Mask(int value) => value & F18InstructionSet.WordMask;
}
