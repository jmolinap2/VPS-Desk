function Register-WindowChrome {
    $script:Controls['MinimizeBtn'].Add_Click({ $script:Window.WindowState = 'Minimized' })

    $script:Controls['MaximizeBtn'].Add_Click({
        $script:Window.WindowState = if ($script:Window.WindowState -eq 'Maximized') { 'Normal' } else { 'Maximized' }
    })

    $script:Controls['CloseBtn'].Add_Click({ $script:Window.Close() })

    $script:Window.Add_StateChanged({ Update-ChromeForState })
}

function Update-ChromeForState {
    $maximized = $script:Window.WindowState -eq 'Maximized'

    $script:Controls['MaximizeBtn'].Content = if ($maximized) { [char]0xE923 } else { [char]0xE922 }
    $script:Controls['MaximizeBtn'].ToolTip  = if ($maximized) { 'Restaurar' } else { 'Maximizar' }

    $script:Controls['MainBorder'].CornerRadius       = if ($maximized) { [System.Windows.CornerRadius]::new(0) } else { [System.Windows.CornerRadius]::new(8) }
    $script:Controls['TitleBarBackground'].CornerRadius = if ($maximized) { [System.Windows.CornerRadius]::new(0) } else { [System.Windows.CornerRadius]::new(8,8,0,0) }
}
