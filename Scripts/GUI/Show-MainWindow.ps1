function Show-MainWindow {
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase | Out-Null

    # ── Load XAML ──────────────────────────────────────────────────────────
    $xamlPath = Join-Path $script:RootPath 'Schemas\MainWindow.xaml'
    $xaml     = [System.IO.File]::ReadAllText($xamlPath)
    $reader   = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($xaml))
    try   { $script:Window = [System.Windows.Markup.XamlReader]::Load($reader) }
    catch { [System.Windows.MessageBox]::Show("Error cargando XAML:`n$_", 'VPS Desk'); return }
    finally { $reader.Close() }

    # ── Get all named controls ─────────────────────────────────────────────
    $script:Controls = @{}
    foreach ($name in @(
        'MainBorder','TitleBarBackground',
        'MinimizeBtn','MaximizeBtn','CloseBtn',
        'NavOperations','NavDashboard','NavStorage','NavLogs','NavSettings',
        'PageOperations','PageDashboard','PageStorage','PageLogs','PageSettings',
        'EnvironmentButton','EnvironmentStatusText',
        'RunButton','StopButton','OpenScriptsBtn','OpenLogBtn',
        'ActionCombo','DeployTargetCombo','MigrationModeCombo','TenantText',
        'RepoLocalText','RemoteRepoPathText','BranchText','ComposeFileText','GitTokenBox',
        'ServerHostText','ServerUserText','SshPortText','SshAuthCombo',
        'SshPasswordBox','RememberSshPasswordCheck','SshKeyPathText',
        'SshBatchModeCheck','InteractivePasswordCheck',
        'SkipPullCheck','SkipMigrationsCheck','SkipBuildCheck','SkipPublicChecksCheck',
        'CreateRestorePointCheck','RestartExplorerCheck','AdvancedToggleButton',
        'AdvancedGeneralPanel','AdvancedSshPanel',
        'RunProgressBar','StatusBadge','LogTextBox',
        'CpuText','MemoryText','DiskText','UptimeText',
        'OverallStatusText','DashboardSourceText','LastCheckedText',
        'SqlStatusText','ApiStatusText','FrontStatusText','RecentRunsGrid','RefreshDashboardBtn',
        'StorageStatusText','DiskUsedText','DiskFreeText','DiskPercentText',
        'DockerImagesGrid','TopDirectoriesGrid',
        'RefreshStorageBtn','PruneBuilderBtn','PruneImagesBtn','PruneAllBtn',
        'RemoteLogSourceCombo','RemoteLogTailText','RemoteLogTextBox',
        'LoadRemoteLogBtn','ClearLogBtn',
        'SettingsSummaryText','ReloadEnvBtn','SaveSettingsBtn'
    )) {
        $script:Controls[$name] = $script:Window.FindName($name)
    }

    # ── Session state ──────────────────────────────────────────────────────
    $script:RunningProcess  = $null
    $script:AdvancedVisible = $false
    $script:RunStartedAt    = $null
    $script:RecentRuns      = [System.Collections.Generic.List[PSObject]]::new()

    # ── Wire chrome, navigation ────────────────────────────────────────────
    Register-WindowChrome
    Register-Navigation

    # ── Load settings & env ───────────────────────────────────────────────
    $saved = Load-AppSettings
    if ($saved.Environment) { $script:AppState.CurrentEnvironment = $saved.Environment }
    Invoke-LoadEnv
    Update-EnvironmentVisuals
    Apply-AdvancedVisibility
    Apply-UiState

    # ── Toolbar buttons ────────────────────────────────────────────────────
    $script:Controls['RunButton'].Add_Click({ Invoke-RunAction })
    $script:Controls['StopButton'].Add_Click({ Invoke-StopAction })

    $script:Controls['OpenScriptsBtn'].Add_Click({
        $path = Join-Path $script:Controls['RepoLocalText'].Text 'scripts'
        if (Test-Path $path) { Start-Process $path }
        else { [System.Windows.MessageBox]::Show("No se encontro la carpeta: $path", 'VPS Desk') }
    })
    $script:Controls['OpenLogBtn'].Add_Click({
        $logPath = Join-Path $script:RootPath 'migrator_logs.txt'
        if (Test-Path $logPath) { Start-Process $logPath }
        else { [System.Windows.MessageBox]::Show('Todavia no hay log generado.', 'VPS Desk') }
    })

    $script:Controls['EnvironmentButton'].Add_Click({
        $script:AppState.CurrentEnvironment = Get-NextEnvironment $script:AppState.CurrentEnvironment
        Update-EnvironmentVisuals
        Save-AppSettings
        Write-AppLog "Entorno objetivo: $($script:AppState.CurrentEnvironment)" 'ENV'
        Invoke-BackgroundStatusRefresh
    })

    # ── Form state events ─────────────────────────────────────────────────
    $script:Controls['ActionCombo'].Add_SelectionChanged({ Apply-UiState })
    $script:Controls['MigrationModeCombo'].Add_SelectionChanged({ Apply-UiState })
    $script:Controls['SkipMigrationsCheck'].Add_Checked({ Apply-UiState })
    $script:Controls['SkipMigrationsCheck'].Add_Unchecked({ Apply-UiState })
    $script:Controls['SshAuthCombo'].Add_SelectionChanged({ Apply-UiState })
    $script:Controls['SshPasswordBox'].Add_PasswordChanged({ Apply-UiState })
    $script:Controls['ServerHostText'].Add_LostFocus({ Invoke-BackgroundStatusRefresh })
    $script:Controls['SshPortText'].Add_LostFocus({ Invoke-BackgroundStatusRefresh })

    $script:Controls['AdvancedToggleButton'].Add_Click({
        $script:AdvancedVisible = -not $script:AdvancedVisible
        Apply-AdvancedVisibility
    })

    # ── Dashboard ──────────────────────────────────────────────────────────
    $script:Controls['RefreshDashboardBtn'].Add_Click({ Invoke-DashboardRefresh })

    # ── Storage ────────────────────────────────────────────────────────────
    $script:Controls['RefreshStorageBtn'].Add_Click({ Invoke-StorageRefresh })
    $script:Controls['PruneBuilderBtn'].Add_Click({ Invoke-Prune 'builder' })
    $script:Controls['PruneImagesBtn'].Add_Click({  Invoke-Prune 'images' })
    $script:Controls['PruneAllBtn'].Add_Click({
        $r = [System.Windows.MessageBox]::Show(
            'Esto eliminara imagenes, contenedores y volumenes no usados. Continuar?',
            'Confirmacion', 'YesNo', 'Warning')
        if ($r -eq 'Yes') { Invoke-Prune 'system' }
    })

    # ── Log Center ─────────────────────────────────────────────────────────
    $script:Controls['LoadRemoteLogBtn'].Add_Click({ Invoke-LoadRemoteLog })
    $script:Controls['ClearLogBtn'].Add_Click({
        $script:Controls['RemoteLogTextBox'].Clear()
        $script:Controls['LogTextBox'].Clear()
    })

    # ── Settings ───────────────────────────────────────────────────────────
    $script:Controls['ReloadEnvBtn'].Add_Click({  Invoke-LoadEnv; Write-AppLog '.env recargado.' })
    $script:Controls['SaveSettingsBtn'].Add_Click({ Save-AppSettings; Update-SettingsSummary; Write-AppLog 'Settings guardados.' })

    # ── Status timer (every 30 s) ──────────────────────────────────────────
    $script:StatusTimer = [System.Windows.Threading.DispatcherTimer]::new()
    $script:StatusTimer.Interval = [TimeSpan]::FromSeconds(30)
    $script:StatusTimer.Add_Tick({ Invoke-BackgroundStatusRefresh })
    $script:StatusTimer.Start()

    $script:Window.Add_Closing({
        $script:StatusTimer.Stop()
        Save-AppSettings
    })

    # ── Show initial state ─────────────────────────────────────────────────
    Show-Page 'PageOperations'
    Write-AppLog 'Sistema listo. Configura SSH y ejecuta Ejecutar.'
    Invoke-BackgroundStatusRefresh

    $script:Window.ShowDialog() | Out-Null
}

