param()

$script:RootPath = $PSScriptRoot

# Core
. "$script:RootPath\Scripts\Core\AppState.ps1"
. "$script:RootPath\Scripts\Core\EnvironmentPolicy.ps1"
. "$script:RootPath\Scripts\Core\HealthCheck.ps1"
. "$script:RootPath\Scripts\Core\LogSecurity.ps1"
. "$script:RootPath\Scripts\Core\EnvLoader.ps1"

# GUI
. "$script:RootPath\Scripts\GUI\MainWindow-WindowChrome.ps1"
. "$script:RootPath\Scripts\GUI\MainWindow-Navigation.ps1"
. "$script:RootPath\Scripts\GUI\Show-MainWindow.ps1"

Show-MainWindow
