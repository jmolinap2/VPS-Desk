# Contributing

Thanks for helping improve VPS Desk.

## Development

- Use PowerShell 7 or newer.
- Keep configuration in `.env`; commit only `.env.example`.
- Do not commit credentials, private keys, generated logs or local backups.
- Prefer small pull requests with a clear description of the behavior changed.
- Document any command that can modify a remote VPS, Docker resources or deploy
  state.

## Manual Checks

Before opening a pull request, start the app locally with:

```powershell
.\Run.bat
```

For script syntax checks, parse the entry point from PowerShell:

```powershell
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile('VpsDesk.ps1', [ref]$tokens, [ref]$errors) | Out-Null
$errors
```

## Security

If your change touches SSH, Docker prune, deploy execution, token handling or
log output, call that out clearly in the pull request.

