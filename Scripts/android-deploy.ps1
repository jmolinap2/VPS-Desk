<#
.SYNOPSIS
    Despliega VPS Desk en el telefono Android (POCO X6 Pro): compila, instala,
    copia los secretos (.env y clave SSH) y abre la app.

.DESCRIPTION
    Sin parametros muestra un menu. Con parametros ejecuta solo lo indicado.
    - Nunca muestra valores de secretos: solo nombres de variables y verificaciones.
    - Nunca desinstala la app (borraria sus datos y los secretos copiados).
    - Detecta el telefono por identidad (modelo y dispositivo), no por puerto.

.EXAMPLE
    .\android-deploy.ps1                          Menu interactivo
    .\android-deploy.ps1 -Doctor                  Solo diagnostico, no cambia nada
    .\android-deploy.ps1 -All                     Compila, instala, copia secretos y abre la app
    .\android-deploy.ps1 -Install -Apk "C:\x\com.vpsdesk.app-Signed.apk"
    .\android-deploy.ps1 -Secrets -KeySource "D:\privado\holos_ed25519" -DryRun
    .\android-deploy.ps1 -Pair                    Solo si se perdio la vinculacion ADB
#>
[CmdletBinding()]
param(
    [switch]$Doctor,
    [switch]$Pair,
    [switch]$Build,
    [switch]$Install,
    [switch]$Secrets,
    [switch]$Launch,
    [switch]$Logs,
    [switch]$All,
    [string]$Apk,
    [string]$SecretsEnv,
    [string]$KeySource,
    [string]$AdbPath = 'C:\Android\Sdk\platform-tools\adb.exe',
    [switch]$DryRun
)

$ErrorActionPreference = 'Continue'

$Package        = 'com.vpsdesk.app'
$ExpectedModel  = '2311DRK48G'
$ExpectedDevice = 'duchamp'
$RemoteDir      = "/sdcard/Android/data/$Package/files"
$RepoRoot       = Split-Path -Parent $PSScriptRoot
$ProjectPath    = Join-Path $RepoRoot 'src\VpsDesk.Android\VpsDesk.Android.csproj'
$ReleaseDir     = Join-Path $RepoRoot 'src\VpsDesk.Android\bin\Release'
$SourceDir      = Join-Path $RepoRoot 'src'

# ---------------------------------------------------------------- salida

function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text)   { Write-Host "  [OK] $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "  [!]  $Text" -ForegroundColor Yellow }
function Write-Bad([string]$Text)  { Write-Host "  [X]  $Text" -ForegroundColor Red }

# ---------------------------------------------------------------- adb

function Invoke-Adb {
    param([string[]]$Arguments, [switch]$AllowFail)
    $output = (& $AdbPath @Arguments 2>&1 | Out-String).Trim()
    $code = $LASTEXITCODE
    if ($code -ne 0 -and -not $AllowFail) {
        throw "adb $($Arguments -join ' ') fallo (codigo $code): $output"
    }
    return $output
}

function Get-AdbDevices {
    $text = Invoke-Adb -Arguments @('devices', '-l') -AllowFail
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^(\S+)\s+(device|unauthorized|offline)\b(.*)$') {
            $serial = $Matches[1]
            $state  = $Matches[2]
            $rest   = $Matches[3]
            $model  = ''
            $device = ''
            if ($rest -match 'model:(\S+)')  { $model  = $Matches[1] }
            if ($rest -match 'device:(\S+)') { $device = $Matches[1] }
            [pscustomobject]@{ Serial = $serial; State = $state; Model = $model; Device = $device }
        }
    }
}

function Test-ExpectedPhone($Dev) {
    return ($Dev.State -eq 'device' -and $Dev.Model -eq $ExpectedModel -and $Dev.Device -eq $ExpectedDevice)
}

function Get-MdnsEndpoints([string]$Service) {
    $text = Invoke-Adb -Arguments @('mdns', 'services') -AllowFail
    $pattern = '_' + [regex]::Escape($Service) + '\._tcp\s+(\S+:\d+)'
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match $pattern) { $Matches[1] }
    }
}

function Connect-Candidates {
    foreach ($endpoint in @(Get-MdnsEndpoints -Service 'adb-tls-connect')) {
        $result = Invoke-Adb -Arguments @('connect', $endpoint) -AllowFail
        if ($result -match '10013|WSAEACCES') {
            Write-Warn 'Windows bloqueo el socket: reinicio el servidor ADB y reintento.'
            $null = Invoke-Adb -Arguments @('kill-server') -AllowFail
            $null = Invoke-Adb -Arguments @('start-server') -AllowFail
            $null = Invoke-Adb -Arguments @('connect', $endpoint) -AllowFail
        }
    }
}

