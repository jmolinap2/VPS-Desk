using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VpsDesk.Application.Abstractions;
using VpsDesk.Domain.Servers;

namespace VpsDesk.Desktop.ViewModels;

public partial class TerminalViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly Regex AnsiCsiRegex = new("\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
    private static readonly Regex AnsiOscRegex = new("\\x1B\\][^\\x07]*(?:\\x07|\\x1B\\\\)", RegexOptions.Compiled);

    private readonly IInteractiveTerminalService _terminalService;
    private readonly Func<ServerProfile?> _serverAccessor;
    private readonly Func<string?> _secretAccessor;
    private IInteractiveTerminalSession? _session;
    private readonly List<string> _history = [];
    private int _historyIndex;
    private string? _pendingMultilineCommand;

    [ObservableProperty] private string _terminalOutput = string.Empty;
    [ObservableProperty] private string _commandText = string.Empty;
    [ObservableProperty] private string _statusMessage = "Terminal desconectado.";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasPendingMultilinePaste;
    [ObservableProperty] private string _pendingMultilineMessage = string.Empty;
    [ObservableProperty] private int _fontSize = 14;
    [ObservableProperty] private int _scrollbackLines = 5000;
    [ObservableProperty] private bool _confirmMultilinePaste = true;

    public bool IsProduction => _serverAccessor()?.Environment == ServerEnvironment.Production;
    public string ActiveServerLabel => _serverAccessor()?.Name ?? "Sin servidor activo";
    public bool CanSend => IsConnected && !IsBusy && !string.IsNullOrWhiteSpace(CommandText);

    public TerminalViewModel(
        IInteractiveTerminalService terminalService,
        Func<ServerProfile?> serverAccessor,
        Func<string?> secretAccessor)
    {
        _terminalService = terminalService;
        _serverAccessor = serverAccessor;
        _secretAccessor = secretAccessor;
    }

    partial void OnCommandTextChanged(string value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsConnectedChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    public void ApplyPreferences(int fontSize, int scrollbackLines, bool confirmMultilinePaste)
    {
        FontSize = Math.Clamp(fontSize, 11, 22);
        ScrollbackLines = Math.Clamp(scrollbackLines, 1000, 20000);
        ConfirmMultilinePaste = confirmMultilinePaste;
        TrimScrollback();
    }

    public async Task ConnectIfNeededAsync()
    {
        if (!IsConnected && !IsBusy)
        {
            await ConnectAsync();
        }
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (IsBusy || IsConnected) return;
        var server = _serverAccessor();
        if (server == null)
        {
            StatusMessage = "Selecciona un servidor activo antes de abrir la terminal.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Conectando a {server.Username}@{server.Host}:{server.Port}...";
        OnPropertyChanged(nameof(IsProduction));
        OnPropertyChanged(nameof(ActiveServerLabel));

        try
        {
            await DisconnectCoreAsync(clearStatus: false);
            _session = await _terminalService.ConnectAsync(
                server,
                _secretAccessor(),
                new TerminalSessionOptions());
            _session.OutputReceived += HandleOutputReceived;
            IsConnected = true;
            StatusMessage = $"Conectado a {server.Name}. La sesión no se registra en el historial de operaciones.";
            AppendOutput($"\r\n[SSH conectado a {server.Username}@{server.Host}]\r\n");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"No se pudo abrir la terminal SSH: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await DisconnectCoreAsync(clearStatus: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        if (_session == null || !IsConnected) return;
        var command = CommandText;
        if (string.IsNullOrWhiteSpace(command)) return;

        var normalized = command.Replace("\r\n", "\n").Replace('\r', '\n');
        if (ConfirmMultilinePaste && normalized.Contains('\n'))
        {
            _pendingMultilineCommand = normalized;
            HasPendingMultilinePaste = true;
            var lineCount = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            PendingMultilineMessage = $"Vas a enviar {lineCount} líneas al servidor. Revisa el contenido antes de continuar.";
            return;
        }

        await SendCoreAsync(normalized);
    }

    [RelayCommand]
    private async Task ConfirmMultilinePasteAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingMultilineCommand)) return;
        var command = _pendingMultilineCommand;
        CancelMultilinePaste();
        await SendCoreAsync(command);
    }

    [RelayCommand]
    private void CancelMultilinePaste()
    {
        _pendingMultilineCommand = null;
        HasPendingMultilinePaste = false;
        PendingMultilineMessage = string.Empty;
    }

    [RelayCommand]
    public async Task SendInterruptAsync()
    {
        if (_session == null || !IsConnected) return;
        try
        {
            await _session.WriteAsync("\u0003");
            StatusMessage = "Ctrl+C enviado a la sesión remota.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo enviar Ctrl+C: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalOutput = string.Empty;
        StatusMessage = IsConnected ? "Salida local limpiada; la sesión SSH sigue conectada." : "Salida local limpiada.";
    }

    public void NavigateHistory(int direction)
    {
        if (_history.Count == 0) return;
        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        CommandText = _historyIndex >= _history.Count ? string.Empty : _history[_historyIndex];
    }

    public async Task ResetForServerChangeAsync()
    {
        await DisconnectCoreAsync(clearStatus: false);
        TerminalOutput = string.Empty;
        CommandText = string.Empty;
        _history.Clear();
        _historyIndex = 0;
        StatusMessage = "Servidor activo cambiado. Abre la terminal para conectar la nueva sesión.";
        OnPropertyChanged(nameof(IsProduction));
        OnPropertyChanged(nameof(ActiveServerLabel));
    }

    private async Task SendCoreAsync(string command)
    {
        if (_session == null || !IsConnected) return;

        try
        {
            var historyValue = command.Trim();
            if (historyValue.Length > 0 && (_history.Count == 0 || _history[^1] != historyValue))
            {
                _history.Add(historyValue);
            }
            _historyIndex = _history.Count;

            await _session.WriteAsync(command.EndsWith('\n') ? command : command + "\n");
            CommandText = string.Empty;
            StatusMessage = "Comando enviado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al enviar a la terminal: {ex.Message}";
            IsConnected = _session.IsConnected;
        }
    }

    private void HandleOutputReceived(object? sender, TerminalOutputEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AppendOutput(e.Text);
            if (_session is { IsConnected: false })
            {
                IsConnected = false;
                StatusMessage = "La sesión SSH terminó.";
            }
        });
    }

    private void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clean = AnsiOscRegex.Replace(AnsiCsiRegex.Replace(text, string.Empty), string.Empty);
        TerminalOutput += clean;
        TrimScrollback();
    }

    private void TrimScrollback()
    {
        if (string.IsNullOrEmpty(TerminalOutput)) return;
        var lines = TerminalOutput.Split('\n');
        if (lines.Length <= ScrollbackLines) return;
        TerminalOutput = string.Join('\n', lines[^ScrollbackLines..]);
    }

    private async Task DisconnectCoreAsync(bool clearStatus)
    {
        if (_session != null)
        {
            _session.OutputReceived -= HandleOutputReceived;
            await _session.DisposeAsync();
            _session = null;
        }
        IsConnected = false;
        if (clearStatus) StatusMessage = "Terminal desconectado.";
    }

    public async ValueTask DisposeAsync() => await DisconnectCoreAsync(clearStatus: false);
}
