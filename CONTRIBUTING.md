# Contribuir

Gracias por ayudar a mejorar VPS Desk.

## Desarrollo

- Usa .NET SDK 10.
- Mantén fuera del repositorio cualquier `.env` real; sube solo `.env.example`.
- No subas credenciales, llaves privadas, logs generados ni backups locales.
- Prefiere pull requests pequeños con una descripción clara del comportamiento cambiado.
- Documenta cualquier comando que pueda modificar un VPS remoto, recursos Docker o estado de deploy.

PowerShell 7 solo es necesario para ejecutar manualmente algunos scripts auxiliares de validación usados por CI.

## Checks manuales

Antes de abrir un pull request:

```bash
dotnet restore VpsDesk.slnx
dotnet build VpsDesk.slnx --configuration Release --no-restore
dotnet run --project src/VpsDesk.Desktop/VpsDesk.Desktop.csproj
```

Si el cambio toca localización, ejecuta además:

```powershell
./Scripts/Validate-Localization.ps1
```

La aplicación oficial es la implementación .NET/Avalonia de `src/`. El cliente PowerShell/WPF heredado fue retirado de `main` y permanece recuperable desde el historial de Git.

## Seguridad

Si tu cambio toca SSH, Docker prune, ejecución de deploys, manejo de tokens o salida de logs, menciónalo claramente en el pull request.