# ═══════════════════════════════════════════════════════════════════════════
# HELPERS
# ═══════════════════════════════════════════════════════════════════════════

function Write-AppLog {
    param([string]$Message, [string]$Source = 'UI')
    $safe = Protect-LogLine (Remove-AnsiCodes $Message)
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $safe"
    $script:Controls['LogTextBox'].AppendText("$line`n")
    $script:Controls['LogTextBox'].ScrollToEnd()
    $logPath = Join-Path $script:RootPath 'migrator_logs.txt'
    try { Add-Content -Path $logPath -Value $line -Encoding UTF8 } catch {}
    Add-LogEntry -Source $Source -Message $safe
}

function Get-ControlText { param([string]$Name) $script:Controls[$Name].Text }
function Get-SshPort { $p = $script:Controls['SshPortText'].Text; if ($p -match '^\d+$') { [int]$p } else { 22 } }
function Get-Password { param([string]$Name) $script:Controls[$Name].Password }

function Get-ComboCode {
    param([string]$Name, [string]$Fallback = '')
    $item = $script:Controls[$Name].SelectedItem
    if (-not $item) { return $Fallback }
    $text = $item.Content
    $idx  = $text.IndexOf(' - ')
    if ($idx -gt 0) { return $text.Substring(0, $idx).Trim() }
    return $text.Trim()
}

