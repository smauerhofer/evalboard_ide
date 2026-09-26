# C Debugger, and the shared PC/breakpoint gutter (2026-09-26)

Stefan's request, verbatim: "next I need a C debugger similar to the existing CVM debugger. It is used
to test C code. the text in the debugger source is placed inside a hidden `void main () {...}` block.
all include files from all libraries are automatically included for the compiler. all libraries are
automatically included for the linker. the runtime display is disassembled code mixed with the
corresponding C code as comments. there is a separate column after the address for the current program
counter position and in the same column, breakpoints can be set and cleared. Also use the same column
for the old CVM debugger."

This is a new window (`Ga144.Evb.Ide.ViewModels.CDebuggerViewModel` / `Views.CDebuggerWindow`) plus a
retrofit of the existing CVM Debugger's own memory inspector (`CvmDebuggerViewModel` /
`Views.CvmDebuggerWindow`) to give both windows the same PC/breakpoint gutter column and, for the C
Debugger, a "C source" column showing the originating C statement alongside its disassembly.

## Architecture: composition, not reimplementation

`CDebuggerViewModel` does **not** reimplement Start/Step/Continue/Run/Pause/Stop, breakpoints, the
transaction log, or the memory inspector. It owns only the C-specific half (the source text and the
compile/assemble/link pipeline) and wraps a genuine, independent `CvmDebuggerViewModel` instance
(`Debugger` property), constructed with the exact same chip/romLibrary/userMacros/kraken/endpoint/
save-back arguments `Views.ChipWindow`'s existing "CVM Debugger" button already passes a plain
`CvmDebuggerViewModel`. `CDebuggerViewModel.CompileAndLoad()`'s only interaction with it is a single
call to a new `CvmDebuggerViewModel.LoadImage(CvmImage, string, IReadOnlyDictionary<int,string>?)`
method once compiling, assembling, and linking succeed.

This is what makes "use the same column for the old CVM debugger" literally true rather than merely
similar in spirit: the C Debugger's memory inspector *is* a `CvmDebuggerViewModel.MemoryRows`
collection -- the exact same type, the exact same `ToggleBreakpointAtAddress` gutter click target, the
exact same `CvmMemoryRowViewModel` row shape -- not a lookalike copy.

`CDebuggerWindow.xaml` binds most of its controls straight through the wrapper
(`{Binding Debugger.StartCommand}`, `{Binding Debugger.MemoryRows}`, etc.), and its own top-level
`SourceCodeText`/`DiagnosticsText`/`CompileAndLoadCommand` for the C-specific half. `CDebuggerViewModel`
subscribes to `Debugger.PropertyChanged` for `IsBusy` so `CompileAndLoadCommand`'s own `CanExecute`
(disabled while the wrapped debugger is mid Step/Continue/Start) stays correctly in sync --
`Debugger`'s own internal `NotifyCommandStates()` only re-queries its *own* commands, since it has no
idea this wrapper's command exists.

## Hidden `void main() { ... }`

Per Stefan's own instruction, the editor holds only declarations and statements, never the function
signature. `CDebuggerViewModel.CompileAndLoad()` builds the actual translation unit as:

```csharp
string wrappedSource = "void main() { " + SourceCodeText + "\n}\n";
```

The wrapper text is added to the **front of line 1 only**, never as a separate line, so a compile
diagnostic's reported line number still matches the editor's own line numbers exactly -- only a column
offset on line 1 itself is affected. `DebuggerSourceFileName` (`"<c debugger>"`) is the fixed file name
threaded through `CCompiler.Compile` for this purpose; it must stay the same string every time, since
it is also the one file name `CCodeGenerator` will accept when deciding whether to emit a source-comment
label for a given statement (see below) -- statements from an `#include`d header never get one, since
there is no cross-file source map to draw on.

## "All include files/libraries from all libraries are automatically included"

Deliberately **different** from an ordinary C project's Build, which only looks at a project's own
checkbox-selected `AvailableLibraries` (`CProjectViewModel`). The C Debugger has no backing `CProject`
of its own to hold such a checkbox list against -- its "source file" is a snippet typed directly into
the window, not a project on disk -- so "automatically include everything, unconditionally" is the
only sensible default for a quick scratch test.

