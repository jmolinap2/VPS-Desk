using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VpsDesk.Desktop.ViewModels;

public partial class FilesViewModel
{
    private static readonly Regex EnvironmentKeyPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ObservableCollection<EnvironmentVariableRow> _environmentVariables = new();
    private readonly List<EnvironmentLineTemplate> _environmentTemplate = new();
    private bool _environmentWatcherAttached;
    private bool _isSynchronizingEnvironment;
    private bool _isEnvironmentManagerOpen;
    private bool _revealEnvironmentSecrets;
    private string _newEnvironmentKey = string.Empty;
    private string _newEnvironmentValue = string.Empty;
    private string _environmentManagerMessage = string.Empty;
    private bool _environmentManagerValid = true;
    private string _environmentNewLine = Environment.NewLine;
    private bool _environmentEndsWithNewLine;

    public ObservableCollection<EnvironmentVariableRow> EnvironmentVariables => _environmentVariables;

    public bool IsEnvironmentFile => string.Equals(EditorFileType, "ENV", StringComparison.OrdinalIgnoreCase);

    public bool IsEnvironmentManagerOpen
    {
        get => _isEnvironmentManagerOpen;
        private set
        {
            if (!SetProperty(ref _isEnvironmentManagerOpen, value)) return;
            OnPropertyChanged(nameof(IsRawEditorVisible));
        }
    }

    public bool IsRawEditorVisible => !IsEnvironmentManagerOpen;

    public bool RevealEnvironmentSecrets
    {
        get => _revealEnvironmentSecrets;
        set
        {
            if (!SetProperty(ref _revealEnvironmentSecrets, value)) return;
            foreach (var item in EnvironmentVariables)
            {
                item.RevealValue = value;
            }
        }
    }

    public string NewEnvironmentKey
    {
        get => _newEnvironmentKey;
        set => SetProperty(ref _newEnvironmentKey, value);
    }

    public string NewEnvironmentValue
    {
        get => _newEnvironmentValue;
        set => SetProperty(ref _newEnvironmentValue, value);
    }

    public string EnvironmentManagerMessage
    {
        get => _environmentManagerMessage;
        private set => SetProperty(ref _environmentManagerMessage, value);
    }

    public bool EnvironmentManagerValid
    {
        get => _environmentManagerValid;
        private set => SetProperty(ref _environmentManagerValid, value);
    }

