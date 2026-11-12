<#
.SYNOPSIS
    Compila el APK Release, genera version.json y (opcionalmente) lo publica en Netlify
    para que la app instalada detecte e instale la actualización sola.

.DESCRIPTION
    Requisitos (una sola vez): carga de trabajo 'android' de .NET, JDK y Android SDK, y un
    keystore PROPIO (ver -Keystore). La firma debe ser siempre la misma: si cambia, Android
    rechaza la actualización (UPDATE_INCOMPATIBLE).

    Secretos del keystore por variables de entorno (nunca en el repositorio):
      VPSDESK_KEYSTORE_PASS   contraseña del keystore
      VPSDESK_KEY_ALIAS       alias de la clave (por defecto: vpsdesk)
      VPSDESK_KEY_PASS        contraseña de la clave (por defecto: la del keystore)

.EXAMPLE
    .\Publish-AndroidUpdate.ps1 -UpdateBaseUrl https://mi-sitio.netlify.app/<ruta> -Keystore D:\privado\vpsdesk.keystore -Deploy
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UpdateBaseUrl,   # carpeta pública donde quedan version.json y el APK (sin barra final)
    [Parameter(Mandatory)][string]$Keystore,
    [string]$Notes = '',
    [string]$OutDir,
    [string]$NetlifySiteId,
    [switch]$Deploy,
    [switch]$Telegram   # envía el APK por Telegram (TELEGRAM_BOT_TOKEN y TELEGRAM_CHAT_ID en el entorno)
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project  = Join-Path $RepoRoot 'src\VpsDesk.Android\VpsDesk.Android.csproj'
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'dist\android-update' }
$UpdateBaseUrl = $UpdateBaseUrl.TrimEnd('/')

if (-not (Test-Path $Keystore)) { throw "No existe el keystore: $Keystore" }
if (-not $env:VPSDESK_KEYSTORE_PASS) { throw 'Define VPSDESK_KEYSTORE_PASS (contraseña del keystore).' }
$alias   = if ($env:VPSDESK_KEY_ALIAS) { $env:VPSDESK_KEY_ALIAS } else { 'vpsdesk' }
$keyPass = if ($env:VPSDESK_KEY_PASS)  { $env:VPSDESK_KEY_PASS }  else { $env:VPSDESK_KEYSTORE_PASS }

if (-not $env:VPSDESK_KEY_PASS) { $env:VPSDESK_KEY_PASS = $keyPass }   # msbuild lo lee con el prefijo env:

[xml]$csproj = Get-Content $Project
$versionCode = [int]$csproj.Project.PropertyGroup.ApplicationVersion
$versionName = [string]$csproj.Project.PropertyGroup.ApplicationDisplayVersion

Write-Host "== Compilando VPS Desk $versionName (código $versionCode)" -ForegroundColor Cyan
dotnet publish $Project -c Release `
    "-p:VpsDeskUpdateUrl=$UpdateBaseUrl/version.json" `
    -p:AndroidKeyStore=true `
    "-p:AndroidSigningKeyStore=$Keystore" `
    "-p:AndroidSigningKeyAlias=$alias" `
    "-p:AndroidSigningKeyPass=env:VPSDESK_KEY_PASS" `
    "-p:AndroidSigningStorePass=env:VPSDESK_KEYSTORE_PASS"
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }

$apk = Get-ChildItem (Join-Path $RepoRoot 'src\VpsDesk.Android\bin\Release') -Recurse -Filter '*-Signed.apk' |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw 'No se encontró el APK firmado.' }

if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir | Out-Null
$apkName = "vpsdesk-$versionCode.apk"
Copy-Item $apk.FullName (Join-Path $OutDir $apkName)
$sha = (Get-FileHash (Join-Path $OutDir $apkName) -Algorithm SHA256).Hash

[ordered]@{
    versionCode = $versionCode
    versionName = $versionName
    apkUrl      = $apkName
    sha256      = $sha
    notes       = $Notes
} | ConvertTo-Json | Set-Content (Join-Path $OutDir 'version.json') -Encoding utf8

# Evita que Netlify cachee version.json (la app compara versiones con él).
@"
/version.json
  Cache-Control: no-cache
/*
  X-Robots-Tag: noindex, nofollow
"@ | Set-Content (Join-Path $OutDir '_headers') -Encoding ascii
'User-agent: *' + [Environment]::NewLine + 'Disallow: /' | Set-Content (Join-Path $OutDir 'robots.txt') -Encoding ascii

Write-Host "APK listo: $OutDir\$apkName  (SHA-256 $sha)" -ForegroundColor Green

if ($Deploy) {
    $args = @('--yes', 'netlify-cli', 'deploy', '--prod', '--dir', $OutDir)
    if ($NetlifySiteId) { $args += @('--site', $NetlifySiteId) }
    Write-Host '== Publicando en Netlify' -ForegroundColor Cyan
    & npx @args
    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación en Netlify (¿netlify login?).' }
}

if ($Telegram) {
    if (-not $env:TELEGRAM_BOT_TOKEN -or -not $env:TELEGRAM_CHAT_ID) {
        throw 'Define TELEGRAM_BOT_TOKEN y TELEGRAM_CHAT_ID (variables de entorno de usuario).'
    }
    Write-Host '== Enviando APK por Telegram' -ForegroundColor Cyan
    $caption = "VPS Desk $versionName (código $versionCode)`nSHA-256: $sha"
    if ($Notes) { $caption += "`n$Notes" }
    $apkPath = Join-Path $OutDir $apkName
    # curl.exe (incluido en Windows) hace el multipart; el token va en la URL, no se imprime.
    $resp = & curl.exe -s -S -F "chat_id=$($env:TELEGRAM_CHAT_ID)" -F "caption=$caption" `
        -F "document=@$apkPath" "https://api.telegram.org/bot$($env:TELEGRAM_BOT_TOKEN)/sendDocument"
    if ($LASTEXITCODE -ne 0 -or $resp -notmatch '"ok":true') { throw "Falló el envío a Telegram: $resp" }
    Write-Host 'APK enviado por Telegram.' -ForegroundColor Green
}