`CDebuggerViewModel.CompileAndLoad()` enumerates every project folder directly under the shared "libs"
workspace directory (`ChipViewModel.CLibsDirectoryPath`, the same directory
`MainWindowViewModel.CLibsDirectoryPath` names), filters to `CProject.Kind == CProjectKind.Library`,
and for each one unconditionally:

- adds its `IncludeDirectoryPath` to the compiler's include search path, and
- adds its already-built `LibraryOutputPath` (`.galib`) to the linker's library inputs, **if it
  exists**.

A library that hasn't been built yet (no `.galib` on disk) is skipped with a warning message, not a
hard failure -- the snippet under test may not need it at all; if it does, `CvmLinker`'s own "undefined
reference" error says so clearly once linking actually runs.

**Flagged for Stefan's confirmation**: this "always all libraries, no opt-out" behavior is a deliberate
default, not something he specified beyond "automatically included" -- if a library with a name
collision or an expensive/undesirable side effect needs to be excluded from a quick C Debugger test,
there is currently no way to do that from this window.

## Runtime display: disassembly + originating C source line

### The per-statement source-comment-label mechanism

Building a full DWARF-style post-link debug-info pipeline (tracking address-to-source-line info
through relocation) was judged too large for what's needed here. Instead:

- `CCodeGenerator` now takes the compile's own file name and source lines (split once, in
  `CCompiler.Compile`, so a CRLF- and an LF-authored source both index identically), and
  `EmitStatement` calls a new `EmitSourceLineComment(CSourceLocation)` immediately before each
  top-level statement's own generated code (skipped for `CCompoundStmt`/`CEmptyStmt`, which have no
  code of their own to attach a comment to).
- `EmitSourceLineComment` emits a zero-cost, **never-exported** CVM assembly label
  (`EmitLabel`, not `.export`) named `__srcline_N` immediately before that statement's own code, and
  records `label -> trimmed source line text` in a new `CCodeGenerator.SourceCommentLabels` dictionary,
  threaded out through `CCompileResult.SourceCommentLabels`. Only emitted when the statement's own
  `CSourceLocation.FileName` matches the translation unit's own top-level file name -- never for a
  statement whose location is inside an `#include`d header.
- Because these labels are deliberately **not** unique across different compiled objects (every C
  compile starts again from `__srcline_0`), resolving one back to a final address after linking needs
  to know which linked object it came from -- see the next section.

### `CvmLinker` now includes every Local symbol, disambiguated by `DefiningObjectName`

**Pre-existing gap, found and fixed as part of this work**: `CvmLinker.Link` previously dropped every
Local (non-exported) symbol from the final `CvmImage.Symbols` table entirely -- only Global symbols and
primitive-table entries were included. This made the source-comment-label mechanism above impossible to
implement (a `__srcline_N` label is, by design, always Local), so `CvmLinker.Link`'s final symbol-table
construction now also walks every linked object's own Local-bound symbols and includes them, each
resolved to its final address via that same object's own `FinalAddressOf` (the identical resolution
already used for the relocation loop's own Local-symbol fast path, fixed 2026-09-10 for a different,
related bug -- see `claude/cvm-toolchain-design.md`'s "Linker fix" section).

Since several linked objects can each define a same-named Local symbol (every C compile emits
`__srcline_0`, `__srcline_1`, ...), `CvmImageSymbol` gained a new field to disambiguate:

```csharp
public string? DefiningObjectName { get; init; }   // which CvmLinkObjectInput.DisplayName defined this
                                                     // symbol; null for a Global/primitive-table entry
                                                     // that CvmLinker itself resolved
public required bool IsExported { get; init; }      // true for a Global symbol or primitive-table
                                                     // mnemonic; false for a Local symbol
```

