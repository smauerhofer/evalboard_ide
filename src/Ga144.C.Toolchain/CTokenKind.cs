namespace Ga144.C.Toolchain;

/// <summary>
/// The kind of a single preprocessing token, in the sense the C standard uses the term: the lexer's
/// output is a flat stream of these, produced before macro expansion and directive handling run.
/// </summary>
public enum CTokenKind
{
  /// <summary>An identifier or keyword -- keywords are not distinguished at this stage, since the
  /// preprocessor itself never needs to tell them apart (only a later compilation phase would).</summary>
  Identifier,

  /// <summary>A "pp-number": a run of digits, letters, '.', and (for exponents) a leading '+'/'-'
  /// after 'e'/'E'/'p'/'P' -- e.g. 123, 0x1A, 3.14, 1e-10, 42UL. Kept as raw text; only the constant
  /// expression evaluator (for #if/#elif) ever needs to parse one as a number.</summary>
  Number,

  /// <summary>A "..." string literal, Text including its surrounding quotes.</summary>
  StringLiteral,

  /// <summary>A '...' character literal, Text including its surrounding quotes.</summary>
  CharLiteral,

  /// <summary>An operator or other punctuation ("(", "##", "+=", etc).</summary>
  Punctuator,

  /// <summary>The end of a source line. Directive processing needs to know where a logical line ends
  /// (after line-splicing has already joined any backslash-newlines), and macro expansion must not
  /// cross this boundary. Horizontal whitespace and comments are never emitted as tokens at all --
  /// they are collapsed into <see cref="CToken.HasLeadingWhitespace"/> on whichever real token
  /// follows, which is all the stringize ('#') operator and text re-serialization actually need in
  /// order to avoid running two tokens together or losing meaningful spacing.</summary>
  NewLine,

  /// <summary>Anything the lexer does not otherwise recognize (e.g. a stray '@' or '$' outside a
  /// string/char literal). Passed through unchanged -- later compiler phases, not the preprocessor,
  /// are responsible for rejecting genuinely invalid tokens.</summary>
  Other,

  /// <summary>Marks the end of the token stream for a single file.</summary>
  EndOfFile,
}
