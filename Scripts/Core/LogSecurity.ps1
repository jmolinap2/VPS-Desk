$script:SensitivePatterns = @(
    [regex]'(?i)(password|passwd|token|secret|key)\s*[=:]\s*\S+'
    [regex]'(?i)-SshPassword\s+\S+'
    [regex]'(?i)-GitToken\s+\S+'
    [regex]'\b[A-Za-z0-9+/]{40,}={0,2}\b'   # base64-like tokens
)

function Protect-LogLine {
    param([string]$Line)
    $result = $Line
    foreach ($pattern in $script:SensitivePatterns) {
        $result = $pattern.Replace($result, { param($m) $m.Value -replace '\S{4}$','****' })
    }
    return $result
}

$script:AnsiRegex = [regex]'\x1b\[[0-9;]*[mGKHF]'

function Remove-AnsiCodes {
    param([string]$Text)
    $script:AnsiRegex.Replace($Text, '')
}