    partial void OnEditorFileTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsEnvironmentFile));
        EnsureEnvironmentWatcher();

        if (string.Equals(value, "ENV", StringComparison.OrdinalIgnoreCase))
        {
            IsEnvironmentManagerOpen = true;
            LoadEnvironmentVariablesFromEditor();
            return;
        }

        ResetEnvironmentManager();
    }

    [RelayCommand]
    private void OpenEnvironmentManager()
    {
        if (!IsEnvironmentFile) return;
        IsEnvironmentManagerOpen = true;
        LoadEnvironmentVariablesFromEditor();
        StatusMessage = $"Administrando variables de {Path.GetFileName(EditorPath)}.";
    }

    [RelayCommand]
    private void CloseEnvironmentManager()
    {
        if (!IsEnvironmentFile) return;
        IsEnvironmentManagerOpen = false;
        ValidateEnvironmentText(EditorContent);
        StatusMessage = $"Vista de archivo activa para {Path.GetFileName(EditorPath)}.";
    }

    [RelayCommand]
    private void AddEnvironmentVariable()
    {
        if (!IsEnvironmentFile || IsEditorReadOnly)
        {
            StatusMessage = "Activa el modo de edición antes de modificar variables de entorno.";
            return;
        }

        var key = NewEnvironmentKey.Trim();
        if (!EnvironmentKeyPattern.IsMatch(key))
        {
            EnvironmentManagerValid = false;
            EnvironmentManagerMessage = "El nombre debe empezar con una letra o _ y usar solo letras, números o _.";
            return;
        }

        if (EnvironmentVariables.Any(item => item.Key.Equals(key, StringComparison.Ordinal)))
        {
            EnvironmentManagerValid = false;
            EnvironmentManagerMessage = $"La variable '{key}' ya existe.";
            return;
        }

        var item = new EnvironmentVariableRow(key, NewEnvironmentValue, isNew: true)
        {
            RevealValue = RevealEnvironmentSecrets,
            IsReadOnly = IsEditorReadOnly
        };
        SubscribeEnvironmentRow(item);
        EnvironmentVariables.Add(item);
        NewEnvironmentKey = string.Empty;
        NewEnvironmentValue = string.Empty;
        SynchronizeEnvironmentEditor();
        StatusMessage = $"Variable {key} agregada localmente. Guarda el archivo para aplicarla al VPS.";
    }

    [RelayCommand]
    private void RemoveEnvironmentVariable(EnvironmentVariableRow? item)
    {
        if (item == null) return;
        if (IsEditorReadOnly)
        {
            StatusMessage = "Activa el modo de edición antes de eliminar variables de entorno.";
            return;
        }

        item.IsDeleted = true;
        item.PropertyChanged -= HandleEnvironmentRowChanged;
        EnvironmentVariables.Remove(item);
        SynchronizeEnvironmentEditor();
        StatusMessage = $"Variable {item.Key} marcada para eliminación. Guarda el archivo para aplicar el cambio.";
    }

    [RelayCommand]
    private void ValidateEnvironmentVariables()
    {
        ValidateEnvironmentRows();
        StatusMessage = EnvironmentManagerValid
            ? $"{EnvironmentVariables.Count} variable(s) válidas en {Path.GetFileName(EditorPath)}."
            : EnvironmentManagerMessage;
    }

    private void EnsureEnvironmentWatcher()
    {
        if (_environmentWatcherAttached) return;
        PropertyChanged += HandleEnvironmentEditorPropertyChanged;
        _environmentWatcherAttached = true;
    }

    private void HandleEnvironmentEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsEditorReadOnly))
        {
            foreach (var item in EnvironmentVariables)
            {
                item.IsReadOnly = IsEditorReadOnly;
            }
            return;
        }

        if (e.PropertyName != nameof(EditorContent) || !IsEnvironmentFile || _isSynchronizingEnvironment)
        {
            return;
        }

        if (IsEnvironmentManagerOpen)
        {
            LoadEnvironmentVariablesFromEditor();
        }
        else
        {
            ValidateEnvironmentText(EditorContent);
        }
    }

    private void LoadEnvironmentVariablesFromEditor()
    {
        if (!IsEnvironmentFile) return;

        foreach (var item in EnvironmentVariables)
        {
            item.PropertyChanged -= HandleEnvironmentRowChanged;
        }

        EnvironmentVariables.Clear();
        _environmentTemplate.Clear();
        _environmentNewLine = EditorContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _environmentEndsWithNewLine = EditorContent.EndsWith("\n", StringComparison.Ordinal);

        var normalized = EditorContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var count = _environmentEndsWithNewLine && lines.Length > 0 ? lines.Length - 1 : lines.Length;

        for (var index = 0; index < count; index++)
        {
            var raw = lines[index];
            if (!TryParseEnvironmentLine(raw, out var prefix, out var key, out var value))
            {
                _environmentTemplate.Add(EnvironmentLineTemplate.Raw(raw));
                continue;
            }

            var item = new EnvironmentVariableRow(key, value, isNew: false)
            {
                RevealValue = RevealEnvironmentSecrets,
                IsReadOnly = IsEditorReadOnly
            };
            SubscribeEnvironmentRow(item);
            EnvironmentVariables.Add(item);
            _environmentTemplate.Add(EnvironmentLineTemplate.ForVariable(raw, prefix, item));
        }

        ValidateEnvironmentRows();
    }

    private void SubscribeEnvironmentRow(EnvironmentVariableRow item)
        => item.PropertyChanged += HandleEnvironmentRowChanged;

    private void HandleEnvironmentRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSynchronizingEnvironment || sender is not EnvironmentVariableRow item) return;
        if (e.PropertyName is not nameof(EnvironmentVariableRow.Key) and not nameof(EnvironmentVariableRow.Value)) return;

        item.RefreshSensitivity();
        SynchronizeEnvironmentEditor();
    }

    private void SynchronizeEnvironmentEditor()
    {
        if (!IsEnvironmentFile) return;

        _isSynchronizingEnvironment = true;
        try
        {
            EditorContent = BuildEnvironmentContent();
        }
        finally
        {
            _isSynchronizingEnvironment = false;
        }

        ValidateEnvironmentRows();
    }

    private string BuildEnvironmentContent()
    {
        var lines = new List<string>();
        var templated = new HashSet<EnvironmentVariableRow>();

        foreach (var template in _environmentTemplate)
        {
            if (template.Variable is null)
            {
                lines.Add(template.RawText);
                continue;
            }

            var item = template.Variable;
            templated.Add(item);
            if (item.IsDeleted) continue;

            if (item.Key == item.OriginalKey && item.Value == item.OriginalValue)
            {
                lines.Add(template.RawText);
            }
            else
            {
                lines.Add($"{template.Prefix}{item.Key}={item.Value}");
            }
        }

        foreach (var item in EnvironmentVariables.Where(item => !item.IsDeleted && !templated.Contains(item)))
        {
            lines.Add($"{item.Key}={item.Value}");
        }

        var content = string.Join(_environmentNewLine, lines);
        if (_environmentEndsWithNewLine && lines.Count > 0)
        {
            content += _environmentNewLine;
        }

        return content;
    }

    private void ValidateEnvironmentRows()
    {
        var invalid = EnvironmentVariables
            .FirstOrDefault(item => !EnvironmentKeyPattern.IsMatch(item.Key));
        if (invalid != null)
        {
            SetEnvironmentValidation(false, $"Nombre de variable inválido: '{invalid.Key}'.");
            return;
        }

        var duplicate = EnvironmentVariables
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            SetEnvironmentValidation(false, $"Variable duplicada: '{duplicate.Key}'.");
            return;
        }

        var multiline = EnvironmentVariables
            .FirstOrDefault(item => item.Value.Contains('\n') || item.Value.Contains('\r'));
        if (multiline != null)
        {
            SetEnvironmentValidation(false, $"'{multiline.Key}' contiene un salto de línea no soportado por el editor estructurado.");
            return;
        }

        SetEnvironmentValidation(true, $"ENV válido · {EnvironmentVariables.Count} variable(s) · comentarios y líneas desconocidas se conservarán.");
    }

    private void ValidateEnvironmentText(string content)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lineNumber = 0;

        foreach (var raw in normalized.Split('\n'))
        {
            lineNumber++;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            if (!TryParseEnvironmentLine(raw, out _, out var key, out _))
            {
                SetEnvironmentValidation(false, $"ENV inválido en línea {lineNumber}: se esperaba NOMBRE=valor.");
                return;
            }

            if (!EnvironmentKeyPattern.IsMatch(key))
            {
                SetEnvironmentValidation(false, $"ENV inválido en línea {lineNumber}: nombre '{key}' no válido.");
                return;
            }

            if (!keys.Add(key))
            {
                SetEnvironmentValidation(false, $"ENV inválido: la variable '{key}' está duplicada.");
                return;
            }
        }

        SetEnvironmentValidation(true, $"ENV válido · {keys.Count} variable(s).");
    }

    private void SetEnvironmentValidation(bool valid, string message)
    {
        EnvironmentManagerValid = valid;
        EnvironmentManagerMessage = message;
        IsEditorValid = valid;
        EditorValidationMessage = message;
    }

    private static bool TryParseEnvironmentLine(
        string raw,
        out string prefix,
        out string key,
        out string value)
    {
        prefix = string.Empty;
        key = string.Empty;
        value = string.Empty;

        var leadingLength = raw.Length - raw.TrimStart().Length;
        var leading = leadingLength > 0 ? raw[..leadingLength] : string.Empty;
        var body = raw[leadingLength..];

        var exportPrefix = string.Empty;
        if (body.StartsWith("export ", StringComparison.Ordinal))
        {
            exportPrefix = "export ";
            body = body[7..];
        }

        var separator = body.IndexOf('=');
        if (separator <= 0) return false;

        key = body[..separator].Trim();
        if (key.Length == 0) return false;

        value = body[(separator + 1)..];
        prefix = leading + exportPrefix;
        return true;
    }

    private void ResetEnvironmentManager()
    {
        foreach (var item in EnvironmentVariables)
        {
            item.PropertyChanged -= HandleEnvironmentRowChanged;
        }

        EnvironmentVariables.Clear();
        _environmentTemplate.Clear();
        IsEnvironmentManagerOpen = false;
        RevealEnvironmentSecrets = false;
        NewEnvironmentKey = string.Empty;
        NewEnvironmentValue = string.Empty;
        EnvironmentManagerMessage = string.Empty;
        EnvironmentManagerValid = true;
    }

    private sealed record EnvironmentLineTemplate(
        string RawText,
        string Prefix,
        EnvironmentVariableRow? Variable)
    {
        public static EnvironmentLineTemplate Raw(string rawText)
            => new(rawText, string.Empty, null);

        public static EnvironmentLineTemplate ForVariable(
            string rawText,
            string prefix,
            EnvironmentVariableRow variable)
            => new(rawText, prefix, variable);
    }
}

