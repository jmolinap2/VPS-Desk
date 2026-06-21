$script:AppState = [ordered]@{
    CurrentEnvironment = 'Staging'
    ServiceStates      = @{}
    Runs               = [System.Collections.Generic.List[hashtable]]::new()
    Logs               = [System.Collections.Generic.List[hashtable]]::new()
    Alerts             = [System.Collections.Generic.List[hashtable]]::new()
}

function Add-Run {
    param([hashtable]$Run)
    $script:AppState.Runs.Add($Run)
}

function Get-Runs { $script:AppState.Runs }

function Set-ServiceState {
    param([string]$Key, [string]$State)
    $script:AppState.ServiceStates[$Key] = $State
}

function Get-ServiceState {
    param([string]$Key)
    if ($script:AppState.ServiceStates.ContainsKey($Key)) { return $script:AppState.ServiceStates[$Key] }
    return 'unknown'
}

function Add-LogEntry {
    param([string]$Source, [string]$Message)
    $script:AppState.Logs.Add(@{
        Timestamp = [datetime]::Now
        Source    = $Source
        Message   = $Message
    })
}