function Get-EffectiveSshAuth {
    $mode = Get-ComboCode 'SshAuthCombo' 'Auto'
    if ($mode -eq 'Auto' -and (Get-Password 'SshPasswordBox')) { return 'Password' }
    return $mode
}

function Update-EnvironmentVisuals {
    $profile = Get-EnvProfile
    $script:Controls['EnvironmentButton'].Content    = $profile.RiskLabel
    $script:Controls['EnvironmentButton'].Background = $profile.RiskColor
}

function Apply-UiState {
    $action     = $script:Controls['ActionCombo'].SelectedItem?.Content
    $migMode    = Get-ComboCode 'MigrationModeCombo' 'B'
    $skipMig    = $action -eq 'Deploy completo' -and $script:Controls['SkipMigrationsCheck'].IsChecked
    $sshAuth    = Get-ComboCode 'SshAuthCombo' 'Auto'
    $effAuth    = Get-EffectiveSshAuth
    $isDeploy   = $action -eq 'Deploy completo'

    $script:Controls['DeployTargetCombo'].IsEnabled      = $isDeploy
    $script:Controls['MigrationModeCombo'].IsEnabled     = -not $skipMig
    $script:Controls['TenantText'].IsEnabled             = ($migMode -eq 'U') -and (-not $skipMig)
    $script:Controls['SkipPullCheck'].IsEnabled          = $isDeploy
    $script:Controls['SkipMigrationsCheck'].IsEnabled    = $isDeploy
    $script:Controls['SkipBuildCheck'].IsEnabled         = $isDeploy
    $script:Controls['SkipPublicChecksCheck'].IsEnabled  = $isDeploy
    $script:Controls['SshKeyPathText'].IsEnabled         = $sshAuth -ne 'Password'
    $script:Controls['SshBatchModeCheck'].IsEnabled      = $sshAuth -ne 'Password'
    $script:Controls['InteractivePasswordCheck'].IsEnabled = ($effAuth -eq 'Password') -and (-not (Get-Password 'SshPasswordBox'))
    if ($effAuth -ne 'Password') { $script:Controls['InteractivePasswordCheck'].IsChecked = $false }
}

function Apply-AdvancedVisibility {
    $v = if ($script:AdvancedVisible) { 'Visible' } else { 'Collapsed' }
    $script:Controls['AdvancedGeneralPanel'].Visibility = $v
    $script:Controls['AdvancedSshPanel'].Visibility     = $v
    $script:Controls['AdvancedToggleButton'].Content    = if ($script:AdvancedVisible) { '[ SYS.ADVANCED: ON ]' } else { '[ SYS.ADVANCED ]' }
}

function Update-SettingsSummary {
    $script:Controls['SettingsSummaryText'].Text =
        "Repo root   : $script:RootPath`n" +
        "Repo local  : $(Get-ControlText 'RepoLocalText')`n" +
        "Servidor    : $(Get-ControlText 'ServerHostText'):$(Get-SshPort)`n" +
        "Usuario     : $(Get-ControlText 'ServerUserText')`n" +
        "Entorno     : $($script:AppState.CurrentEnvironment)`n" +
        "Log local   : $(Join-Path $script:RootPath 'migrator_logs.txt')"
}

function Set-ServiceLabel {
    param([string]$Name, [string]$State)
    $ctrl = $script:Controls[$Name]
    switch ($State) {
        'running' { $ctrl.Text = 'Servicio activo';      $ctrl.Foreground = '#4ED68A' }
        'stopped' { $ctrl.Text = 'Servicio detenido';    $ctrl.Foreground = '#FF8181' }
        default   { $ctrl.Text = 'Estado no verificado'; $ctrl.Foreground = '#FFD166' }
    }
}