public sealed class EnvironmentVariableRow : ObservableObject
{
    private string _key;
    private string _value;
    private bool _revealValue;
    private bool _isReadOnly;

    public EnvironmentVariableRow(string key, string value, bool isNew)
    {
        _key = key;
        _value = value;
        OriginalKey = key;
        OriginalValue = value;
        IsNew = isNew;
    }

    public string Key
    {
        get => _key;
        set
        {
            if (!SetProperty(ref _key, value)) return;
            OnPropertyChanged(nameof(IsSecret));
            OnPropertyChanged(nameof(IsValueMasked));
        }
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public bool RevealValue
    {
        get => _revealValue;
        set
        {
            if (!SetProperty(ref _revealValue, value)) return;
            OnPropertyChanged(nameof(IsValueMasked));
        }
    }

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set => SetProperty(ref _isReadOnly, value);
    }

    public bool IsSecret => IsSecretKey(Key);
    public bool IsValueMasked => IsSecret && !RevealValue;
    public bool IsDeleted { get; set; }
    public bool IsNew { get; }
    internal string OriginalKey { get; }
    internal string OriginalValue { get; }

    internal void RefreshSensitivity()
    {
        OnPropertyChanged(nameof(IsSecret));
        OnPropertyChanged(nameof(IsValueMasked));
    }

    private static bool IsSecretKey(string key)
    {
        var normalized = key.ToUpperInvariant();
        return normalized.Contains("PASSWORD", StringComparison.Ordinal)
               || normalized.Contains("PASSWD", StringComparison.Ordinal)
               || normalized.Contains("SECRET", StringComparison.Ordinal)
               || normalized.Contains("TOKEN", StringComparison.Ordinal)
               || normalized.Contains("API_KEY", StringComparison.Ordinal)
               || normalized.Contains("PRIVATE_KEY", StringComparison.Ordinal)
               || normalized.Contains("CONNECTIONSTRING", StringComparison.Ordinal)
               || normalized.Contains("CONNECTION_STRING", StringComparison.Ordinal)
               || normalized.EndsWith("_KEY", StringComparison.Ordinal);
    }
}
