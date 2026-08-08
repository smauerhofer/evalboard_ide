using Ga144.Evb.Ide.Models;
using System.Collections.ObjectModel;

namespace Ga144.Evb.Ide.ViewModels;

public sealed class MacroDefinitionViewModel : ObservableObject
{
  private string _name;
  private string _description;
  private string _sourceCode;

  public MacroDefinitionViewModel(F18MacroDefinition model)
  {
    Model = model;
    Model.Normalize();
    _name = Model.Name;
    _description = Model.Description;
    _sourceCode = Model.SourceCode;
  }

  public F18MacroDefinition Model { get; }

  public string Name
  {
    get => _name;
    set => SetProperty(ref _name, value ?? string.Empty);
  }

  public string Description
  {
    get => _description;
    set => SetProperty(ref _description, value ?? string.Empty);
  }

  public string SourceCode
  {
    get => _sourceCode;
    set => SetProperty(ref _sourceCode, value ?? string.Empty);
  }

  public F18MacroDefinition CreateModel() => new()
  {
    Name = Name.Trim(),
    Description = Description,
    SourceCode = SourceCode
  };
}

public sealed class MacroEditorViewModel : ObservableObject
{
  private readonly Ga144Project _project;
  private readonly Ga144RomLibrary _romLibrary;
  private MacroDefinitionViewModel? _selectedSystemMacro;
  private MacroDefinitionViewModel? _selectedUserMacro;
  private string _validationMessage = string.Empty;

  public MacroEditorViewModel(Ga144Project project, Ga144RomLibrary romLibrary, string romLibraryPath)
  {
    _project = project ?? throw new ArgumentNullException(nameof(project));
    _romLibrary = romLibrary ?? throw new ArgumentNullException(nameof(romLibrary));
    _project.Normalize();
    _romLibrary.Normalize();
    RomLibraryPath = romLibraryPath;

    foreach (F18MacroDefinition macro in _romLibrary.SystemMacros)
    {
      SystemMacros.Add(new MacroDefinitionViewModel(macro.Clone()));
    }

    foreach (F18MacroDefinition macro in _project.UserMacros)
    {
      UserMacros.Add(new MacroDefinitionViewModel(macro.Clone()));
    }

    AddSystemMacroCommand = new RelayCommand(AddSystemMacro);
    RemoveSystemMacroCommand = new RelayCommand(RemoveSystemMacro, () => SelectedSystemMacro is not null);
    AddUserMacroCommand = new RelayCommand(AddUserMacro);
    RemoveUserMacroCommand = new RelayCommand(RemoveUserMacro, () => SelectedUserMacro is not null);

    SelectedSystemMacro = SystemMacros.FirstOrDefault();
    SelectedUserMacro = UserMacros.FirstOrDefault();
  }

  public ObservableCollection<MacroDefinitionViewModel> SystemMacros { get; } = [];
  public ObservableCollection<MacroDefinitionViewModel> UserMacros { get; } = [];
  public RelayCommand AddSystemMacroCommand { get; }
  public RelayCommand RemoveSystemMacroCommand { get; }
  public RelayCommand AddUserMacroCommand { get; }
  public RelayCommand RemoveUserMacroCommand { get; }
  public string RomLibraryPath { get; }
  public string ProjectName => _project.Name;
  public bool SystemChanged { get; private set; }
  public bool UserChanged { get; private set; }

  public MacroDefinitionViewModel? SelectedSystemMacro
  {
    get => _selectedSystemMacro;
    set
    {
      if (SetProperty(ref _selectedSystemMacro, value))
      {
        RemoveSystemMacroCommand.NotifyCanExecuteChanged();
      }
    }
  }

  public MacroDefinitionViewModel? SelectedUserMacro
  {
    get => _selectedUserMacro;
    set
    {
      if (SetProperty(ref _selectedUserMacro, value))
      {
        RemoveUserMacroCommand.NotifyCanExecuteChanged();
      }
    }
  }

  public string ValidationMessage
  {
    get => _validationMessage;
    private set => SetProperty(ref _validationMessage, value);
  }