# Busca el telefono por identidad. Nunca usa un puerto guardado.
function Find-Phone {
    $all   = @(Get-AdbDevices)
    $match = @($all | Where-Object { Test-ExpectedPhone $_ })
    if ($match.Count -eq 0) {
        Write-Warn 'El telefono no aparece en adb devices. Busco por mDNS...'
        Connect-Candidates
        $all   = @(Get-AdbDevices)
        $match = @($all | Where-Object { Test-ExpectedPhone $_ })
    }
    if ($match.Count -gt 1) { throw 'Hay varios dispositivos que coinciden con el POCO X6 Pro. Desconecta los que sobren.' }
    if ($match.Count -eq 1) { return $match[0] }

    $unauthorized = @($all | Where-Object { $_.State -eq 'unauthorized' })
    if ($unauthorized.Count -gt 0) {
        throw 'El telefono pide autorizacion: acepta la depuracion en la pantalla del telefono y repite.'
    }
    throw 'No encuentro el POCO X6 Pro. Si Android pidio nuevo emparejamiento, usa la opcion 2 (Emparejar).'
}

# ---------------------------------------------------------------- acciones

function Invoke-Pair {
    Write-Step 'Emparejar el telefono (solo si Android pidio nuevo emparejamiento)'
    Write-Host '  En el telefono: Opciones de desarrollador > Depuracion inalambrica >' -ForegroundColor DarkGray
    Write-Host '  "Vincular dispositivo con un codigo de emparejamiento". Deja esa pantalla abierta.' -ForegroundColor DarkGray

    $pairing = @(Get-MdnsEndpoints -Service 'adb-tls-pairing')
    if ($pairing.Count -eq 0) {
        throw 'No veo el servicio de emparejamiento. Abre "Vincular dispositivo con un codigo de emparejamiento" en el telefono y repite.'
    }
    $endpoint = $pairing[0]
    if ($pairing.Count -gt 1) {
        Write-Warn ("Hay varios emparejamientos; uso {0}. Si falla, cierra y vuelve a abrir la pantalla del telefono." -f $endpoint)
    }

    $code = (Read-Host 'Codigo de 6 digitos que muestra el telefono').Trim()
    if ($code -notmatch '^\d{6}$') { throw 'El codigo debe tener 6 digitos.' }

    $result = Invoke-Adb -Arguments @('pair', $endpoint, $code) -AllowFail
    if ($result -match 'protocol fault') {
        throw 'El codigo o el puerto ya expiraron. Vuelve a abrir "Vincular dispositivo..." en el telefono y repite esta opcion.'
    }
    if ($result -notmatch 'Successfully paired') { throw "No se pudo emparejar: $result" }
    Write-Ok 'Emparejado.'

    Connect-Candidates
    $connected = @(Get-AdbDevices | Where-Object { Test-ExpectedPhone $_ })
    if ($connected.Count -eq 0) {
        Write-Warn 'mDNS aun no muestra la conexion. Usa la IP:puerto de "Depuracion inalambrica > Direccion IP y puerto".'
        $address = (Read-Host 'IP:puerto de conexion (Enter para omitir)').Trim()
        if ($address) { $null = Invoke-Adb -Arguments @('connect', $address) -AllowFail }
    }
}

function Invoke-Build {
    Write-Step 'Compilar APK (Release)'
    $workloads = (& dotnet workload list 2>&1 | Out-String)
    if ($workloads -notmatch '(?m)^\s*android\s') {
        throw "Falta la carga de trabajo 'android' de .NET. Instalala con 'dotnet workload install android' (quiza debas abrir la terminal como administrador), o compila desde Visual Studio y usa -Apk."
    }
    if ($DryRun) {
        Write-Warn "DryRun: no compilo. Comando: dotnet build `"$ProjectPath`" -c Release"
        return
    }
    $null = & dotnet build $ProjectPath -c Release --nologo -v q 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "La compilacion fallo. Ejecuta: dotnet build `"$ProjectPath`" -c Release para ver los errores."
    }
    Write-Ok 'APK compilado.'
}

