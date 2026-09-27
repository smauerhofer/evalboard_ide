using System.ComponentModel;
using Ga144.Evb.Ide.Cvm;
using YamlDotNet.Serialization;

namespace Ga144.Evb.Ide.Models;

public sealed class Ga144ChipConfiguration
{
  public Ga144ChipRole Role { get; set; }
  public string Name { get; set; } = string.Empty;
  public List<Ga144NodeConfiguration> Nodes { get; set; } = [];

  /// <summary>
  /// SUPERSEDED, 2026-09-20, by <see cref="DebuggerPrograms"/> -- the CVM Debugger's Assembly Code
  /// editor now holds several NAMED programs instead of one anonymous scratch buffer, per Stefan's own
  /// request. Kept only so an older project's single saved program survives loading: <see cref="Normalize"/>
  /// migrates a non-null value here into a "default"-named entry in <see cref="DebuggerPrograms"/> the
  /// first time this chip is normalized after upgrading, then clears this field so a fresh save never
  /// writes it again. Nothing in this codebase reads or writes this property any more once that
  /// migration has run once.
  /// </summary>
  public string? DebuggerAssemblyCode { get; set; }

  /// <summary>
  /// The CVM Debugger's own named programs -- <see cref="ViewModels.CvmDebuggerViewModel"/>'s program
  /// selector lets Stefan switch between several hand-written CVM asm scratch programs on this chip
  /// without one overwriting another, add new ones, and rename the selected one (on Save). Always has
  /// at least one entry after <see cref="Normalize"/> has run: a fresh chip (or one upgrading from the
  /// older single-<see cref="DebuggerAssemblyCode"/> shape) gets exactly one program, named "default"
  /// and always first in this list -- Stefan's own requirement, and also what lets a chip whose
  /// "default" happens to sort elsewhere in a hand-edited YAML file be silently put back in front.
  /// Nothing here forces "default" to keep existing forever, though: once Stefan renames it (via Save),
  /// it stays renamed -- <see cref="Normalize"/> only reorders an EXISTING "default" entry to the
  /// front, it never resurrects a fresh one once this list is non-empty.
  /// </summary>
  public List<Ga144DebuggerProgramConfiguration> DebuggerPrograms { get; set; } = [];

  /// <summary>
  /// The name of whichever <see cref="DebuggerPrograms"/> entry the CVM Debugger had selected the last
  /// time it was open on this chip. <see cref="ViewModels.CvmDebuggerViewModel"/> reselects the program
  /// with this name when it opens (falling back to <see cref="DebuggerPrograms"/>'s own first entry if
  /// no program by this name exists any more -- renamed, never saved, or this is the chip's very first
  /// debugger session), so reopening the window picks up exactly where Stefan left off instead of
  /// always resetting to "default". Null until a program has ever been selected. Updated immediately
  /// whenever the selected program changes -- unlike <see cref="DebuggerPrograms"/>'s own content, this
  /// is UI state, not program content, so it is not gated behind Save.
  /// </summary>
  public string? LastSelectedDebuggerProgramName { get; set; }

  /// <summary>
  /// The C Debugger's own named programs (added 2026-09-27, per Stefan: "the C debugger must support
  /// multiple programs like the CVM debugger") -- mirrors <see cref="DebuggerPrograms"/>'s own "always at
  /// least one, 'default' always first" contract exactly, via its own <see cref="Ga144CDebuggerProgramConfiguration"/>
  /// type and its own <see cref="EnsureCDebuggerPrograms"/> pass in <see cref="Normalize"/>, but as a
  /// SEPARATE list from <see cref="DebuggerPrograms"/>: a C source snippet and a CVM assembly program are
  /// different content entirely, even though both live on this same chip and both debuggers are opened
  /// from the same chip window.
  ///
  /// UNLIKE <see cref="DebuggerPrograms"/>'s own "default" entry, this class's "default" has NO special
  /// "always shows a fixed built-in text regardless of what's saved" override anywhere in
  /// <see cref="ViewModels.CDebuggerViewModel"/> -- that CVM-side behavior exists to guarantee one always-
  /// known-good, always-assemblable test program for hardware-confidence testing (see
  /// <see cref="ViewModels.CvmDebuggerViewModel.LoadEditorFromProgram"/>'s own remarks); nothing here calls
  /// for that same guarantee, so this "default" is just an ordinary, normally-editable, normally-persisted
  /// program that merely happens to be the first one a chip starts with, seeded once from
  /// <see cref="CDebuggerDefaultProgram.Source"/>. Flagged here rather than silently copied, since this is
  /// the one deliberate deviation from an otherwise exact mirror of <see cref="DebuggerPrograms"/>'s own
  /// design.
  /// </summary>
  public List<Ga144CDebuggerProgramConfiguration> CDebuggerPrograms { get; set; } = [];