  public bool TryApply()
  {
    ValidationMessage = string.Empty;
    if (!ValidateCollection(SystemMacros, "system", out string? error) ||
        !ValidateCollection(UserMacros, "project user", out error))
    {
      ValidationMessage = error ?? "Invalid macro definition.";
      return false;
    }

    List<F18MacroDefinition> newSystem = SystemMacros.Select(item => item.CreateModel()).ToList();
    List<F18MacroDefinition> newUser = UserMacros.Select(item => item.CreateModel()).ToList();

    SystemChanged = !AreEqual(_romLibrary.SystemMacros, newSystem);
    UserChanged = !AreEqual(_project.UserMacros, newUser);

    if (SystemChanged)
    {
      _romLibrary.SystemMacros = newSystem;
      _romLibrary.Normalize();
    }

    if (UserChanged)
    {
      _project.UserMacros = newUser;
      _project.Normalize();
    }

    return true;
  }

  private void AddSystemMacro()
  {
    var macro = new MacroDefinitionViewModel(new F18MacroDefinition
    {
      Name = CreateUniqueName("system-macro", SystemMacros)
    });
    SystemMacros.Add(macro);
    SelectedSystemMacro = macro;
  }

  private void RemoveSystemMacro()
  {
    RemoveSelected(SystemMacros, SelectedSystemMacro, selected => SelectedSystemMacro = selected);
  }

  private void AddUserMacro()
  {
    var macro = new MacroDefinitionViewModel(new F18MacroDefinition
    {
      Name = CreateUniqueName("user-macro", UserMacros)
    });
    UserMacros.Add(macro);
    SelectedUserMacro = macro;
  }

  private void RemoveUserMacro()
  {
    RemoveSelected(UserMacros, SelectedUserMacro, selected => SelectedUserMacro = selected);
  }

  private static void RemoveSelected(
      ObservableCollection<MacroDefinitionViewModel> collection,
      MacroDefinitionViewModel? selected,
      Action<MacroDefinitionViewModel?> setSelected)
  {
    if (selected is null)
    {
      return;
    }

    int index = collection.IndexOf(selected);
    collection.Remove(selected);
    setSelected(collection.Count == 0 ? null : collection[Math.Clamp(index, 0, collection.Count - 1)]);
  }

  private static bool ValidateCollection(
      IEnumerable<MacroDefinitionViewModel> macros,
      string scopeName,
      out string? error)
  {
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (MacroDefinitionViewModel macro in macros)
    {
      string name = macro.Name.Trim();
      if (!F18MacroDefinition.IsValidName(name))
      {
        error = $"'{macro.Name}' is not a valid {scopeName} macro name. Use one FORTH token containing at least one non-decimal character.";
        return false;
      }

      if (name.Equals("import", StringComparison.OrdinalIgnoreCase))
      {
        error = $"The {scopeName} macro name 'import' is reserved.";
        return false;
      }

      if (!names.Add(name))
      {
        error = $"The {scopeName} macro name '{name}' is defined more than once.";
        return false;
      }
    }

    error = null;
    return true;
  }

  private static string CreateUniqueName(
      string stem,
      IEnumerable<MacroDefinitionViewModel> macros)
  {
    var names = macros.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (!names.Contains(stem))
    {
      return stem;
    }

    for (int suffix = 2; ; suffix++)
    {
      string candidate = $"{stem}-{suffix}";
      if (!names.Contains(candidate))
      {
        return candidate;
      }
    }
  }

  private static bool AreEqual(
      IReadOnlyList<F18MacroDefinition> left,
      IReadOnlyList<F18MacroDefinition> right)
  {
    if (left.Count != right.Count)
    {
      return false;
    }

    for (int index = 0; index < left.Count; index++)
    {
      if (!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal) ||
          !string.Equals(left[index].Description, right[index].Description, StringComparison.Ordinal) ||
          !string.Equals(left[index].SourceCode, right[index].SourceCode, StringComparison.Ordinal))
      {
        return false;
      }
    }

    return true;
  }
}