function Get-NewestSource {
    Get-ChildItem -Path $SourceDir -Recurse -File -Include '*.cs', '*.axaml' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Resolve-Apk {
    if ($Apk) {
        if (-not (Test-Path -LiteralPath $Apk)) { throw "No existe el APK indicado: $Apk" }
        return (Resolve-Path -LiteralPath $Apk).Path
    }
    $latest = Get-ChildItem -Path $ReleaseDir -Recurse -Filter "$Package-Signed.apk" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) { throw 'No hay APK compilado. Usa -Build o indica uno con -Apk.' }

    $newest = Get-NewestSource
    if ($newest -and $latest.LastWriteTime -lt $newest.LastWriteTime) {
        Write-Warn ("El APK es mas viejo que tu codigo ({0} cambio despues). Compila de nuevo con -Build." -f $newest.Name)
    }
    return $latest.FullName
}

function Invoke-Install {
    param([string]$Serial, [string]$ApkPath)
    Write-Step 'Instalar APK (conserva los datos de la app)'
    if ($DryRun) {
        Write-Warn "DryRun: adb install -r `"$ApkPath`""
        return
    }
    $result = Invoke-Adb -Arguments @('-s', $Serial, 'install', '-r', $ApkPath) -AllowFail
    if ($result -match 'Success') {
        Write-Ok 'APK instalado.'
        return
    }
    if ($result -match 'UPDATE_INCOMPATIBLE|INCONSISTENT_CERTIFICATES|signatures do not match') {
        throw 'La firma del APK no coincide con la app instalada. El script NO la desinstala: se borrarian los datos y los secretos del telefono. Si aceptas perderlos, desinstala la app tu mismo y repite las opciones 4 y 5.'
    }
    if ($result -match 'USER_RESTRICTED|INSTALL_FAILED_USER') {
        throw 'MIUI bloqueo la instalacion. En el telefono activa "Instalar via USB" y "Depuracion de seguridad" en Opciones de desarrollador, y repite.'
    }
    throw "No se pudo instalar: $result"
}

function Confirm-Remote {
    param([string]$Serial, [string]$LocalPath, [string]$RemotePath)
    $localHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $LocalPath).Hash.ToLower()
    $remote = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'sha256sum', $RemotePath) -AllowFail
    if ($remote -match '^([0-9a-fA-F]{64})') {
        if ($Matches[1].ToLower() -eq $localHash) {
            Write-Ok ("Verificado en el telefono: {0}" -f (Split-Path -Leaf $RemotePath))
            return
        }
        throw "La copia de $RemotePath en el telefono no coincide con el original."
    }
    Write-Warn ("No pude verificar {0} (sha256sum no disponible en este Android)." -f (Split-Path -Leaf $RemotePath))
}

function Invoke-Secrets {
    param([string]$Serial)
    Write-Step 'Copiar secretos al telefono (.env y clave SSH)'

    $envSource = if ($SecretsEnv) { $SecretsEnv } else { Join-Path $RepoRoot '.env' }
    if (-not (Test-Path -LiteralPath $envSource)) {
        throw "No encuentro el .env de origen: $envSource. Usa -SecretsEnv con la ruta correcta."
    }
    $envSource = (Resolve-Path -LiteralPath $envSource).Path
    $envDir    = Split-Path -Parent $envSource
    $lines     = [System.IO.File]::ReadAllLines($envSource)

    # Solo nombres de variables: los valores nunca se muestran.
    $names = @($lines | Where-Object { $_ -match '^\s*[A-Za-z_][A-Za-z0-9_]*\s*=' } |
        ForEach-Object { ($_ -split '=', 2)[0].Trim() })
    Write-Ok ("Variables en el .env: {0} (valores no mostrados)" -f $names.Count)
    Write-Host ('      ' + ($names -join ', ')) -ForegroundColor DarkGray

    # Clave SSH: -KeySource manda; si no, SSH_KEY_PATH del .env (relativo al .env).
    $keyLine       = $lines | Where-Object { $_ -match '^\s*SSH_KEY_PATH\s*=' } | Select-Object -First 1
    $keySourcePath = $null
    if ($KeySource) {
        $keySourcePath = $KeySource
    }
    elseif ($keyLine) {
        $keyValue = (($keyLine -split '=', 2)[1]).Trim().Trim('"')
        if ($keyValue) {
            $keySourcePath = if ([System.IO.Path]::IsPathRooted($keyValue)) { $keyValue } else { Join-Path $envDir $keyValue }
        }
    }

    $keyName = $null
    if ($keySourcePath) {
        if (-not (Test-Path -LiteralPath $keySourcePath)) {
            throw "No encuentro la clave SSH: $keySourcePath. Indica su ruta con -KeySource."
        }
        $keySourcePath = (Resolve-Path -LiteralPath $keySourcePath).Path
        $keyName       = Split-Path -Leaf $keySourcePath
    }

    # En el telefono la clave va junto al .env y SSH_KEY_PATH queda como nombre de archivo.
    $phoneLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($keyName -and $line -match '^\s*SSH_KEY_PATH\s*=') {
            $phoneLines.Add("SSH_KEY_PATH=$keyName")
            continue
        }
        $phoneLines.Add($line)
    }
    if ($keyName -and -not $keyLine) { $phoneLines.Add("SSH_KEY_PATH=$keyName") }

    if ($DryRun) {
        Write-Warn "DryRun: copiaria el .env a $RemoteDir/.env (contenido no mostrado)."
        if ($keyName) { Write-Warn "DryRun: copiaria la clave a $RemoteDir/$keyName" }
        return
    }

    $tempEnv = Join-Path $env:TEMP ('vpsdesk-' + [guid]::NewGuid().ToString('N') + '.env')
    try {
        [System.IO.File]::WriteAllLines($tempEnv, $phoneLines)

        $app = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'pm', 'path', $Package) -AllowFail
        if ($app -notmatch 'package:') {
            throw 'La app no esta instalada en el telefono. Instalala primero (opcion 4).'
        }
        $null = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'mkdir', '-p', $RemoteDir) -AllowFail

        $null = Invoke-Adb -Arguments @('-s', $Serial, 'push', $tempEnv, "$RemoteDir/.env")
        Confirm-Remote -Serial $Serial -LocalPath $tempEnv -RemotePath "$RemoteDir/.env"

        if ($keySourcePath) {
            $null = Invoke-Adb -Arguments @('-s', $Serial, 'push', $keySourcePath, "$RemoteDir/$keyName")
            Confirm-Remote -Serial $Serial -LocalPath $keySourcePath -RemotePath "$RemoteDir/$keyName"
        }
        Write-Ok 'Secretos copiados. La app los lee al arrancar.'
    }
    finally {
        # El .env temporal con el contenido de los secretos se borra siempre.
        if (Test-Path -LiteralPath $tempEnv) { Remove-Item -LiteralPath $tempEnv -Force }
    }
}

function Invoke-Launch {
    param([string]$Serial)
    Write-Step 'Abrir la app'
    $null = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'monkey', '-p', $Package, '-c', 'android.intent.category.LAUNCHER', '1') -AllowFail
    Write-Ok 'Orden de apertura enviada.'
}

function Show-Logs {
    param([string]$Serial)
    Write-Step 'Registro reciente de la app'
    $pidText = Invoke-Adb -Arguments @('-s', $Serial, 'shell', 'pidof', $Package) -AllowFail
    $appPid  = ($pidText -split '\s+' | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1)
    if ($appPid) {
        $log = Invoke-Adb -Arguments @('-s', $Serial, 'logcat', '-d', "--pid=$appPid", '-t', '200') -AllowFail
    }
    else {
        Write-Warn 'La app no esta corriendo. Muestro mensajes recientes relacionados.'
        $raw = Invoke-Adb -Arguments @('-s', $Serial, 'logcat', '-d', '-t', '2000') -AllowFail
        $log = (($raw -split "`r?`n") | Where-Object { $_ -match 'VpsDesk|vpsdesk|SshNet|Renci' }) -join "`n"
    }
    Write-Host $log
}

