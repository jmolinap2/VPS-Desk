using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VpsDesk.Desktop.Views;

public partial class BackupPasswordDialog : Window
{
    private readonly bool _requiresConfirmation;

    public BackupPasswordDialog()
        : this("Copia cifrada", string.Empty, requiresConfirmation: false)
    {
    }

    public BackupPasswordDialog(string title, string description, bool requiresConfirmation)
    {
        InitializeComponent();
        _requiresConfirmation = requiresConfirmation;
        Title = title;
        TitleText.Text = title;
        DescriptionText.Text = description;
        ConfirmationPanel.IsVisible = requiresConfirmation;
        ConfirmButton.Content = requiresConfirmation ? "Crear copia" : "Restaurar";
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Text ?? string.Empty;
        if (password.Length < 12)
        {
            ShowError("Usa una contraseña de al menos 12 caracteres.");
            return;
        }
        if (_requiresConfirmation && !string.Equals(password, ConfirmationInput.Text, StringComparison.Ordinal))
        {
            ShowError("Las contraseñas no coinciden.");
            return;
        }

        Close(password);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