  /// <summary>
  /// The name of whichever <see cref="CDebuggerPrograms"/> entry the C Debugger had selected the last
  /// time it was open on this chip -- mirrors <see cref="LastSelectedDebuggerProgramName"/> exactly, as
  /// its own separate field for the same reason <see cref="CDebuggerPrograms"/> is its own separate list.
  /// </summary>
  public string? LastSelectedCDebuggerProgramName { get; set; }

  /// <summary>
  /// The Kraken structure (head 708 + three fixed tentacles) is a constant of the
  /// GA144 array and the boot protocol, not per-chip configuration. It is never
  /// persisted: it is always the one fixed topology, recreated in memory. Whether
  /// a Kraken is actually running is transient runtime state owned by the live
  /// controller (HardwareErected), not anything stored here. Any legacy 'kraken:'
  /// block in old YAML is ignored on load.
  /// </summary>
  [YamlIgnore]
  public KrakenConfiguration Kraken { get; } = KrakenConfiguration.CreateFixed();

  public static Ga144ChipConfiguration Create(Ga144ChipRole role)
  {
    var chip = new Ga144ChipConfiguration
    {
      Role = role,
      Name = role == Ga144ChipRole.Host ? "Host GA144" : "Target GA144"
    };

    chip.EnsureAllNodes();
    return chip;
  }

  public void Normalize()
  {
    Name = string.IsNullOrWhiteSpace(Name)
    ? Role == Ga144ChipRole.Host ? "Host GA144" : "Target GA144"
    : Name.Trim();
    Nodes ??= [];
    EnsureAllNodes();
    Nodes.Sort((left, right) => left.Coordinate.CompareTo(right.Coordinate));
    EnsureDebuggerPrograms();
    EnsureCDebuggerPrograms();
  }

  /// <summary>
  /// Ensures <see cref="DebuggerPrograms"/> always has at least one entry, with a "default"-named one
  /// (if any exists) always first -- see that property's own remarks. Also performs the one-time
  /// migration of the older, now-retired <see cref="DebuggerAssemblyCode"/> single-program field, the
  /// first time this method runs against a chip that still has one.
  /// </summary>
  private void EnsureDebuggerPrograms()
  {
    DebuggerPrograms ??= [];
    foreach (Ga144DebuggerProgramConfiguration program in DebuggerPrograms)
    {
      program.Normalize();
    }

    if (DebuggerPrograms.Count == 0)
    {
      // Fresh chip, or migrating an older save's single DebuggerAssemblyCode scratch program into the
      // new list -- either way, the very first program a chip ever has is named "default", seeded from
      // whatever scratch code was already saved, or the built-in CVM Debugger test program for a chip
      // that never had any.
      string seedSource = string.IsNullOrEmpty(DebuggerAssemblyCode)
          ? CvmDebuggerDefaultProgram.Source
          : DebuggerAssemblyCode;
      DebuggerPrograms.Add(new Ga144DebuggerProgramConfiguration { Name = "default", Source = seedSource });
    }
    else
    {
      // A "default"-named program (however it got there) always sorts first -- but this only REORDERS
      // an existing one; a chip whose "default" was deliberately renamed away by Stefan stays exactly
      // as he left it, and this never resurrects a fresh "default" entry once the list is non-empty.
      int defaultIndex = DebuggerPrograms.FindIndex(program => string.Equals(program.Name, "default", StringComparison.OrdinalIgnoreCase));
      if (defaultIndex > 0)
      {
        Ga144DebuggerProgramConfiguration defaultProgram = DebuggerPrograms[defaultIndex];
        DebuggerPrograms.RemoveAt(defaultIndex);
        DebuggerPrograms.Insert(0, defaultProgram);
      }
    }

    // Migrated into DebuggerPrograms above (the first time only, when it mattered) -- stop carrying
    // the legacy field forward so a fresh save doesn't keep writing dead data into the project YAML.
    DebuggerAssemblyCode = null;
  }