# ═══════════════════════════════════════════════════════════════════════════
# ENV LOADER
# ═══════════════════════════════════════════════════════════════════════════

function Invoke-LoadEnv {
    $envPath = Find-EnvFile
    if (-not $envPath) { Write-AppLog '.env no encontrado. Valores por defecto.'; Update-SettingsSummary; return }

    $env = Read-EnvFile $envPath
    if ($env['SERVER_HOST'])  { $script:Controls['ServerHostText'].Text  = $env['SERVER_HOST'] }
    if ($env['SERVER_USER'])  { $script:Controls['ServerUserText'].Text  = $env['SERVER_USER'] }
    if ($env['SSH_PORT'])     { $script:Controls['SshPortText'].Text     = $env['SSH_PORT'] }
    if ($env['REPO_LOCAL'])   { $script:Controls['RepoLocalText'].Text   = $env['REPO_LOCAL'] }
    if ($env['SSH_KEY_PATH']) { $script:Controls['SshKeyPathText'].Text  = $env['SSH_KEY_PATH'] }
    if ($env['GIT_TOKEN'])    { $script:Controls['GitTokenBox'].Password = $env['GIT_TOKEN'] }
    if ($env['SSH_PASSWORD'] -and -not $script:Controls['SshPasswordBox'].Password) {
        $script:Controls['SshPasswordBox'].Password = $env['SSH_PASSWORD']
    }
    Write-AppLog ".env cargado: $envPath"
    Update-SettingsSummary
}

# ═══════════════════════════════════════════════════════════════════════════
# STATUS REFRESH (background)
# ═══════════════════════════════════════════════════════════════════════════

function Invoke-BackgroundStatusRefresh {
    $host_    = $script:Controls['ServerHostText'].Text.Trim()
    $port     = Get-SshPort
    $profile  = Get-EnvProfile
    $dispatch = $script:Window.Dispatcher
    $statusTb = $script:Controls['EnvironmentStatusText']

    [System.Threading.Tasks.Task]::Run([Action]{
        $reachable = Test-HostReachable -SshHost $host_ -Port $port
        $label = if ($reachable) { "VPS: EN LINEA | $($profile.RiskLabel) | $(Get-Date -Format 'HH:mm:ss')" }
                 else            { "VPS: SIN CONEXION | $($profile.RiskLabel)" }
        $color = if ($reachable) { '#4ED68A' } else { '#FF8181' }
        $dispatch.Invoke([Action]{
            $statusTb.Text       = $label
            $statusTb.Foreground = $color
        })
    }) | Out-Null
}

# ═══════════════════════════════════════════════════════════════════════════
# RUN / STOP
# ═══════════════════════════════════════════════════════════════════════════

$script:ProgressSteps = @(
    @{ Pattern = '[INFO] Verificando docker compose'; Value = 5  }
    @{ Pattern = '[INFO] Actualizando codigo';        Value = 10 }
    @{ Pattern = '[INFO] Levantando target';          Value = 25 }
    @{ Pattern = '[INFO] Asegurando SQL Server';      Value = 70 }
    @{ Pattern = '[INFO] Ejecutando migraciones';     Value = 75 }
    @{ Pattern = '[INFO] Estado final de servicios';  Value = 88 }
    @{ Pattern = '[INFO] Smoke checks internos';      Value = 90 }
    @{ Pattern = '[OK] Deploy remoto completado';     Value = 95 }
    @{ Pattern = '[OK] Automatizacion finalizada';    Value = 100}
)

function Update-ProgressFromLog {
    param([string]$Line)
    $bar = $script:Controls['RunProgressBar']
    foreach ($step in $script:ProgressSteps) {
        if ($Line -like "*$($step.Pattern)*" -and $step.Value -gt $bar.Value) {
            $bar.Value = $step.Value; return
        }
    }
}