**Breaking on-disk change to the `.gaimg` "SYMT" chunk** -- consistent with this project's own
established convention (make the breaking change, document it in place, don't version the format;
same treatment already given to `CvmRelocation.EmbeddedValue`'s own addition). **A `.gaimg` linked
before this change cannot be loaded by code built after it, and vice versa** -- re-link from source
rather than loading an old image.

`CDebuggerViewModel.CompileAndLoad()` resolves its own compile's source-comment labels back to final
addresses by filtering `image.Symbols` to `DefiningObjectName == "c-debugger"` (the fixed display name
this compile's own object is registered under with `CvmLinker.Link`), then looking up each
`SourceCommentLabels` entry's label in that filtered set. This can never collide with a same-named
label some linked library object also happens to define, since the filter is keyed on the *defining
object*, not the label text alone.

### Fixing a side effect: the CVM Debugger's own name annotation

Including every Local symbol in `CvmImage.Symbols` has a side effect that was caught and fixed before
this work was considered done: the pre-existing CVM Debugger's own memory-view name annotation
(`RefreshMemoryView`, built from `_loadedImageSymbols`) would otherwise start showing every
compiler-internal local label -- loop/branch labels, string-literal labels, "static"-local mangled
names, and now `__srcline_N` labels too -- for an *ordinary* `.gaimg` loaded via "Load linked image
(.gaimg)...", not just for the C Debugger's own compiles. That is noise this annotation was never
designed to show.

Fixed by the `IsExported` field above: `RefreshMemoryView`'s symbol-lookup is now filtered to
`symbol.IsExported` before building the address->name lookup, so only Global symbols and primitive-table
mnemonics ever appear in that annotation (now the dedicated "Label" column -- see the 2026-09-26
follow-up section below) -- exactly the pre-existing behavior, since before this round's change to
`CvmLinker`, Local symbols were never in `CvmImage.Symbols` at all. The C Debugger's own
`DefiningObjectName`-filtered lookup (above) is unaffected -- it doesn't go through `IsExported` at all,
since it specifically wants its own Local source-comment labels.

## The shared PC/breakpoint gutter column

Per Stefan: "there is a separate column after the address for the current program counter position and
in the same column, breakpoints can be set and cleared... use the same column for the old CVM debugger."

`CvmDebuggerViewModel`'s memory inspector used to be one plain-text `MemoryViewText` string
(`TextBox`). It is now a row collection:

```csharp
public ObservableCollection<CvmMemoryRowViewModel> MemoryRows { get; }
public string MemoryStatusText { get; private set; }   // the two edge-case error messages that used
                                                         // to be inlined into MemoryViewText
```

```csharp
public sealed class CvmMemoryRowViewModel
{
  public required int FlatAddress { get; init; }
  public required string AddressText { get; init; }
  public required string ValueText { get; init; }
  public required bool IsCurrentPc { get; init; }
  public required bool IsBreakpoint { get; init; }
  public required string LabelText { get; init; }
  public required string DisassemblyText { get; init; }
  public required string SourceCommentText { get; init; } // populated only when a C Debugger compile
                                                            // supplied source comments for this address
}
```

(`LabelText` was added in the 2026-09-26 follow-up below; originally this row also carried a single
combined `GutterText` string, replaced in that same follow-up -- see there for why.)

A public `ToggleBreakpointAtAddress(int flatAddress)` method is the gutter's own click target -- both
`CvmDebuggerWindow.xaml.cs` and `CDebuggerWindow.xaml.cs` wire an identical `MouseLeftButtonDown`
handler on the gutter cell (no parameterized command type exists in this codebase, so a plain
code-behind event handler reading the clicked row's own `DataContext` was used, the same convention
already established elsewhere in this IDE for a click-driven action with no other UI dependency).

Both `CvmDebuggerWindow.xaml` and `CDebuggerWindow.xaml` use the identical `GridView` column layout:
a narrow gutter column right after the address, then Address, Value, Label, Disassembly, and a "C
source" column -- the last one empty for the ordinary CVM Debugger's own hand-typed/loaded programs,
populated only when the C Debugger's own compile pipeline loaded what's showing.

`LoadImageFile(string path)` (loading a `.gaimg` from disk) is now a thin wrapper around a new public
method:

```csharp
public void LoadImage(CvmImage image, string description, IReadOnlyDictionary<int, string>? sourceComments = null)
```

which lets the C Debugger hand over an in-memory `CvmImage` from its own compile pipeline without a
`.gaimg` file round-trip, and carries the optional per-address source-comment overlay into the memory
view's "C source" column.

## Breakpoints independent of run state, and removing the old dialog (2026-09-26 follow-up)

Stefan's request, verbatim: "in the C and CVM Debugger i want to place brakepoints independent whether
a program is running or not. by clicking on a line in simulated SRAM, i toggle a breakpoint. breakpoint
are displayed with red circles in the first column. breakpoints are activated, when running a program.
also remove the breakpoints dialog above the source area from C and CVM debugger. it is no longer
needed."

Two things changed, both in `CvmDebuggerViewModel` (shared, as above, by the C Debugger's own wrapped
`Debugger` instance -- one fix covers both windows):

**1. The old "Breakpoints" `GroupBox`, removed.** Both `CvmDebuggerWindow.xaml` and `CDebuggerWindow.xaml`
had a `GroupBox` sitting directly above the source/Assembly editor: a typed "page:address" `TextBox`,
Add/Remove selected/Clear all buttons, and a plain `ListBox` of the armed addresses as strings
(`NewBreakpointText`, `SelectedBreakpoint`, `Breakpoints` (an `ObservableCollection<string>`),
`AddBreakpointCommand`/`RemoveBreakpointCommand`/`ClearBreakpointsCommand`). All of it is now gone, from
both the XAML and the view model -- the gutter click (below) is the only way to set or clear a
breakpoint. Removing the `GroupBox` row from each window's `Grid` also meant renumbering every
subsequent `Grid.Row` (and dropping the now-unused `RowDefinition`) in both `.xaml` files.

**2. Breakpoints are now independent of `_session`.** Before this change, a breakpoint only ever
existed inside `CvmDebugSession.Breakpoints` (a plain `HashSet<int>` owned by the live session object) --
`ToggleBreakpointAtAddress` was a silent no-op with no session (`_session is null`), which is exactly
the state the debugger is in before the very first Start and after Stop. So a breakpoint could
previously only be set while a chip was actually connected, and every one of them was lost the instant
Stop ran (`Stop()`/`StartAsync()` both used to call `Breakpoints.Clear()` on the now-removed
`ObservableCollection<string>`).

`CvmDebuggerViewModel` now keeps its own `private readonly HashSet<int> _breakpoints`, independent of
`_session`'s lifetime, as the single source of truth:

```csharp
public void ToggleBreakpointAtAddress(int flatAddress)
{
  if (_breakpoints.Contains(flatAddress))
  {
    _breakpoints.Remove(flatAddress);
    _session?.RemoveBreakpoint(flatAddress);
  }
  else
  {
    _breakpoints.Add(flatAddress);
    _session?.AddBreakpoint(flatAddress);
  }

  RefreshMemoryView();
}
```

This no longer checks `_session` at all before recording the toggle, so a gutter click works identically
before the first Start, while stopped, or while a session is live (including mid-`Continue`/`Run` --
`CvmDebugSession.AddBreakpoint`/`RemoveBreakpoint` are already documented as safe to call from the UI
thread while a background thread is servicing transactions). When a session *does* exist, the change is
mirrored onto it immediately, so a currently running chip picks up a newly-armed address right away --
this is what "breakpoints are activated when running" means in practice: the address is armed the
instant it's clicked, but it only ever actually pauses anything once the chip's own memory-interface
traffic reaches it during a Step/Continue/Run.

`RefreshMemoryView` reads breakpoints for the gutter's `IsBreakpoint` red-circle glyph from
`_breakpoints` unconditionally, instead of branching on `_session.Breakpoints` vs. an empty set:

```csharp
HashSet<int> breakpoints = _breakpoints;
```

**Design choice, flagged for Stefan's confirmation**: `_breakpoints` deliberately survives both `Stop()`
and a fresh `StartAsync()` (neither clears it any more) -- `StartAsync` instead re-arms every persisted
address against the freshly booted session right after it's created:

```csharp
foreach (int flatAddress in _breakpoints)
{
  _session.AddBreakpoint(flatAddress);
}
```

This was read as the more natural interpretation of "independent whether a program is running or not" --
breakpoints behave like an ordinary IDE's (set once, they stay set across Stop/Start cycles) rather than
needing to be re-clicked every time Start runs, which would have been the effect of clearing them. If
Stefan actually wants them cleared on Stop or on a fresh Start, that is a one-line change (clear
`_breakpoints` at the top of `Stop()`/`StartAsync()` instead of leaving it alone) -- flagged rather than
silently assumed.

## Two-color gutter glyph, and a "Label" column (2026-09-26 follow-up)

Stefan's request, verbatim: "the first column (which is empty now) displays a breakpoint with a red
circle and the current program position to the right with a green arrow. add a column 'Label' between
'Value' and 'Disassembly' and put in there the name of a label for that address."

### The gutter: two colored glyphs, not one string

The single combined `GutterText` string (`"●→"` / `"→"` / `"●"` / `""`, one `Foreground` for the whole
cell) could not show the breakpoint circle and the PC arrow in two different colors, so it's gone,
along with its `FormatGutter` helper. `CvmMemoryRowViewModel` now exposes only the two booleans it
always had, `IsCurrentPc`/`IsBreakpoint`, and each window's own gutter `DataTemplate` renders them as
two separate `TextBlock`s inside one `StackPanel`:

```xml
<StackPanel Orientation="Horizontal" HorizontalAlignment="Center"
                Background="Transparent" Cursor="Hand"
                ToolTip="..." MouseLeftButtonDown="OnMemoryGutterClick">
  <TextBlock Text="●" FontWeight="Bold" Foreground="#B00020"
                  Visibility="{Binding IsBreakpoint, Converter={StaticResource BoolToVis}}" />
  <TextBlock Text="→" FontWeight="Bold" Foreground="#1B8A3B"
                  Visibility="{Binding IsCurrentPc, Converter={StaticResource BoolToVis}}" />
</StackPanel>
```

The red circle sits first (left) and the green arrow second (right), matching "the current program
position to the right with a green arrow" -- when both are true they show side by side rather than
overlapping. Each `TextBlock`'s own `Visibility` is bound through a `BooleanToVisibilityConverter`
(`x:Key="BoolToVis"`, added to each window's own `Window.Resources` -- the same converter/key already
used elsewhere in this IDE, e.g. `CProjectWindow.xaml`), so an inactive glyph collapses rather than
leaving blank space. The whole `StackPanel`, not either individual glyph, carries the click handler and
the cell's hit-testable background, so the entire cell stays clickable exactly as it was before,
regardless of which glyph(s) happen to be showing.

### The "Label" column: two label sources, merged

"The name of a label for that address" can come from either of two, normally mutually-exclusive
sources depending on how the currently-loaded program got there, and the new "Label" column shows
whichever one is actually populated:

1. **A loaded image's own exported symbol** (`_loadedImageSymbols`, from `LoadImage`/`LoadImageFile` --
   e.g. a Program project's linked `.gaimg`, or the C Debugger's own compile). This is the exact same
   `IsExported`-filtered lookup `RefreshMemoryView` already built for the pre-existing name annotation
   (see "Fixing a side effect" above) -- previously rendered as a bracketed `"<name>"` note folded into
   `DisassemblyText`; now it populates `LabelText` on its own instead.
2. **A hand-typed Assembly Code editor label** (new: `_assembledLabelAddresses`, a
   `Dictionary<string,int>` name -> address). CVM assembly already supports labels ("Labels
   (2026-09-02, per Stefan)" in `CvmAssemblyLanguage.Assemble`, e.g. `"loop: nop"`, resolved internally
   by `CollectLabelAddresses` to resolve label OPERANDS) but that name/address map was never returned to
   any caller -- it was computed and thrown away once assembly finished. To surface it:
   - `CvmAssemblyLanguage.Assemble`'s return type gained a `Labels` member:
     `(List<int>? Words, IReadOnlyDictionary<string,int>? Labels, string? Error)` -- simply the
     already-computed `labelAddresses` handed back on success (null on any failure, exactly like
     `Words`). Every early-return inside `Assemble` needed updating to the new 3-tuple shape.
   - `CvmAssemblyLanguage.AssembleAndLoadProgram` (the shared static helper both a live session and the
     standalone path use) passes this straight through unchanged.
   - `CvmDebugSession` gained a new `IReadOnlyDictionary<string, int> ProgramLabels` property, set by
     its own `AssembleAndLoadProgram(string)` on success (left alone on failure, and reset to empty by
     `LoadImage`, since a linked image's symbols are a completely different mechanism).
   - `CvmDebuggerViewModel._assembledLabelAddresses` is populated from `_session.ProgramLabels` (a live
     session) or from a new `Labels` output on `AssembleStandalone()`'s own return tuple (no session
     yet) every time `Assemble()` succeeds, and cleared by `LoadImage` -- mirroring exactly how
     `_loadedImageSymbols`/`_loadedImage` are themselves cleared by a successful `Assemble()`, since the
     two label sources describe mutually exclusive SRAM contents.
   - One incidental call-site fix: `CvmMemoryProtocol.TryBuildDebuggerTestProgram` used to `return
     CvmAssemblyLanguage.Assemble(instructions, compiledRam)` directly (its own return shape matched
     `Assemble`'s old 2-tuple); it now discards the `Labels` output, since that install-time assemble is
     immediately superseded by `StartAsync`'s own re-application of the Assembly Code editor's text
     moments later, which IS where labels get captured for display.

`RefreshMemoryView` builds an `ILookup<int,string>` from each source (inverting `_assembledLabelAddresses`'s
name -> address shape) and simply concatenates both lookups' results for a given address into
`LabelText` (comma-joined, for the rare case two labels coincide on one address, e.g. two consecutive
bare `"name:"` lines before the same instruction). In practice at most one source is ever non-empty at
a time, but nothing enforces that, so both are always consulted rather than picking one.

The red-circle rendering itself needed no further change beyond the two-`TextBlock` gutter above --
`"●"`/`Foreground="#B00020"` for breakpoint already matched "displays a breakpoint with a red circle";
only the PC arrow's own color (previously the same red as the breakpoint circle, since both shared one
`Foreground`) needed to become green (`"#1B8A3B"`) to match "the current program position... with a
green arrow".

## Launching the C Debugger

A new "C Debugger" button sits next to the existing "CVM Debugger" button in `ChipWindow.xaml`, wired
to `OnOpenCDebuggerClick` in `ChipWindow.xaml.cs` -- mirroring `OnOpenCvmDebuggerClick`'s own
"reuse-if-already-open, else create" window-management pattern exactly (a `CDebuggerViewModel`'s own
`Debugger` holds a real serial port open for its whole life, same as a plain `CvmDebuggerViewModel`, so
closing the window must actually tear that session down, not just cancel a pending Kraken request).

`ChipViewModel` gained a new, optional trailing constructor parameter and property,
`CLibsDirectoryPath`, threaded from `MainWindowViewModel.CLibsDirectoryPath` through the one
`new ChipViewModel(...)` call site in `MainWindow.xaml.cs`, purely so `ChipWindow`'s own "C Debugger"
button handler can hand it to a new `CDebuggerViewModel`.

## What's deliberately out of scope (flagged, not silently decided)

- **The C Debugger's source text is not persisted across sessions**, unlike the CVM Debugger's own
  named-Programs mechanism (`Programs`/`SelectedProgram`/Save/Restore). This is a deliberate scope
  trim for a "quick scratch test" tool, not an oversight -- flagged in case Stefan wants it saved
  alongside the chip's other project data the way CVM Debugger programs already are.
- **No per-library opt-out** for the C Debugger's "all libraries, always" default (see above).
- **A missing library's `.galib` is a warning, not a hard failure** (see above) -- flagged in case
  Stefan would prefer this to stop the compile outright instead.
- **`tjmp` is still not used for C's own `switch`/`case`** (unrelated pre-existing gap, unaffected by
  this work) -- `switch` still compiles to an equality-comparison chain, per this project's own
  standing "never guess an unconfirmed hardware calling convention" discipline.
- **Whether breakpoints should survive Stop/Start** (see the follow-up section above) -- implemented as
  "yes, they persist", flagged as a judgment call rather than something Stefan specified explicitly.
</content>