  /// <summary>
  /// Ensures <see cref="CDebuggerPrograms"/> always has at least one entry, with a "default"-named one
  /// (if any exists) always first -- mirrors <see cref="EnsureDebuggerPrograms"/> exactly, except there is
  /// no legacy single-scratch-buffer field to migrate here (the C Debugger never had one before this
  /// feature existed -- see <see cref="CDebuggerPrograms"/>'s own remarks).
  /// </summary>
  private void EnsureCDebuggerPrograms()
  {
    CDebuggerPrograms ??= [];
    foreach (Ga144CDebuggerProgramConfiguration program in CDebuggerPrograms)
    {
      program.Normalize();
    }

    if (CDebuggerPrograms.Count == 0)
    {
      CDebuggerPrograms.Add(new Ga144CDebuggerProgramConfiguration { Name = "default", Source = CDebuggerDefaultProgram.Source });
    }
    else
    {
      int defaultIndex = CDebuggerPrograms.FindIndex(program => string.Equals(program.Name, "default", StringComparison.OrdinalIgnoreCase));
      if (defaultIndex > 0)
      {
        Ga144CDebuggerProgramConfiguration defaultProgram = CDebuggerPrograms[defaultIndex];
        CDebuggerPrograms.RemoveAt(defaultIndex);
        CDebuggerPrograms.Insert(0, defaultProgram);
      }
    }
  }

  public Ga144NodeConfiguration GetNode(int coordinate)
  {
    Normalize();
    return Nodes.First(node => node.Coordinate == coordinate);
  }

  private void EnsureAllNodes()
  {
    var existing = Nodes.ToDictionary(node => node.Coordinate);
    for (int row = 0; row < 8; row++)
    {
      for (int column = 0; column < 18; column++)
      {
        int coordinate = row * 100 + column;
        if (!existing.ContainsKey(coordinate))
        {
          Nodes.Add(Ga144NodeConfiguration.Create(coordinate));
        }
      }
    }
  }
}

/// <summary>
/// One named CVM Debugger program stored on a chip -- see <see cref="Ga144ChipConfiguration.DebuggerPrograms"/>'s
/// own remarks for the "always at least one, 'default' always first" contract this is part of.
///
/// Implements <see cref="INotifyPropertyChanged"/> for <see cref="Name"/> alone (BUG FIX, 2026-09-20:
/// Stefan reported that renaming a program via Save did not visibly update the CVM Debugger's own
/// program-selector dropdown) -- <see cref="ViewModels.CvmDebuggerViewModel"/> keeps these SAME
/// instances inside its own <c>Programs</c> <c>ObservableCollection</c> rather than replacing them, so
/// the WPF ComboBox bound to it (<c>DisplayMemberPath="Name"</c>) generates its dropdown text once per
/// item and never re-reads it again unless that ONE item itself raises change notification -- an
/// <c>ObservableCollection</c> only ever announces additions/removals/replacements of ENTRIES, never a
/// plain field mutation on an entry already inside it. <see cref="Source"/> needs no such notification:
/// nothing in this app binds it for live display inside a list the way <see cref="Name"/> is.
/// </summary>
public sealed class Ga144DebuggerProgramConfiguration : INotifyPropertyChanged
{
  private string _name = string.Empty;

  public string Name
  {
    get => _name;
    set
    {
      if (_name == value)
      {
        return;
      }

      _name = value;
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }
  }

  public string Source { get; set; } = string.Empty;

  public event PropertyChangedEventHandler? PropertyChanged;

  public void Normalize()
  {
    Name ??= string.Empty;
    Source ??= string.Empty;
  }
}

/// <summary>
/// One named C Debugger program stored on a chip -- see <see cref="Ga144ChipConfiguration.CDebuggerPrograms"/>'s
/// own remarks for the "always at least one, 'default' always first" contract this is part of, and for the
/// one deliberate deviation from <see cref="Ga144DebuggerProgramConfiguration"/> (no special-cased "default").
///
/// Implements <see cref="INotifyPropertyChanged"/> for <see cref="Name"/> alone, for the exact same reason
/// <see cref="Ga144DebuggerProgramConfiguration"/> does -- <see cref="ViewModels.CDebuggerViewModel"/> keeps
/// these SAME instances inside its own <c>Programs</c> <c>ObservableCollection</c> rather than replacing
/// them, so the WPF ComboBox bound to it (<c>DisplayMemberPath="Name"</c>) only picks up a rename via this
/// notification. <see cref="Source"/> needs no such notification: nothing in this app binds it for live
/// display inside a list the way <see cref="Name"/> is.
/// </summary>
public sealed class Ga144CDebuggerProgramConfiguration : INotifyPropertyChanged
{
  private string _name = string.Empty;

  public string Name
  {
    get => _name;
    set
    {
      if (_name == value)
      {
        return;
      }

      _name = value;
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }
  }

  public string Source { get; set; } = string.Empty;

  public event PropertyChangedEventHandler? PropertyChanged;

  public void Normalize()
  {
    Name ??= string.Empty;
    Source ??= string.Empty;
  }
}