function Invoke-RunAction {
    if ($script:RunningProcess) { Write-AppLog 'Ya hay un proceso en ejecucion.'; return }

    # Validate
    $host_    = $script:Controls['ServerHostText'].Text.Trim()
    $user     = $script:Controls['ServerUserText'].Text.Trim()
    $repoRemote = $script:Controls['RemoteRepoPathText'].Text.Trim()
    $compose  = $script:Controls['ComposeFileText'].Text.Trim()
    $action   = $script:Controls['ActionCombo'].SelectedItem?.Content
    $migMode  = Get-ComboCode 'MigrationModeCombo' 'B'
    $branch   = $script:Controls['BranchText'].Text.Trim()

    if (-not $host_)    { [System.Windows.MessageBox]::Show('Servidor es requerido.',    'Error'); return }
    if (-not $user)     { [System.Windows.MessageBox]::Show('Usuario es requerido.',     'Error'); return }
    if (-not $repoRemote -and $script:AdvancedVisible) { [System.Windows.MessageBox]::Show('Repo remoto es requerido.', 'Error'); return }

    $guard = Test-ProductionGuard `
        -SkipPublicChecks ($script:Controls['SkipPublicChecksCheck'].IsChecked -eq $true) `
        -SkipMigrations   ($script:Controls['SkipMigrationsCheck'].IsChecked   -eq $true) `
        -Action $action
    if ($guard) { [System.Windows.MessageBox]::Show($guard, 'Restriccion de produccion'); return }

    $repoLocal = $script:Controls['RepoLocalText'].Text.Trim()
    $scriptFile = if ($action -eq 'Deploy completo') { 'deploy-hostinger.ps1' } else { 'migrate-hostinger-ui.ps1' }
    $scriptPath = Join-Path $repoLocal "scripts\$scriptFile"
    if (-not (Test-Path $scriptPath)) {
        [System.Windows.MessageBox]::Show("Script no encontrado:`n$scriptPath", 'Error'); return
    }

    # Build args
    $psArgs = [System.Collections.Generic.List[string]]::new()
    $psArgs.AddRange([string[]]@('-NoProfile','-ExecutionPolicy','Bypass','-File',$scriptPath,
        '-ServerHost',$host_,'-ServerUser',$user,
        '-SshPort',(Get-SshPort).ToString(),
        '-SshAuthMode',(Get-EffectiveSshAuth),
        '-MigrationMode',$migMode))

    if ($script:AdvancedVisible) {
        $psArgs.AddRange([string[]]@('-RemoteRepoPath',$repoRemote,'-ComposeFile',$compose))
    }
    $keyPath = $script:Controls['SshKeyPathText'].Text.Trim()
    if ($keyPath) { $psArgs.AddRange([string[]]@('-SshKeyPath',$keyPath)) }
    if ($script:Controls['SshBatchModeCheck'].IsChecked) { $psArgs.Add('-SshBatchMode') }

    $pwd = Get-Password 'SshPasswordBox'
    if ((Get-EffectiveSshAuth) -eq 'Password' -and $pwd) {
        $psArgs.AddRange([string[]]@('-SshPassword',$pwd))
    }
    if ($migMode -eq 'U') { $psArgs.AddRange([string[]]@('-TenantIdentifier',$script:Controls['TenantText'].Text.Trim())) }
    if ($action -eq 'Deploy completo') {
        $psArgs.AddRange([string[]]@('-Branch',$branch,'-DeployTarget',(Get-ComboCode 'DeployTargetCombo' 'Both')))
        $gt = Get-Password 'GitTokenBox'
        if ($gt) { $psArgs.AddRange([string[]]@('-GitToken',$gt)) }
        if ($script:Controls['SkipPullCheck'].IsChecked)         { $psArgs.Add('-SkipPull') }
        if ($script:Controls['SkipMigrationsCheck'].IsChecked)   { $psArgs.Add('-SkipMigrations') }
        if ($script:Controls['SkipBuildCheck'].IsChecked)        { $psArgs.Add('-SkipBuild') }
        if ($script:Controls['SkipPublicChecksCheck'].IsChecked) { $psArgs.Add('-SkipPublicChecks') }
    } else { $psArgs.Add('-NoUi') }

    # Kick off interactive terminal if needed
    if ((Get-EffectiveSshAuth) -eq 'Password' -and -not $pwd -and $script:Controls['InteractivePasswordCheck'].IsChecked) {
        $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes(
            "& pwsh $($psArgs -join ' '); Read-Host 'Proceso finalizado. Enter para cerrar'"))
        Start-Process pwsh "-NoExit -EncodedCommand $encoded"
        Write-AppLog 'Terminal interactiva abierta para ingresar password SSH.'
        return
    }

    # Start captured process
    $psi = [System.Diagnostics.ProcessStartInfo]::new('pwsh')
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.CreateNoWindow         = $true
    if ($pwd) { $psi.Environment['HOLOS_SSH_PASSWORD'] = $pwd }
    foreach ($a in $psArgs) { $psi.ArgumentList.Add($a) }

    $proc             = [System.Diagnostics.Process]::new()
    $proc.StartInfo   = $psi
    $proc.EnableRaisingEvents = $true
    $script:RunningProcess    = $proc
    $script:RunStartedAt      = [datetime]::Now

    $dispatch = $script:Window.Dispatcher
    $logBox   = $script:Controls['LogTextBox']

    $proc.add_OutputDataReceived({
        param($s, $e)
        if ($null -ne $e.Data) {
            $data = Remove-AnsiCodes $e.Data
            $dispatch.Invoke([Action]{
                $logBox.AppendText("$data`n")
                $logBox.ScrollToEnd()
                Update-ProgressFromLog $data
            })
        }
    }.GetNewClosure())

    $proc.add_ErrorDataReceived({
        param($s, $e)
        if ($null -ne $e.Data) {
            $clean = (Remove-AnsiCodes $e.Data).Trim()
            if ($clean) {
                $dispatch.Invoke([Action]{
                    $logBox.AppendText("ERR: $clean`n")
                    $logBox.ScrollToEnd()
                })
            }
        }
    }.GetNewClosure())

    $proc.add_Exited({
        $code = $proc.ExitCode
        $dispatch.Invoke([Action]{ Finish-RunAction -ExitCode $code })
    }.GetNewClosure())

    Write-AppLog "======== INICIO [$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] ========"
    $script:Controls['RunButton'].IsEnabled  = $false
    $script:Controls['StopButton'].IsEnabled = $true
    $script:Controls['RunProgressBar'].Value = 0
    $script:Controls['StatusBadge'].Text     = 'Ejecutando...'
    $script:Controls['StatusBadge'].Foreground = '#FFD166'

    $proc.Start() | Out-Null
    $proc.BeginOutputReadLine()
    $proc.BeginErrorReadLine()
}

