<#
.SYNOPSIS
  Publica VPS Desk para Windows con una compilación limpia y, opcionalmente, la instala.

.DESCRIPTION
  Evita el fallo "Attempted to access a missing method": una publicación incremental
  puede dejar ensamblados de versiones distintas dentro del mismo exe. Este script
  recompila todo sin reutilizar resultados anteriores (--no-incremental), publica en
  <repo>\VPS-Desk-win-x64 (ignorada por git) y, con -Install, copia el exe a la carpeta
  de instalación conservando el anterior como VpsDesk.Desktop.exe.previous.

  Nunca modifica el .env de la carpeta instalada: solo avisa si su huella SSH difiere
  de la del .env del repositorio.

.EXAMPLE
  .\Scripts\Publish-Desktop.ps1 -Install
#>
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$InstallDir = "C:\VPS-Desk-win-x64",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\VpsDesk.Desktop\VpsDesk.Desktop.csproj"
$publishDir = Join-Path $repoRoot "VPS-Desk-win-x64"
$exeName = "VpsDesk.Desktop.exe"

if ($Install) {
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and (Split-Path $_.Path) -ieq $InstallDir }
    if ($running) {
        throw "VPS Desk está abierto desde $InstallDir (PID $($running.Id -join ', ')). Ciérralo y vuelve a ejecutar."
    }
}

Write-Host "==> Compilación limpia ($Runtime)"
dotnet build $project -c Release -r $Runtime --self-contained true --no-incremental --nologo
if ($LASTEXITCODE -ne 0) { throw "Falló la compilación." }

Write-Host "==> Publicación"
dotnet publish $project -c Release -r $Runtime --self-contained true --no-build `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación." }

$published = Join-Path $publishDir $exeName
if (-not (Test-Path -LiteralPath $published -PathType Leaf)) { throw "No se generó $published." }
$sizeMb = [math]::Round((Get-Item -LiteralPath $published).Length / 1MB, 1)
Write-Host "==> Publicado: $published ($sizeMb MB)"

if (-not $Install) {
    Write-Host "Usa -Install para copiarlo a $InstallDir."
    return
}

if (-not (Test-Path -LiteralPath $InstallDir -PathType Container)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

$target = Join-Path $InstallDir $exeName
if (Test-Path -LiteralPath $target -PathType Leaf) {
    Copy-Item -LiteralPath $target -Destination ($target + ".previous") -Force
}
Copy-Item -LiteralPath $published -Destination $target -Force
Write-Host "==> Instalado en $target (la versión anterior quedó como $exeName.previous)"

function Get-Fingerprint([string]$envFile) {
    if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) { return $null }
    $line = Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^\s*SSH_HOST_FINGERPRINT\s*=' } | Select-Object -First 1
    if (-not $line) { return $null }
    return (($line -split '=', 2)[1]).Trim().Trim('"') -replace '^SHA256:', ''
}

$repoFingerprint = Get-Fingerprint (Join-Path $repoRoot ".env")
$installedFingerprint = Get-Fingerprint (Join-Path $InstallDir ".env")
if ($repoFingerprint -and $installedFingerprint -and $repoFingerprint -ne $installedFingerprint) {
    Write-Warning "La huella SSH del .env instalado difiere de la del repositorio. Si el servidor cambió, actualiza SSH_HOST_FINGERPRINT en $InstallDir\.env."
}
