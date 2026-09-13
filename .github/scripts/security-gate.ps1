param(
    [string]$BaseSha = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repoRoot

$failures = New-Object System.Collections.Generic.List[string]
$tracked = @(git ls-files)
$self = '.github/scripts/security-gate.ps1'

function Fail([string]$message) {
    $script:failures.Add($message)
    Write-Host "::error::$message"
}

function Is-TextFile([string]$path) {
    $ext = [IO.Path]::GetExtension($path).ToLowerInvariant()
    return $ext -in @('.cs','.csproj','.props','.targets','.ps1','.psm1','.psd1','.json','.yml','.yaml','.xml','.config','.md','.txt','.toml','.ini','.sh','.bash','.env')
}

Write-Host '== VPS-Desk security gate =='

# -----------------------------------------------------------------------------
# Layer 1 - repository secret / sensitive-file integrity
# -----------------------------------------------------------------------------
Write-Host 'Layer 1/4 - secret and sensitive-file guard'

$sensitiveExtensions = @('.pfx','.p12','.p8','.key','.pem','.kdbx','.ppk')
$sensitiveNames = @('id_rsa','id_ed25519','credentials.json','service-account.json','secrets.json')

foreach ($file in $tracked) {
    $leaf = [IO.Path]::GetFileName($file).ToLowerInvariant()
    $ext = [IO.Path]::GetExtension($file).ToLowerInvariant()

    if ($sensitiveExtensions -contains $ext -or $sensitiveNames -contains $leaf) {
        Fail "Sensitive credential/key file is tracked: $file"
    }

    if ($leaf -like '.env*' -and $leaf -ne '.env.example') {
        Fail "Real environment file is tracked: $file"
    }
}

$secretPatterns = @(
    @{ Name = 'Private key'; Pattern = '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----' },
    @{ Name = 'GitHub token'; Pattern = '\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b' },
    @{ Name = 'AWS access key'; Pattern = '\bAKIA[0-9A-Z]{16}\b' },
    @{ Name = 'OpenAI-style key'; Pattern = '\bsk-[A-Za-z0-9_-]{20,}\b' },
    @{ Name = 'Bearer token literal'; Pattern = '(?i)\bBearer\s+[A-Za-z0-9_\-\.=]{24,}\b' },
    @{ Name = 'Connection-string password'; Pattern = '(?i)(?:Password|Pwd)\s*=\s*[^;\r\n]{4,}' },
    @{ Name = 'Assigned secret/token'; Pattern = '(?i)\b(?:api[_-]?key|client[_-]?secret|access[_-]?token|git[_-]?token|ssh[_-]?password)\b\s*[:=]\s*["''][A-Za-z0-9_\-\./+=]{16,}["'']' }
)

foreach ($file in $tracked) {
    if ($file -eq $self -or $file -eq '.env.example' -or -not (Is-TextFile $file)) { continue }
    $full = Join-Path $repoRoot $file
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

    $content = Get-Content -LiteralPath $full -Raw
    foreach ($rule in $secretPatterns) {
        if ($content -match $rule.Pattern) {
            Fail "$($rule.Name) pattern detected in $file"
        }
    }
}

# -----------------------------------------------------------------------------
# Resolve the change range used by the high-risk change gate.
# -----------------------------------------------------------------------------
$zeroSha = '0000000000000000000000000000000000000000'
if ([string]::IsNullOrWhiteSpace($BaseSha) -or $BaseSha -eq $zeroSha) {
    git rev-parse HEAD^ 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $BaseSha = (git rev-parse HEAD^) }
}

$addedLines = @()
if (-not [string]::IsNullOrWhiteSpace($BaseSha)) {
    git cat-file -e "$BaseSha^{commit}" 2>$null
    if ($LASTEXITCODE -eq 0) {
        $diff = @(git diff --no-ext-diff --unified=0 "$BaseSha..HEAD" -- 'src/**' 'Scripts/**' '.github/workflows/**' '*.ps1' '*.bat' '*.cmd' '*.sh')
        $addedLines = @($diff | Where-Object { $_ -match '^\+(?!\+\+\+)' } | ForEach-Object { $_.Substring(1) })
    }
}

# -----------------------------------------------------------------------------
# Layer 2 - high-risk remote execution / destructive-change guard
# Only NEW lines are inspected so legitimate existing server-management code is
# not blocked. A newly introduced destructive primitive must be reviewed and
# redesigned or intentionally adjusted in this gate in the same commit.
# -----------------------------------------------------------------------------
Write-Host 'Layer 2/4 - remote execution and destructive-change guard'

