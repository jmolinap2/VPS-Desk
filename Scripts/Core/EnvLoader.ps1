function Find-EnvFile {
    # Walk up from project root
    $dir = [System.IO.DirectoryInfo]::new($script:RootPath)
    while ($null -ne $dir) {
        $candidate = [System.IO.Path]::Combine($dir.FullName, '.env')
        if ([System.IO.File]::Exists($candidate)) { return $candidate }
        $dir = $dir.Parent
    }

    return $null
}

function Read-EnvFile {
    param([string]$Path)
    $result = @{}
    foreach ($rawLine in [System.IO.File]::ReadAllLines($Path)) {
        $line = $rawLine.Trim()
        if (-not $line -or $line.StartsWith('#')) { continue }
        $idx = $line.IndexOf('=')
        if ($idx -le 0) { continue }
        $key   = $line.Substring(0, $idx).Trim()
        $value = $line.Substring($idx + 1).Trim().Trim('"').Trim("'")
        $result[$key] = $value
    }
    return $result
}

function Get-AppSettingsPath {
    [System.IO.Path]::Combine($env:APPDATA, 'VPSDesk', 'settings.json')
}

function Save-AppSettings {
    param([hashtable]$Extra = @{})
    $c    = $script:Controls
    $path = Get-AppSettingsPath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    $s = @{
        Environment         = $script:AppState.CurrentEnvironment
        RememberSshPassword = [bool]$c['RememberSshPasswordCheck'].IsChecked
    } + $Extra
    $s | ConvertTo-Json | Set-Content $path -Encoding UTF8
}

function Load-AppSettings {
    $path = Get-AppSettingsPath
    if (-not (Test-Path $path)) { return @{} }
    try   { return Get-Content $path -Raw | ConvertFrom-Json -AsHashtable }
    catch { return @{} }
}

