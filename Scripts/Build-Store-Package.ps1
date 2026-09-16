[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StoreIdentityName,
    [Parameter(Mandatory)] [string]$StorePublisher,
    [Parameter(Mandatory)] [string]$PublisherDisplayName,
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')] [string]$Version = "1.0.0.0",
    [string]$DisplayName = "VPS Desk",
    [string]$CertificatePath,
    [SecureString]$CertificatePassword
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $repoRoot "src\VpsDesk.Desktop\VpsDesk.Desktop.csproj"
$manifestTemplate = Join-Path $repoRoot "Packaging\AppxManifest.template.xml"
$stageDirectory = Join-Path $repoRoot "obj\store-package\stage"
$outputDirectory = Join-Path $repoRoot "app\clinical-care\appointments\store"
$packagePath = Join-Path $outputDirectory "VPS-Desk-$Version.msix"
$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe |
    Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if ([string]::IsNullOrWhiteSpace($makeAppx)) {
    throw "No se encontró makeappx.exe. Instala Windows SDK para generar el paquete MSIX."
}

foreach ($target in @($stageDirectory, $outputDirectory)) {
    $parent = [IO.Path]::GetFullPath((Split-Path -Parent $target))
    if (-not $parent.StartsWith([IO.Path]::GetFullPath($repoRoot), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Ruta de salida no segura: $target"
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
New-Item -ItemType Directory -Path $stageDirectory, $outputDirectory -Force | Out-Null

dotnet publish $desktopProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false `
    -o $stageDirectory
if ($LASTEXITCODE -ne 0) { throw "Falló la publicación para Microsoft Store." }

foreach ($forbidden in @(".env", "secrets", "data", "portable.mode")) {
    if (Test-Path -LiteralPath (Join-Path $stageDirectory $forbidden)) {
        throw "El paquete de Store contiene un archivo no permitido: $forbidden"
    }
}

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::new((Join-Path $repoRoot "src\VpsDesk.Desktop\Assets\vps-desk.ico"))
$assetsDirectory = Join-Path $stageDirectory "Assets"
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null
foreach ($size in @(44, 150)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(23, 35, 59))
    $graphics.DrawIcon($icon, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
    $bitmap.Save((Join-Path $assetsDirectory "Square${size}x${size}Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}
$icon.Dispose()

function Escape-Xml([string]$value) { [System.Security.SecurityElement]::Escape($value) }
$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest.Replace("__IDENTITY_NAME__", (Escape-Xml $StoreIdentityName))
$manifest = $manifest.Replace("__PUBLISHER__", (Escape-Xml $StorePublisher))
$manifest = $manifest.Replace("__PUBLISHER_DISPLAY_NAME__", (Escape-Xml $PublisherDisplayName))
$manifest = $manifest.Replace("__DISPLAY_NAME__", (Escape-Xml $DisplayName))
$manifest = $manifest.Replace("__VERSION__", $Version)
Set-Content -LiteralPath (Join-Path $stageDirectory "AppxManifest.xml") -Value $manifest -Encoding utf8NoBOM

& $makeAppx pack /d $stageDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw "Falló la creación del paquete MSIX." }

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw "No existe el certificado de firma indicado." }
    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($signTool)) { throw "No se encontró signtool.exe en Windows SDK." }
    $password = if ($CertificatePassword) {
        [System.Net.NetworkCredential]::new("", $CertificatePassword).Password
    } else { throw "Indica CertificatePassword para firmar el paquete." }
    & $signTool sign /fd SHA256 /f $CertificatePath /p $password /tr "http://timestamp.digicert.com" /td SHA256 $packagePath
    if ($LASTEXITCODE -ne 0) { throw "Falló la firma del paquete MSIX." }
}
else {
    Write-Warning "El MSIX está sin firmar. Para Partner Center, usa el certificado asociado a la identidad de Store."
}

Write-Host "Paquete MSIX limpio creado en: $packagePath"
