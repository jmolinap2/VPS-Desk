[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$InnoSetupCompiler = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $repoRoot "src\VpsDesk.Desktop\VpsDesk.Desktop.csproj"
$sourceEnv = Join-Path $repoRoot ".env"
$stagingRoot = Join-Path $repoRoot "obj\installer-staging"
$cleanPayload = Join-Path $stagingRoot "clean"
$personalPayload = Join-Path $stagingRoot "personal"
$deliveryRoot = Join-Path $repoRoot "dist\installers"
$installerScript = Join-Path $repoRoot "Packaging\VPS-Desk-Setup.iss"

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $InnoSetupCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw "No se encontró ISCC.exe. Instala Inno Setup 6 o pasa -InnoSetupCompiler con su ruta."
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $cleanPayload, $personalPayload, $deliveryRoot -Force | Out-Null

dotnet publish $desktopProject -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $cleanPayload
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación para los instaladores." }

# The standard installer is intentionally free of local state and credentials.
foreach ($forbidden in @(".env", "secrets", "data", "portable.mode")) {
    $forbiddenPath = Join-Path $cleanPayload $forbidden
    if (Test-Path -LiteralPath $forbiddenPath) {
        Remove-Item -LiteralPath $forbiddenPath -Recurse -Force
    }
}

Copy-Item -Path (Join-Path $cleanPayload "*") -Destination $personalPayload -Recurse -Force
if (-not (Test-Path -LiteralPath $sourceEnv -PathType Leaf)) {
    throw "No existe el archivo .env requerido para el instalador personal: $sourceEnv"
}

$envLines = Get-Content -LiteralPath $sourceEnv
$keyLine = $envLines | Where-Object { $_ -match '^\s*SSH_KEY_PATH\s*=' } | Select-Object -First 1
if ($keyLine -and $keyLine -match '^\s*SSH_KEY_PATH\s*=\s*(.*)$') {
    $keyPath = $matches[1].Trim().Trim('"').Trim("'")
    if (-not [IO.Path]::IsPathRooted($keyPath)) {
        $keyPath = [IO.Path]::GetFullPath($keyPath, $repoRoot)
    }
    if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
        throw "La llave indicada por SSH_KEY_PATH no existe."
    }

    $secretDirectory = Join-Path $personalPayload "secrets"
    New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
    Copy-Item -LiteralPath $keyPath -Destination (Join-Path $secretDirectory "id_vpsdesk") -Force
    if (Test-Path -LiteralPath ($keyPath + ".pub") -PathType Leaf) {
        Copy-Item -LiteralPath ($keyPath + ".pub") -Destination (Join-Path $secretDirectory "id_vpsdesk.pub") -Force
    }
    $envLines = $envLines | ForEach-Object {
        if ($_ -match '^\s*SSH_KEY_PATH\s*=') { 'SSH_KEY_PATH=secrets/id_vpsdesk' } else { $_ }
    }
}
Set-Content -LiteralPath (Join-Path $personalPayload ".env") -Value $envLines -Encoding utf8NoBOM

function Build-Installer([string]$payload, [string]$fileName) {
    $env:VPSDESK_INSTALLER_PAYLOAD = $payload
    $env:VPSDESK_INSTALLER_OUTPUT = $deliveryRoot
    $env:VPSDESK_INSTALLER_FILE_NAME = $fileName
    & $InnoSetupCompiler $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Falló la creación de $fileName.exe." }
}

Build-Installer $cleanPayload "VPS-Desk-Setup"
Build-Installer $personalPayload "VPS-Desk-Personal-Setup"

Write-Host "Instaladores creados en: $deliveryRoot"