$riskyPatterns = @(
    @{ Name = 'Pipe remote download directly to shell'; Pattern = '(?i)(?:curl|wget)\b[^\r\n|]*\|\s*(?:sudo\s+)?(?:sh|bash|zsh)\b' },
    @{ Name = 'Recursive delete of root'; Pattern = '(?i)\brm\s+-[A-Za-z]*r[A-Za-z]*f[A-Za-z]*\s+/(?:\s|$|\*|\$)' },
    @{ Name = 'Filesystem format command'; Pattern = '(?i)\bmkfs(?:\.[a-z0-9]+)?\b' },
    @{ Name = 'Raw disk overwrite'; Pattern = '(?i)\bdd\s+[^\r\n]*\bof=/dev/(?:sd|vd|xvd|nvme|mmcblk)' },
    @{ Name = 'Disable firewall'; Pattern = '(?i)\b(?:ufw\s+disable|systemctl\s+(?:stop|disable)\s+(?:firewalld|ufw)|iptables\s+-F)\b' },
    @{ Name = 'SSH host-key verification disabled'; Pattern = '(?i)StrictHostKeyChecking\s*=\s*(?:no|false)' },
    @{ Name = 'Accept any SSH host key'; Pattern = '(?i)(?:HostKeyReceived|HostKeyReceivedEventArgs)[^\r\n]*(?:CanTrust\s*=\s*true|return\s+true)' },
    @{ Name = 'World-writable permissions'; Pattern = '(?i)\bchmod\s+(?:-R\s+)?777\b' },
    @{ Name = 'Shell execution with encoded/base64 payload'; Pattern = '(?i)(?:powershell|pwsh)[^\r\n]*(?:-EncodedCommand|-enc\s+[A-Za-z0-9+/=]{8,})' },
    @{ Name = 'PowerShell execution-policy bypass'; Pattern = '(?i)-ExecutionPolicy\s+Bypass' }
)

foreach ($line in $addedLines) {
    foreach ($rule in $riskyPatterns) {
        if ($line -match $rule.Pattern) {
            Fail "$($rule.Name) introduced in changed code: $line"
        }
    }
}

# -----------------------------------------------------------------------------
# Layer 3 - CI/workflow integrity
# -----------------------------------------------------------------------------
Write-Host 'Layer 3/4 - CI integrity guard'

$workflowFiles = $tracked | Where-Object { $_ -like '.github/workflows/*.yml' -or $_ -like '.github/workflows/*.yaml' }
foreach ($file in $workflowFiles) {
    $content = Get-Content -LiteralPath (Join-Path $repoRoot $file) -Raw

    if ($content -match '(?m)^\s*pull_request_target\s*:') {
        Fail "pull_request_target is forbidden in $file because it can expose privileged context to untrusted PR code."
    }

    if ($content -notmatch '(?ms)^permissions:\s*\r?\n\s+contents:\s*read\s*$') {
        Fail "$file must keep top-level GitHub Actions permissions restricted to contents: read."
    }

    if ($content -match '(?i)(?:curl|wget)\b[^\r\n|]*\|\s*(?:sh|bash|pwsh|powershell)\b') {
        Fail "Remote download piped directly into a shell is forbidden in $file."
    }
}

# -----------------------------------------------------------------------------
# Layer 4 - dependency vulnerability audit happens in the workflow after restore.
# This script verifies that the workflow has not silently removed that barrier.
# -----------------------------------------------------------------------------
Write-Host 'Layer 4/4 - dependency-audit presence guard'

$ciPath = '.github/workflows/avalonia-v2-ci.yml'
if (-not (Test-Path -LiteralPath $ciPath)) {
    Fail "Required CI workflow is missing: $ciPath"
} else {
    $ci = Get-Content -LiteralPath $ciPath -Raw
    if ($ci -notmatch '(?i)dotnet\s+list\s+VpsDesk\.slnx\s+package\s+--vulnerable\s+--include-transitive') {
        Fail 'CI must run dotnet list VpsDesk.slnx package --vulnerable --include-transitive.'
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "SECURITY GATE FAILED with $($failures.Count) finding(s)."
    exit 1
}

Write-Host ''
Write-Host 'SECURITY GATE PASSED.'