function Finish-RunAction {
    param([int]$ExitCode)
    $ok = $ExitCode -eq 0
    $script:Controls['RunProgressBar'].Value   = if ($ok) { 100 } else { $script:Controls['RunProgressBar'].Value }
    $script:Controls['StatusBadge'].Text       = if ($ok) { 'Deploy completado correctamente.' } else { "Proceso termino con codigo $ExitCode." }
    $script:Controls['StatusBadge'].Foreground = if ($ok) { '#4ED68A' } else { '#FF8181' }
    $script:Controls['RunButton'].IsEnabled    = $true
    $script:Controls['StopButton'].IsEnabled   = $true
    $script:RunningProcess = $null
    Write-AppLog "======== FIN codigo=$ExitCode [$(Get-Date -Format 'HH:mm:ss')] ========"

    $script:RecentRuns.Insert(0, [PSCustomObject]@{
        Inicio    = $script:RunStartedAt.ToString('MM-dd HH:mm')
        Fin       = (Get-Date).ToString('MM-dd HH:mm')
        Duracion  = "$([Math]::Round(([datetime]::Now - $script:RunStartedAt).TotalMinutes, 1))m"
        Accion    = $script:Controls['ActionCombo'].SelectedItem?.Content
        Entorno   = $script:AppState.CurrentEnvironment
        Exit      = $ExitCode
        Resultado = if ($ok) { 'OK' } else { 'FAIL' }
    })
    Refresh-RecentRunsGrid
}

function Invoke-StopAction {
    if (-not $script:RunningProcess) { Write-AppLog 'No hay proceso en ejecucion.'; return }
    try   { $script:RunningProcess.Kill($true); Write-AppLog 'Proceso detenido por usuario.' }
    catch { Write-AppLog "No se pudo detener: $_" }
    finally {
        $script:RunningProcess               = $null
        $script:Controls['RunButton'].IsEnabled  = $true
        $script:Controls['StopButton'].IsEnabled = $true
    }
}

# ═══════════════════════════════════════════════════════════════════════════
# DASHBOARD
# ═══════════════════════════════════════════════════════════════════════════