function Invoke-Doctor {
    Write-Step 'Diagnostico (solo lectura)'
    Write-Ok "adb: $AdbPath"

    $phone = $null
    try {
        $phone = Find-Phone
        Write-Ok ("Telefono: serie {0} | modelo {1} | dispositivo {2}" -f $phone.Serial, $phone.Model, $phone.Device)
    }
    catch { Write-Bad $_.Exception.Message }

    if ($phone) {
        $path = Invoke-Adb -Arguments @('-s', $phone.Serial, 'shell', 'pm', 'path', $Package) -AllowFail
        if ($path -match 'package:') { Write-Ok 'App instalada.' } else { Write-Bad 'App no instalada (opcion 4).' }

        # -a: sin el, ls oculta el .env (los archivos que empiezan por punto).
        $listing = Invoke-Adb -Arguments @('-s', $phone.Serial, 'shell', 'ls', '-la', $RemoteDir) -AllowFail
        if ($listing -match '\.env') { Write-Ok 'Archivo .env presente en el telefono.' } else { Write-Warn '.env no esta en el telefono (opcion 5).' }
        Write-Host '      Archivos en la carpeta de la app (nombres y tamanos):' -ForegroundColor DarkGray
        foreach ($l in ($listing -split "`r?`n")) { Write-Host "      $l" -ForegroundColor DarkGray }
    }

    $envSource = if ($SecretsEnv) { $SecretsEnv } else { Join-Path $RepoRoot '.env' }
    if (Test-Path -LiteralPath $envSource) { Write-Ok 'Origen de secretos: .env encontrado.' }
    else { Write-Bad "Falta el .env de origen: $envSource" }

    $workloads = (& dotnet workload list 2>&1 | Out-String)
    if ($workloads -match '(?m)^\s*android\s') { Write-Ok "Carga de trabajo 'android' instalada." }
    else { Write-Warn "Falta la carga de trabajo 'android': compila desde Visual Studio y usa -Apk, o instala la carga." }

    try { Write-Ok ("APK a instalar: {0}" -f (Resolve-Apk)) }
    catch { Write-Warn $_.Exception.Message }
}

