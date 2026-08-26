using Ga144.Evb.Ide.Compiler;

namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 708's real, unmirrored factory ROM -- <c>macro rom_async_boot</c>
/// (rom_relay + rom_warm + rom_async + rom_shift), extracted verbatim from
/// this project's system ROM library (<c>data/ga144-rom.yaml</c>, system
/// macros of the same names) so <see cref="CvmBootStreamBuilder"/> can
/// compile node 708's RAM program against its real ROM exports (18ibits,
/// delay, and so on) without this compile-only builder needing to load the
/// ROM library file or take a dependency on Ga144RomLibrary/YamlDotNet.
///
/// Node 708 is the only node in this CVM cluster that needs its own real
/// factory ROM: every other node (607, 507, 506, 508, 606, 608, 407) is an
/// ordinary internal F18A node compiled with just the compiler's built-in
/// common ROM words, but 708 is the actual async serial boot node -- its RAM
/// program (see <see cref="Node708Program"/>) calls 18ibits and delay, both
/// genuine words in node 708's real ROM, not common words available on
/// every node.
///
/// Text below is copied byte-for-byte from the system macros of these same
/// names in data/ga144-rom.yaml -- not retyped -- so this can never silently
/// drift from the ROM library every other verification path in this project
/// already trusts.
/// </summary>
internal static class Node708Rom
{
  /// <summary>Node 708's own ROM node entry in data/ga144-rom.yaml: <c>sourceCode: macro rom_async_boot</c>.</summary>
  public const string RomAsyncBootSource = "macro rom_async_boot";

  /// <summary>
  /// Resolves one of the four system macros <see cref="RomAsyncBootSource"/> expands to. Pass as
  /// an <c>F18CompilerOptions.MacroResolver</c> when compiling node 708's ROM.
  /// </summary>
  public static F18MacroResolution ResolveSystemMacro(string name, F18MacroLookupScope scope)
  {
    string normalized = (name ?? string.Empty).Trim();
    return SystemMacros.TryGetValue(normalized, out string? source)
        ? F18MacroResolution.FromSource(normalized, source, F18MacroKind.System)
        : F18MacroResolution.Failure($"No macro named '{normalized}' is defined.");
  }

  private static readonly IReadOnlyDictionary<string, string> SystemMacros =
      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        ["rom_async_boot"] = """
      macro rom_relay
      macro rom_warm
      macro rom_async
      macro rom_shift
      """,
        ["rom_relay"] = """
      # 0xA1 org
      : relay ( a) r> a! @+ >r @+
        zif drop ahead [ swap ] then r> over >r @p a relay !b !b !b
        begin @+ !b unext
      : done then a >r a! ;
      """,
        ["rom_warm"] = """
      # 0xA9 org
      : warm await ;
      """,
        ["rom_async"] = """
      # 0xCB equ 18ibits
      : cold   x31A5. a! @  @b .. -if
      : ser-exec ( x - d)   18ibits drop >r 18ibits drop a! 18ibits
      : ser-copy ( xnx-d)   drop >r zif ;  then begin 18ibits drop !+ next ;  then drop avail lit >r >r ;
      : wait ( x-1/1)   begin . drop @b -until  . drop ;
      : sync ( x-3/2-d)   dup dup wait  xor inv >r begin @b . -if . drop *next await ;  then . drop r> inv 2/ ;
      : start ( dw-4/2-dw,io) dup wait over dup 2/ . + >r
      : delay ( -1/1-io) .loc begin @b . -if then . drop next @b ;
      ( 18ibits ( x-4/6-dwx) .loc sync sync dup start ( 2bits) leap leap
      : byte   then drop start leap
      : 4bits   then leap
      : 2bits   then then leap
      : 1bit ( nw,io - nw,io) then >r 2/ r> over xor x20000. and xor over >r delay ;

      """,
        ["rom_shift"] = """
      : lsh ( w n-1 - w') for 2* unext ;
      : rsh ( w n-1 - w') for 2/ unext ;
      """,
      };
}