function Invoke-DashboardRefresh {
    $host_ = $script:Controls['ServerHostText'].Text.Trim()
    $user  = $script:Controls['ServerUserText'].Text.Trim()
    $port  = Get-SshPort
    $key   = $script:Controls['SshKeyPathText'].Text.Trim()
    $dispatch = $script:Window.Dispatcher

    $script:Controls['OverallStatusText'].Text       = 'Actualizando...'
    $script:Controls['OverallStatusText'].Foreground = '#FFD166'

    [System.Threading.Tasks.Task]::Run([Action]{
        $reachable = Test-HostReachable -SshHost $host_ -Port $port
        $states  = $null
        $metrics = $null
        $canSsh  = $reachable -and $user -and $key

        if ($canSsh) {
            $states  = Get-DockerServiceStates -SshHost $host_ -User $user -Port $port -KeyPath $key
            $metrics = Get-ServerMetrics       -SshHost $host_ -User $user -Port $port -KeyPath $key
        }

        $dispatch.Invoke([Action]{
            # Metrics
            $script:Controls['CpuText'].Text    = if ($metrics?.cpu)    { $metrics.cpu }    else { if ($reachable) {'--'} else {'offline'} }
            $script:Controls['MemoryText'].Text  = if ($metrics?.memory) { $metrics.memory } else { '--' }
            $script:Controls['DiskText'].Text    = if ($metrics?.disk)   { $metrics.disk }   else { '--' }
            $script:Controls['UptimeText'].Text  = if ($metrics?.uptime) { $metrics.uptime } else { '--' }

            # Services
            foreach ($svc in @('sql','api','front')) {
                $state = if ($states -and $states.ContainsKey($svc)) { $states[$svc] } else { Get-ServiceState $svc }
                if ($states -and $states.ContainsKey($svc)) { Set-ServiceState $svc $state }
                $labelName = $svc.Substring(0,1).ToUpper() + $svc.Substring(1) + 'StatusText'
                Set-ServiceLabel $labelName $state
            }

            # Overall
            $active = @('sql','api','front') | Where-Object { (Get-ServiceState $_) -eq 'running' } | Measure-Object | Select-Object -ExpandProperty Count
            if (-not $reachable) {
                $script:Controls['OverallStatusText'].Text       = 'Estado VPS: SIN CONEXION SSH'
                $script:Controls['OverallStatusText'].Foreground = '#FF8181'
            } elseif ($active -eq 3) {
                $script:Controls['OverallStatusText'].Text       = 'Estado VPS: OPERATIVO'
                $script:Controls['OverallStatusText'].Foreground = '#4ED68A'
            } else {
                $script:Controls['OverallStatusText'].Text       = "Estado VPS: DEGRADADO ($active/3 servicios)"
                $script:Controls['OverallStatusText'].Foreground = '#FFD166'
            }

            $script:Controls['DashboardSourceText'].Text = if ($canSsh) { 'Fuente: SSH (docker ps + /proc + df)' } elseif ($reachable) { 'Fuente: TCP — configura key SSH para detalle' } else { 'Fuente: sin conexion' }
            $script:Controls['LastCheckedText'].Text     = "Ultima verificacion: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

            Refresh-RecentRunsGrid
        })
    }) | Out-Null
}

function Refresh-RecentRunsGrid {
    $script:Controls['RecentRunsGrid'].ItemsSource = $script:RecentRuns
}

# ═══════════════════════════════════════════════════════════════════════════
# STORAGE
# ═══════════════════════════════════════════════════════════════════════════

function Invoke-StorageRefresh {
    $host_ = $script:Controls['ServerHostText'].Text.Trim()
    $user  = $script:Controls['ServerUserText'].Text.Trim()
    $port  = Get-SshPort
    $key   = $script:Controls['SshKeyPathText'].Text.Trim()

    if (-not $host_ -or -not $user) {
        $script:Controls['StorageStatusText'].Text = 'Configura SERVER_HOST y SERVER_USER primero.'
        return
    }

    $script:Controls['StorageStatusText'].Text       = 'Consultando...'
    $script:Controls['StorageStatusText'].Foreground = '#78D6FF'
    $dispatch = $script:Window.Dispatcher

    [System.Threading.Tasks.Task]::Run([Action]{
        $raw = Get-RemoteStorageInfo -SshHost $host_ -User $user -Port $port -KeyPath $key
        $dispatch.Invoke([Action]{
            if (-not $raw) {
                $script:Controls['StorageStatusText'].Text       = 'No se pudo obtener datos del VPS.'
                $script:Controls['StorageStatusText'].Foreground = '#FF8181'
                return
            }
            Parse-StorageOutput $raw
            $script:Controls['StorageStatusText'].Text       = "Actualizado: $(Get-Date -Format 'HH:mm:ss')"
            $script:Controls['StorageStatusText'].Foreground = '#4ED68A'
        })
    }) | Out-Null
}

