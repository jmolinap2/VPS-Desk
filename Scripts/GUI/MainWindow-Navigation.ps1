function Register-Navigation {
    $navMap = @{
        NavOperations = 'PageOperations'
        NavDashboard  = 'PageDashboard'
        NavStorage    = 'PageStorage'
        NavLogs       = 'PageLogs'
        NavSettings   = 'PageSettings'
    }

    foreach ($btnName in $navMap.Keys) {
        $pageName = $navMap[$btnName]
        $script:Controls[$btnName].Add_Click({
            Show-Page -PageName $pageName
        }.GetNewClosure())
    }
}

function Show-Page {
    param([string]$PageName)
    $pages = @('PageOperations','PageDashboard','PageStorage','PageLogs','PageSettings')
    $navs  = @('NavOperations','NavDashboard','NavStorage','NavLogs','NavSettings')
    $pageToNav = @{
        PageOperations = 'NavOperations'
        PageDashboard  = 'NavDashboard'
        PageStorage    = 'NavStorage'
        PageLogs       = 'NavLogs'
        PageSettings   = 'NavSettings'
    }

    foreach ($p in $pages) {
        $script:Controls[$p].Visibility = if ($p -eq $PageName) { 'Visible' } else { 'Collapsed' }
    }
    foreach ($n in $navs) {
        $active = ($n -eq $pageToNav[$PageName])
        $script:Controls[$n].Background = if ($active) { '#2A2A2A' } else { 'Transparent' }
        $script:Controls[$n].Foreground = if ($active) { '#F4F4F4' } else { '#A0A0A0' }
    }

    switch ($PageName) {
        'PageDashboard' { Invoke-DashboardRefresh }
        'PageStorage'   { Invoke-StorageRefresh }
    }
}
