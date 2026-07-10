param(
    [ValidateSet('install','uninstall','start','stop','restart','pause','resume','status','reconnect','show')]
    [string]$Action = 'status'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Exe = Join-Path $Root 'BS2Lite.exe'
$TaskName = 'FlydigiBS2LiteAutoStart'
$OriginalTask = 'FlydigiSpaceStationAutoStart'
$OriginalSetting = 'C:\Program Files\FlydigiSpaceStation\config\Setting.ini'
$StatusPath = Join-Path $Root 'status.json'
$ShortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'BS2 Lite 控制面板.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'BS2 Lite 控制面板.lnk')
)

function Install-Shortcuts {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($path in $ShortcutPaths) {
        $shortcut = $shell.CreateShortcut($path)
        $shortcut.TargetPath = $Exe
        $shortcut.Arguments = '--show'
        $shortcut.WorkingDirectory = $Root
        $shortcut.IconLocation = "$Exe,0"
        $shortcut.Description = '打开 BS2 Lite 散热底座控制面板'
        $shortcut.Save()
    }
}

function Test-Administrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-Elevated {
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $Action"
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait
}

function Invoke-BS2Lite([string]$Argument) {
    if (-not (Test-Path $Exe)) { throw "未找到 $Exe" }
    & $Exe $Argument
}

function Start-BS2Lite {
    $running = Get-CimInstance Win32_Process -Filter "Name='BS2Lite.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -eq $Exe }
    if (-not $running) {
        Start-Process -FilePath $Exe -WorkingDirectory $Root
        Start-Sleep -Milliseconds 800
    }
}

if ($Action -in @('install','uninstall') -and -not (Test-Administrator)) {
    Restart-Elevated
    exit
}

switch ($Action) {
    'install' {
        if (-not (Test-Path $Exe)) { throw "未找到 $Exe" }
        $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
        $taskAction = New-ScheduledTaskAction -Execute $Exe -WorkingDirectory $Root
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $settings = New-ScheduledTaskSettingsSet -Hidden -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1)
        Register-ScheduledTask -TaskName $TaskName -Action $taskAction -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

        $old = Get-ScheduledTask -TaskName $OriginalTask -ErrorAction SilentlyContinue
        if ($old) { Disable-ScheduledTask -TaskName $OriginalTask | Out-Null }
        if (Test-Path $OriginalSetting) {
            $text = [IO.File]::ReadAllText($OriginalSetting)
            $text = [regex]::Replace($text, '(?m)^IsAutoStart=.*$', 'IsAutoStart=False')
            [IO.File]::WriteAllText($OriginalSetting, $text, [Text.UTF8Encoding]::new($false))
        }

        Invoke-BS2Lite '--stop' 2>$null
        Start-Sleep -Seconds 1
        Get-Process FlydigiSpaceStation -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-ScheduledTask -TaskName $TaskName
        Install-Shortcuts
        Write-Host '已安装：BS2 Lite 将随登录静默启动，桌面与托盘使用统一图标。'
    }
    'uninstall' {
        Invoke-BS2Lite '--stop' 2>$null
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        $old = Get-ScheduledTask -TaskName $OriginalTask -ErrorAction SilentlyContinue
        if ($old) { Enable-ScheduledTask -TaskName $OriginalTask | Out-Null }
        if (Test-Path $OriginalSetting) {
            $text = [IO.File]::ReadAllText($OriginalSetting)
            $text = [regex]::Replace($text, '(?m)^IsAutoStart=.*$', 'IsAutoStart=True')
            [IO.File]::WriteAllText($OriginalSetting, $text, [Text.UTF8Encoding]::new($false))
        }
        foreach ($path in $ShortcutPaths) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
        Write-Host '已停用 BS2 Lite，并恢复飞智空间站自启；程序文件和配置未删除。'
    }
    'start' { Start-BS2Lite }
    'stop' { Invoke-BS2Lite '--stop' }
    'restart' { Invoke-BS2Lite '--stop' 2>$null; Start-Sleep -Seconds 1; Start-BS2Lite }
    'pause' { Invoke-BS2Lite '--pause' }
    'resume' { Start-BS2Lite; Invoke-BS2Lite '--resume' 2>$null }
    'reconnect' { Invoke-BS2Lite '--reconnect' }
    'show' { Start-BS2Lite; Invoke-BS2Lite '--show' }
    'status' {
        if (Test-Path $StatusPath) { Get-Content -LiteralPath $StatusPath -Raw -Encoding UTF8 }
        else { Write-Host '尚未生成状态文件。' }
    }
}