function Parse-StorageOutput {
    param([string]$Raw)
    $section    = ''
    $imageDt    = New-Object System.Data.DataTable
    $imageDt.Columns.AddRange([System.Data.DataColumn[]]@(
        [System.Data.DataColumn]::new('Imagen'),
        [System.Data.DataColumn]::new('Tamano'),
        [System.Data.DataColumn]::new('ID')))
    $dirDt = New-Object System.Data.DataTable
    $dirDt.Columns.AddRange([System.Data.DataColumn[]]@(
        [System.Data.DataColumn]::new('Tamano'),
        [System.Data.DataColumn]::new('Directorio')))

    foreach ($rawLine in $Raw -split "`n") {
        $line = $rawLine.Trim()
        if ($line -match '^\[.+\]$') { $section = $line; continue }
        switch ($section) {
            '[DISK]' {
                $parts = $line -split '\s+' | Where-Object { $_ }
                if ($parts.Count -ge 5 -and $parts[0] -ne 'Filesystem') {
                    $script:Controls['DiskUsedText'].Text    = $parts[2]
                    $script:Controls['DiskFreeText'].Text    = $parts[3]
                    $script:Controls['DiskPercentText'].Text = $parts[4]
                }
            }
            '[DOCKER_IMAGES]' {
                $parts = $line -split "`t"
                if ($parts.Count -ge 3) { $imageDt.Rows.Add($parts[0], $parts[1], $parts[2]) | Out-Null }
            }
            '[TOP_DIRS]' {
                $parts = $line -split "`t"
                if ($parts.Count -ge 2) { $dirDt.Rows.Add($parts[0], $parts[1]) | Out-Null }
            }
        }
    }
    $script:Controls['DockerImagesGrid'].ItemsSource   = $imageDt.DefaultView
    $script:Controls['TopDirectoriesGrid'].ItemsSource = $dirDt.DefaultView
}

function Invoke-Prune {
    param([string]$Target)
    $host_ = $script:Controls['ServerHostText'].Text.Trim()
    $user  = $script:Controls['ServerUserText'].Text.Trim()
    $port  = Get-SshPort
    $key   = $script:Controls['SshKeyPathText'].Text.Trim()
    $script:Controls['StorageStatusText'].Text       = "Limpiando $Target..."
    $script:Controls['StorageStatusText'].Foreground = '#FFD166'
    $dispatch = $script:Window.Dispatcher

    [System.Threading.Tasks.Task]::Run([Action]{
        $result = Invoke-DockerPrune -SshHost $host_ -User $user -Port $port -KeyPath $key -Target $Target
        $dispatch.Invoke([Action]{
            if ($result) { [System.Windows.MessageBox]::Show($result, 'Resultado prune') }
            Invoke-StorageRefresh
        })
    }) | Out-Null
}

# ═══════════════════════════════════════════════════════════════════════════
# LOG CENTER
# ═══════════════════════════════════════════════════════════════════════════

function Invoke-LoadRemoteLog {
    $host_ = $script:Controls['ServerHostText'].Text.Trim()
    $user  = $script:Controls['ServerUserText'].Text.Trim()
    $port  = Get-SshPort
    $key   = $script:Controls['SshKeyPathText'].Text.Trim()
    if (-not $host_ -or -not $user) {
        [System.Windows.MessageBox]::Show('Configura SERVER_HOST y SERVER_USER.', 'Faltan datos')
        return
    }
    $source = $script:Controls['RemoteLogSourceCombo'].SelectedItem?.Content ?? 'migrator'
    $tail   = if ($script:Controls['RemoteLogTailText'].Text -match '^\d+$') { [int]$script:Controls['RemoteLogTailText'].Text } else { 300 }

    $script:Controls['RemoteLogTextBox'].Text = 'Cargando log remoto...'
    $dispatch = $script:Window.Dispatcher
    $logBox   = $script:Controls['RemoteLogTextBox']

    [System.Threading.Tasks.Task]::Run([Action]{
        $log = Get-RemoteLog -SshHost $host_ -User $user -Port $port -KeyPath $key -Source $source -Tail $tail
        $dispatch.Invoke([Action]{ $logBox.Text = if ($log) { $log } else { 'No se pudo obtener el log remoto.' } })
    }) | Out-Null
}

