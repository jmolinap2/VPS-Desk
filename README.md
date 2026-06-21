# VPS Desk

VPS Desk is a lightweight Windows desktop control panel for monitoring and
operating a Linux VPS through SSH. It is built with PowerShell 7 and WPF, so it
can give a VPS a practical graphical interface without requiring a full remote
desktop environment on the server.

## Features

- Desktop dashboard for VPS reachability, CPU, memory, disk and uptime.
- SSH-based server checks using key auth or password auth.
- Docker service visibility for common `sql`, `api` and `front` containers.
- Storage view with disk usage, Docker images and top Docker directories.
- Remote log viewer for Docker containers and host journals.
- Deploy and migration launcher for existing PowerShell automation scripts.
- Environment switcher for Development, Staging and Production.
- Production guardrails for risky deploy options.
- Local log masking for passwords, tokens, secrets and long encoded values.

## Requirements

- Windows 10 or Windows 11.
- PowerShell 7 or newer, available as `pwsh`.
- OpenSSH client available in `PATH`.
- SSH access to the target VPS.
- Docker on the VPS for Docker-related status, storage and log features.
- Optional: a Git token only when your deploy script needs private repository
  access.

## Quick Start

1. Clone or download this repository.
2. Copy `.env.example` to `.env`.
3. Edit `.env` with your VPS host, SSH user and local paths.
4. Run `Run.bat`.

You can also start it directly from PowerShell:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\VpsDesk.ps1
```

## Configuration

VPS Desk reads local configuration from a `.env` file in the project folder.
Use `.env.example` as the public template.

Important variables:

- `SERVER_HOST`: VPS IP address or DNS name.
- `SERVER_USER`: SSH user.
- `SSH_PORT`: SSH port, usually `22`.
- `SSH_KEY_PATH`: path to your private SSH key.
- `REPO_LOCAL`: local path to the project that contains your deploy scripts.
- `REMOTE_REPO_PATH`: remote path to your app on the VPS.
- `COMPOSE_FILE`: Docker Compose file name on the VPS.
- `GIT_TOKEN`: optional token for private repositories.
- `SSH_PASSWORD`: optional password for password-based SSH.

Never commit `.env`, real tokens, private keys, passwords or generated logs.

## Usage Notes

- The Dashboard page can check host reachability and collect metrics through
  SSH.
- The Storage page can inspect Docker disk usage and run Docker prune commands.
  Review destructive prune actions before confirming them.
- The Log Center can load logs from Docker containers or `journalctl`.
- The Operations page expects deploy or migration scripts to exist under the
  `scripts` folder of `REPO_LOCAL`.

## Project Structure

```text
.
|-- Assets/                 # App assets
|-- Schemas/                # WPF XAML layout
|-- Scripts/
|   |-- Core/               # State, env loading, health checks, log security
|   `-- GUI/                # Window, navigation and UI handlers
|-- Run.bat                 # Windows launcher
|-- VpsDesk.ps1             # Main PowerShell entry point
|-- .env.example            # Public configuration template
|-- LICENSE                 # MIT license
`-- README.md
```

## Security

This tool can execute commands against a real server. Use a least-privilege SSH
user whenever possible, keep private keys protected, and test actions in
Development or Staging before Production.

If a token or password was ever committed, pasted into an issue, or shared in a
public zip, rotate it immediately.

## Contributing

Contributions are welcome. Please keep changes small, avoid committing
machine-specific configuration, and document any behavior that can modify a
remote VPS.

## License

MIT. See `LICENSE`.
