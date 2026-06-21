function Test-HostReachable {
    param([string]$SshHost, [int]$Port = 22, [int]$TimeoutMs = 3000)
    if ([string]::IsNullOrWhiteSpace($SshHost)) { return $false }
    $tcp = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $tcp.ConnectAsync($SshHost, $Port)
        $task.Wait($TimeoutMs) | Out-Null
        return $tcp.Connected
    } catch { return $false }
    finally   { $tcp.Dispose() }
}

function Invoke-RemoteSsh {
    param(
        [string]$SshHost,
        [string]$User,
        [int]$Port    = 22,
        [string]$KeyPath,
        [string]$Command,
        [int]$TimeoutSec = 8
    )
    $args = @('-o', "ConnectTimeout=$TimeoutSec", '-o', 'StrictHostKeyChecking=no',
              '-o', 'BatchMode=yes', '-p', $Port)
    if ($KeyPath -and (Test-Path $KeyPath)) { $args += '-i', $KeyPath }
    $args += "$User@$SshHost", $Command

    $out = & ssh @args 2>$null
    if ($LASTEXITCODE -eq 0) { return $out -join "`n" }
    return $null
}

function Get-ServerMetrics {
    param([string]$SshHost, [string]$User, [int]$Port, [string]$KeyPath)
    $p = @{ SshHost=$SshHost; User=$User; Port=$Port; KeyPath=$KeyPath }
    $cpu    = Invoke-RemoteSsh @p -Command "top -bn1 | grep 'Cpu(s)' | awk '{print \$2\"%\"}"
    $mem    = Invoke-RemoteSsh @p -Command "free -h | awk 'NR==2{print \$3\"/\"\$2}'"
    $disk   = Invoke-RemoteSsh @p -Command "df -h / | awk 'NR==2{print \$3\"/\"\$2\" (\"\$5\")\"}'"
    $uptime = Invoke-RemoteSsh @p -Command "uptime -p"
    return @{ cpu=$cpu; memory=$mem; disk=$disk; uptime=$uptime }
}

function Get-DockerServiceStates {
    param([string]$SshHost, [string]$User, [int]$Port, [string]$KeyPath)
    $raw = Invoke-RemoteSsh @{ SshHost=$SshHost; User=$User; Port=$Port; KeyPath=$KeyPath } `
           -Command "docker ps --format '{{.Names}}\t{{.Status}}' 2>/dev/null"
    if (-not $raw) { return @{} }
    $states = @{}
    foreach ($line in $raw -split "`n") {
        $parts = $line -split "`t"
        if ($parts.Count -ge 2) {
            $name  = $parts[0].Trim().ToLower()
            $state = if ($parts[1] -match '^Up') { 'running' } else { 'stopped' }
            foreach ($svc in @('sql','api','front')) {
                if ($name -like "*$svc*") { $states[$svc] = $state }
            }
        }
    }
    return $states
}

function Get-RemoteStorageInfo {
    param([string]$SshHost, [string]$User, [int]$Port, [string]$KeyPath)
    $p   = @{ SshHost=$SshHost; User=$User; Port=$Port; KeyPath=$KeyPath }
    $cmd = @"
echo '[DISK]'
df -h /
echo '[DOCKER_IMAGES]'
docker images --format '{{.Repository}}:{{.Tag}}\t{{.Size}}\t{{.ID}}' 2>/dev/null
echo '[TOP_DIRS]'
du -sh /var/lib/docker/*/  2>/dev/null | sort -rh | head -8
"@
    return Invoke-RemoteSsh @p -Command $cmd
}

function Invoke-DockerPrune {
    param([string]$SshHost, [string]$User, [int]$Port, [string]$KeyPath, [string]$Target)
    $cmd = switch ($Target) {
        'builder' { 'docker builder prune -f 2>&1' }
        'images'  { 'docker image prune -af 2>&1' }
        default   { 'docker system prune -af --volumes 2>&1' }
    }
    return Invoke-RemoteSsh @{ SshHost=$SshHost; User=$User; Port=$Port; KeyPath=$KeyPath } -Command $cmd -TimeoutSec 60
}

function Get-RemoteLog {
    param([string]$SshHost, [string]$User, [int]$Port, [string]$KeyPath,
          [string]$Source = 'migrator', [int]$Tail = 300)
    $cmd = switch ($Source) {
        'migrator'    { "tail -n $Tail ~/migrator_logs.txt 2>/dev/null" }
        'host-syslog' { "journalctl -n $Tail --no-pager 2>/dev/null" }
        'host-auth'   { "journalctl -u ssh -n $Tail --no-pager 2>/dev/null" }
        default       { "docker logs --tail $Tail $Source 2>&1" }
    }
    return Invoke-RemoteSsh @{ SshHost=$SshHost; User=$User; Port=$Port; KeyPath=$KeyPath } -Command $cmd -TimeoutSec 30
}
