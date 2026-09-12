# Contribuir

Gracias por ayudar a mejorar VPS Desk.

## Desarrollo

- Usa PowerShell 7 o superior.
- Mantén la configuración en `.env`; sube solo `.env.example`.
- No subas credenciales, llaves privadas, logs generados ni backups locales.
- Prefiere pull requests pequeños con una descripción clara del comportamiento cambiado.
- Documenta cualquier comando que pueda modificar un VPS remoto, recursos Docker o estado de deploy.

## Checks Manuales

Antes de abrir un pull request, inicia la app localmente con:

```powershell
.\Run.bat
```

Para revisar sintaxis de scripts, parsea el punto de entrada desde PowerShell:

```powershell
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile('VpsDesk.ps1', [ref]$tokens, [ref]$errors) | Out-Null
$errors
```

## Seguridad

Si tu cambio toca SSH, Docker prune, ejecución de deploys, manejo de tokens o salida de logs, menciónalo claramente en el pull request.
