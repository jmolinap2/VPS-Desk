using System.ComponentModel;
using VpsDesk.Application.Abstractions;

namespace VpsDesk.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private TerminalViewModel? _terminalModule;

    public TerminalViewModel TerminalModule
        => _terminalModule ?? throw new InvalidOperationException("Terminal module has not been initialized.");

    public void InitializeTerminal(IInteractiveTerminalService terminalService)
    {
        if (_terminalModule != null) return;

        _terminalModule = new TerminalViewModel(
            terminalService,
            () => _server,
            () => _activeSecret);

        PropertyChanged += HandleTerminalNavigation;
        OnPropertyChanged(nameof(TerminalModule));
    }

    private void HandleTerminalNavigation(object? sender, PropertyChangedEventArgs e)
    {
        if (_terminalModule == null) return;

        if (e.PropertyName == nameof(SelectedPage) && IsTerminalPage)
        {
            _ = _terminalModule.ConnectIfNeededAsync();
        }
        else if (e.PropertyName == nameof(SelectedServerName))
        {
            _ = _terminalModule.ResetForServerChangeAsync();
        }
    }

    public async Task DisposeTerminalAsync()
    {
        if (_terminalModule != null)
        {
            await _terminalModule.DisposeAsync();
        }
    }
}
