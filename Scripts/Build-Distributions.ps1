[CmdletBinding()]
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$deliveryRoot = Join-Path $repoRoot "app\clinical-care\appointments"
$stagingRoot = Join-Path $repoRoot "obj\distribution-staging"
$portableRoot = Join-Path $deliveryRoot "portable"
$installerRoot = Join-Path $deliveryRoot "installer"
$installedPayload = Join-Path $stagingRoot "installed"
$payloadZip = Join-Path $stagingRoot "VpsDesk-payload.zip"
$desktopProject = Join-Path $repoRoot "src\VpsDesk.Desktop\VpsDesk.Desktop.csproj"
$installerProject = Join-Path $repoRoot "src\VpsDesk.Installer\VpsDesk.Installer.csproj"
$sourceEnv = Join-Path $repoRoot ".env"

if (-not (Test-Path -LiteralPath $sourceEnv -PathType Leaf)) {
    throw "No existe el archivo .env requerido: $sourceEnv"
}

foreach ($target in @($deliveryRoot, $stagingRoot)) {
    $resolvedParent = [IO.Path]::GetFullPath((Split-Path -Parent $target))
    if (-not $resolvedParent.StartsWith([IO.Path]::GetFullPath($repoRoot), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta de salida no segura: $target"
    }
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $portableRoot, $installerRoot, $installedPayload -Force | Out-Null

dotnet publish $desktopProject -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $portableRoot
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación portable." }

dotnet publish $desktopProject -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $installedPayload
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación instalable." }

function Copy-ConfiguredSecrets([string]$destination) {
    $envLines = Get-Content -LiteralPath $sourceEnv
    $keyLine = $envLines | Where-Object { $_ -match '^\s*SSH_KEY_PATH\s*=' } | Select-Object -First 1
    $keyPath = $null
    if ($keyLine -and $keyLine -match '^\s*SSH_KEY_PATH\s*=\s*(.*)$') {
        $keyPath = $matches[1].Trim().Trim('"').Trim("'")
    }

    if (-not [string]::IsNullOrWhiteSpace($keyPath)) {
        if (-not [IO.Path]::IsPathRooted($keyPath)) {
            $keyPath = [IO.Path]::GetFullPath($keyPath, $repoRoot)
        }
        if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
            throw "La llave indicada por SSH_KEY_PATH no existe."
        }

        $secretDirectory = Join-Path $destination "secrets"
        New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
        Copy-Item -LiteralPath $keyPath -Destination (Join-Path $secretDirectory "id_vpsdesk") -Force
        if (Test-Path -LiteralPath ($keyPath + ".pub") -PathType Leaf) {
            Copy-Item -LiteralPath ($keyPath + ".pub") -Destination (Join-Path $secretDirectory "id_vpsdesk.pub") -Force
        }
        $envLines = $envLines | ForEach-Object {
            if ($_ -match '^\s*SSH_KEY_PATH\s*=') { 'SSH_KEY_PATH=secrets/id_vpsdesk' } else { $_ }
        }
    }

    Set-Content -LiteralPath (Join-Path $destination ".env") -Value $envLines -Encoding utf8NoBOM
}

Copy-ConfiguredSecrets $portableRoot
Copy-ConfiguredSecrets $installedPayload
Set-Content -LiteralPath (Join-Path $portableRoot "portable.mode") -Value "VPS Desk portable data mode" -Encoding ascii

$portableData = Join-Path $portableRoot "data"
New-Item -ItemType Directory -Path $portableData -Force | Out-Null
$roamingData = Join-Path $env:APPDATA "VPS Desk"
$localDatabase = Join-Path $env:LOCALAPPDATA "VPSDesk\vpsdesk.db"
if (Test-Path -LiteralPath $roamingData -PathType Container) {
    Copy-Item -Path (Join-Path $roamingData "*") -Destination $portableData -Recurse -Force
}
if (Test-Path -LiteralPath $localDatabase -PathType Leaf) {
    Copy-Item -LiteralPath $localDatabase -Destination (Join-Path $portableData "vpsdesk.db") -Force
}

Compress-Archive -Path (Join-Path $portableRoot "*") `
    -DestinationPath (Join-Path $deliveryRoot "VPS-Desk-portable-win-x64.zip") -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $installedPayload "*") -DestinationPath $payloadZip -CompressionLevel Optimal

dotnet publish $installerProject -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PayloadZip=$payloadZip -o $installerRoot
if ($LASTEXITCODE -ne 0) { throw "Falló la creación del instalador." }

Get-ChildItem -LiteralPath $installerRoot -File |
    Where-Object Name -ne "VPS-Desk-Setup.exe" |
    Remove-Item -Force

$readme = @'
VPS DESK - ENTREGA PARA USB

PORTABLE
1. Abre la carpeta portable.
2. Ejecuta VpsDesk.Desktop.exe.
3. Conserva juntos .env, secrets, data y portable.mode.

También puedes descomprimir VPS-Desk-portable-win-x64.zip en cualquier PC Windows x64.

INSTALABLE
1. Abre la carpeta installer.
2. Ejecuta VPS-Desk-Setup.exe.
3. Confirma la instalación para el usuario actual.

SEGURIDAD
Esta entrega contiene secretos reales en .env y secrets/id_vpsdesk. Trátala como una
credencial: cifra la USB, no compartas la carpeta y elimina copias que no necesites.
'@
Set-Content -LiteralPath (Join-Path $deliveryRoot "LEEME.txt") -Value $readme -Encoding utf8NoBOM

python (Join-Path $repoRoot "Scripts\Generate-Manual.py") `
    (Join-Path $deliveryRoot "MANUAL-DE-USUARIO.pdf")
if ($LASTEXITCODE -ne 0) { throw "Falló la creación del manual PDF." }

Write-Host "Distribuciones creadas en: $deliveryRoot"
