using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace VpsDesk.Installer;

internal static class Program
{
    private const string ProductName = "VPS Desk";
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        ProductName);

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        try
        {
            if (args.Any(argument => argument.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall();
                return;
            }

            Install();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"No se pudo completar la operación.\n\n{exception.Message}",
                ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Install()
    {
        var answer = MessageBox.Show(
            $"Se instalará {ProductName} para el usuario actual en:\n\n{InstallDirectory}\n\n" +
            "El paquete incluye la configuración y los secretos suministrados al generarlo.",
            $"Instalar {ProductName}",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (answer != DialogResult.OK) return;

        var assembly = typeof(Program).Assembly;
        using var payload = assembly.GetManifestResourceStream("VpsDesk.Payload.zip")
                            ?? throw new InvalidOperationException("El instalador no contiene la carga de la aplicación.");

        Directory.CreateDirectory(InstallDirectory);
        ZipFile.ExtractToDirectory(payload, InstallDirectory, overwriteFiles: true);

        var executablePath = Path.Combine(InstallDirectory, "VpsDesk.Desktop.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("No se encontró el ejecutable después de instalar.", executablePath);
        }

        var installedSetup = Path.Combine(InstallDirectory, "VPS-Desk-Setup.exe");
        var currentSetup = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentSetup)
            && !Path.GetFullPath(currentSetup).Equals(Path.GetFullPath(installedSetup), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(currentSetup, installedSetup, overwrite: true);
        }

        CreateInternetShortcut(StartMenuShortcutPath(), executablePath);
        CreateInternetShortcut(DesktopShortcutPath(), executablePath);
        RegisterUninstaller(installedSetup);

        var launch = MessageBox.Show(
            $"{ProductName} quedó instalado correctamente.\n\n¿Deseas abrirlo ahora?",
            ProductName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (launch == DialogResult.Yes)
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
        }
    }

    private static void Uninstall()
    {
        var answer = MessageBox.Show(
            $"¿Deseas desinstalar {ProductName}?\n\nLos datos guardados en AppData no se eliminarán.",
            $"Desinstalar {ProductName}",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        DeleteIfExists(StartMenuShortcutPath());
        DeleteIfExists(DesktopShortcutPath());
        Registry.CurrentUser.DeleteSubKeyTree(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VPSDesk",
            throwOnMissingSubKey: false);

        var escapedDirectory = InstallDirectory.Replace("'", "''", StringComparison.Ordinal);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"Start-Sleep -Seconds 2; Remove-Item -LiteralPath '{escapedDirectory}' -Recurse -Force\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        MessageBox.Show(
            $"{ProductName} fue desinstalado. Los datos del usuario se conservaron.",
            ProductName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void RegisterUninstaller(string setupPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VPSDesk");
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("Publisher", "VPS Desk");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", Path.Combine(InstallDirectory, "VpsDesk.Desktop.exe"));
        key.SetValue("UninstallString", $"\"{setupPath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static string StartMenuShortcutPath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            ProductName);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{ProductName}.url");
    }

    private static string DesktopShortcutPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.url");

    private static void CreateInternetShortcut(string shortcutPath, string executablePath)
    {
        var executableUri = new Uri(executablePath).AbsoluteUri;
        File.WriteAllLines(shortcutPath,
        [
            "[InternetShortcut]",
            $"URL={executableUri}",
            $"IconFile={executablePath}",
            "IconIndex=0"
        ]);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
