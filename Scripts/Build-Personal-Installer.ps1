[CmdletBinding()]
throw "Este script fue reemplazado. Ejecuta Scripts\\Build-Installers.ps1 para generar el instalador limpio y el personal juntos."
param(
    [string]$Runtime = "win-x64",
    [string]$InnoSetupCompiler = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $repoRoot "src\VpsDesk.Desktop\VpsDesk.Desktop.csproj"
$sourceEnv = Join-Path $repoRoot ".env"
$stagingRoot = Join-Path $repoRoot "obj\personal-installer-staging"
$payloadRoot = Join-Path $stagingRoot "payload"
$deliveryRoot = Join-Path $repoRoot "app\clinical-care\appointments\installer"
$installerScript = Join-Path $repoRoot "Packaging\VPS-Desk-Personal.iss"

if (-not (Test-Path -LiteralPath $sourceEnv -PathType Leaf)) {
    throw "No existe el archivo .env requerido: $sourceEnv"
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $InnoSetupCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw "No se encontró ISCC.exe. Instala Inno Setup 6 o pasa -InnoSetupCompiler con su ruta."
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
New-Item -ItemType Directory -Path $deliveryRoot -Force | Out-Null

dotnet publish $desktopProject -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $payloadRoot
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación para el instalador personal." }

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

    $secretDirectory = Join-Path $payloadRoot "secrets"
    New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
    Copy-Item -LiteralPath $keyPath -Destination (Join-Path $secretDirectory "id_vpsdesk") -Force
    if (Test-Path -LiteralPath ($keyPath + ".pub") -PathType Leaf) {
        Copy-Item -LiteralPath ($keyPath + ".pub") -Destination (Join-Path $secretDirectory "id_vpsdesk.pub") -Force
    }
    $envLines = $envLines | ForEach-Object {
        if ($_ -match '^\s*SSH_KEY_PATH\s*=') { 'SSH_KEY_PATH=secrets/id_vpsdesk' } else { $_ }
    }
}

Set-Content -LiteralPath (Join-Path $payloadRoot ".env") -Value $envLines -Encoding utf8NoBOM

$env:VPSDESK_INSTALLER_PAYLOAD = $payloadRoot
$env:VPSDESK_INSTALLER_OUTPUT = $deliveryRoot
& $InnoSetupCompiler $installerScript
if ($LASTEXITCODE -ne 0) { throw "Falló la creación del instalador personal." }

Write-Host "Instalador creado en: $deliveryRoot\VPS-Desk-Personal-Setup.exe"
