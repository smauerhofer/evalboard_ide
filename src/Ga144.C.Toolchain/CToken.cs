namespace Ga144.C.Toolchain;

/// <summary>
/// A single preprocessing token: its kind, its exact source text, where it came from, and whether at
/// least one whitespace/comment character preceded it on the same line. <see cref="IsAtLineStart"/>
/// and <see cref="HasLeadingWhitespace"/> exist purely to support directive detection ("#" only
/// introduces a directive as the first non-whitespace token on a line) and correct macro
/// re-stringification (spacing between tokens must be preserved when text is reassembled, e.g. by the
/// '#' stringize operator or when writing preprocessed output back out).
/// </summary>
public sealed record CToken(CTokenKind Kind, string Text, CSourceLocation Location, bool IsAtLineStart, bool HasLeadingWhitespace)
{
  /// <summary>True for Identifier tokens -- the only kind that can ever name a macro or be looked up
  /// as one during expansion.</summary>
  public bool IsIdentifier => Kind == CTokenKind.Identifier;

  public bool Is(CTokenKind kind, string text) => Kind == kind && Text == text;

  public bool IsPunctuator(string text) => Kind == CTokenKind.Punctuator && Text == text;

  public override string ToString() => Text;
}
