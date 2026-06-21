$script:EnvProfiles = @{
    Development = @{ RiskLabel = 'DEV';        RiskColor = '#2D7A2D' }
    Staging     = @{ RiskLabel = 'STAGING';    RiskColor = '#7A5A2D' }
    Production  = @{ RiskLabel = 'PRODUCCION'; RiskColor = '#8E2D2D' }
}

function Get-EnvProfile {
    param([string]$Env = $script:AppState.CurrentEnvironment)
    $script:EnvProfiles[$Env] ?? $script:EnvProfiles['Staging']
}

function Get-NextEnvironment {
    param([string]$Current)
    switch ($Current) {
        'Development' { 'Staging' }
        'Staging'     { 'Production' }
        default       { 'Development' }
    }
}

function Test-ProductionGuard {
    param([bool]$SkipPublicChecks, [bool]$SkipMigrations, [string]$Action)
    if ($script:AppState.CurrentEnvironment -ne 'Production') { return $null }
    if ($SkipPublicChecks)                                    { return 'En Production no se permite omitir checks publicos.' }
    if ($Action -eq 'Deploy completo' -and $SkipMigrations)  { return 'En Production no se permite omitir migraciones.' }
    return $null
}