# ---------------------------------------------------------------- despacho

# En -DryRun no hace falta el telefono para revisar los archivos de origen.
function Get-PhoneSerial {
    try { return (Find-Phone).Serial }
    catch {
        if ($DryRun) {
            Write-Warn 'DryRun: sin telefono conectado; reviso solo los archivos de origen.'
            return ''
        }
        throw
    }
}

function Invoke-Action([string]$Name) {
    try {
        switch ($Name) {
            'doctor'  { Invoke-Doctor }
            'pair'    { Invoke-Pair }
            'build'   { Invoke-Build }
            'install' { $serial = Get-PhoneSerial; Invoke-Install -Serial $serial -ApkPath (Resolve-Apk) }
            'secrets' { $serial = Get-PhoneSerial; Invoke-Secrets -Serial $serial }
            'launch'  { $phone = Find-Phone; Invoke-Launch -Serial $phone.Serial }
            'logs'    { $phone = Find-Phone; Show-Logs -Serial $phone.Serial }
            'all' {
                Invoke-Build
                $phone = Find-Phone
                Invoke-Install -Serial $phone.Serial -ApkPath (Resolve-Apk)
                Invoke-Secrets -Serial $phone.Serial
                Invoke-Launch -Serial $phone.Serial
            }
        }
        return $true
    }
    catch {
        Write-Bad $_.Exception.Message
        return $false
    }
}

function Show-Menu {
    $options = @{
        '1' = 'doctor'; '2' = 'pair'; '3' = 'build'; '4' = 'install'
        '5' = 'secrets'; '6' = 'launch'; '7' = 'logs'; '8' = 'all'
    }
    while ($true) {
        Write-Host ''
        Write-Host 'VPS Desk - despliegue en Android' -ForegroundColor Cyan
        Write-Host '  1) Diagnostico (no cambia nada)'
        Write-Host '  2) Emparejar telefono (solo si perdio la vinculacion)'
        Write-Host '  3) Compilar APK (Release)'
        Write-Host '  4) Instalar APK (conserva datos)'
        Write-Host '  5) Copiar secretos (.env y clave SSH)'
        Write-Host '  6) Abrir la app'
        Write-Host '  7) Ver registro de la app'
        Write-Host '  8) Todo: compilar, instalar, secretos y abrir'
        Write-Host '  0) Salir'
        $choice = (Read-Host 'Opcion').Trim()
        if ($choice -eq '0' -or $choice -eq '') { return }
        if ($options.ContainsKey($choice)) { $null = Invoke-Action $options[$choice] }
        else { Write-Warn 'Opcion no valida.' }
    }
}

# ---------------------------------------------------------------- main

if (-not (Test-Path -LiteralPath $AdbPath)) {
    Write-Bad "No encuentro adb en $AdbPath. Indica otra ruta con -AdbPath."
    exit 1
}

$selected = @()
if ($All) {
    $selected = @('all')
}
else {
    if ($Doctor)  { $selected += 'doctor' }
    if ($Pair)    { $selected += 'pair' }
    if ($Build)   { $selected += 'build' }
    if ($Install) { $selected += 'install' }
    if ($Secrets) { $selected += 'secrets' }
    if ($Launch)  { $selected += 'launch' }
    if ($Logs)    { $selected += 'logs' }
}

if ($selected.Count -eq 0) {
    Show-Menu
    exit 0
}

$allOk = $true
foreach ($action in $selected) {
    if (-not (Invoke-Action $action)) { $allOk = $false; break }
}
if ($allOk) { exit 0 } else { exit 1 }
