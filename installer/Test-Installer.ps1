param([Parameter(Mandatory)][string]$SetupPath)
$ErrorActionPreference = 'Stop'
$rapidRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rapidSetup = (Resolve-Path -LiteralPath $SetupPath).Path
$rapidTestRoot = Join-Path $rapidRoot ('artifacts\installer-smoke-' + [Guid]::NewGuid().ToString('N'))
$rapidInstallDir = [IO.Path]::GetFullPath((Join-Path $rapidTestRoot 'App'))
if (-not $rapidInstallDir.StartsWith($rapidRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Тестовая папка находится вне проекта' }
$rapidRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{3BEF2EBC-4C44-4CFB-9E5D-0E33A9FA118E}_is1'
if (Test-Path -LiteralPath $rapidRegistry) { throw 'RapidShot уже установлен. Этот тест не изменяет существующую установку.' }
if (Get-Process RapidShot -ErrorAction SilentlyContinue) { throw 'Сначала закройте RapidShot через меню трея.' }
New-Item -ItemType Directory -Path $rapidTestRoot -Force | Out-Null
$rapidSettings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'RapidShot\settings.json'
$rapidSettingsHash = if (Test-Path -LiteralPath $rapidSettings) { (Get-FileHash -LiteralPath $rapidSettings -Algorithm SHA256).Hash } else { $null }
$rapidAppProcess = $null
function Assert-RapidInstaller([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; Write-Output ('PASS ' + $Message) }
function Invoke-RapidSetup([string]$Name) {
    $rapidArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOICONS', '/TASKS=!desktopicon', ('/DIR="' + $rapidInstallDir + '"'), ('/LOG="' + (Join-Path $rapidTestRoot ($Name + '.log')) + '"'))
    $rapidProcess = Start-Process -FilePath $rapidSetup -ArgumentList $rapidArguments -WindowStyle Hidden -Wait -PassThru
    return $rapidProcess.ExitCode
}
try {
    Assert-RapidInstaller ((Invoke-RapidSetup 'install') -eq 0) 'Тихая установка завершается успешно без повышения прав'
    $rapidExe = Join-Path $rapidInstallDir 'RapidShot.exe'
    $rapidUninstaller = Join-Path $rapidInstallDir 'unins000.exe'
    Assert-RapidInstaller ((Test-Path -LiteralPath $rapidExe) -and (Test-Path -LiteralPath $rapidUninstaller)) 'Установлены программа и средство удаления'
    $rapidVersion = (Get-Item -LiteralPath $rapidExe).VersionInfo.ProductVersion.Split('+')[0]
    Assert-RapidInstaller ((Get-ItemProperty -LiteralPath $rapidRegistry).DisplayVersion -eq $rapidVersion) 'Программа зарегистрирована в списке установленных приложений'
    $rapidCreated = $false
    $rapidMutex = [Threading.Mutex]::new($true, 'Local\RapidShot.Desktop.v1', [ref]$rapidCreated)
    try { Assert-RapidInstaller ((Invoke-RapidSetup 'blocked-running') -ne 0) 'Обновление блокируется, пока приложение работает' }
    finally { if ($rapidCreated) { $rapidMutex.ReleaseMutex() }; $rapidMutex.Dispose() }
    Assert-RapidInstaller ((Invoke-RapidSetup 'reinstall') -eq 0) 'Повторная установка поверх существующей завершается успешно'
    $rapidAppProcess = Start-Process -FilePath $rapidExe -WorkingDirectory $rapidInstallDir -WindowStyle Hidden -PassThru
    $rapidStarted = $false
    for ($rapidAttempt = 0; $rapidAttempt -lt 50; $rapidAttempt++) {
        $rapidLiveMutex = $null
        if ([Threading.Mutex]::TryOpenExisting('Local\RapidShot.Desktop.v1', [ref]$rapidLiveMutex)) { $rapidLiveMutex.Dispose(); $rapidStarted = $true; break }
        if ($rapidAppProcess.HasExited) { break }
        Start-Sleep -Milliseconds 100
    }
    Assert-RapidInstaller $rapidStarted 'Установленный EXE запускается с включённым .NET'
    $rapidAppProcess.Refresh()
    Assert-RapidInstaller ($rapidAppProcess.MainWindowHandle -eq 0) 'Обычный запуск остаётся в трее без редактора'
    $rapidAppProcess | Stop-Process; $rapidAppProcess.WaitForExit(); $rapidAppProcess = $null
    $rapidUninstallArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $rapidTestRoot 'uninstall.log') + '"'))
    $rapidUninstallProcess = Start-Process -FilePath $rapidUninstaller -ArgumentList $rapidUninstallArgs -WindowStyle Hidden -Wait -PassThru
    Assert-RapidInstaller ($rapidUninstallProcess.ExitCode -eq 0 -and -not (Test-Path -LiteralPath $rapidExe) -and -not (Test-Path -LiteralPath $rapidRegistry)) 'Удаление убирает программу и регистрационную запись'
    $rapidAfterHash = if (Test-Path -LiteralPath $rapidSettings) { (Get-FileHash -LiteralPath $rapidSettings -Algorithm SHA256).Hash } else { $null }
    Assert-RapidInstaller ($rapidSettingsHash -eq $rapidAfterHash) 'Пользовательские настройки сохранены без изменений'
    Write-Output ('Логи проверки: ' + $rapidTestRoot)
}
finally {
    if ($rapidAppProcess -and -not $rapidAppProcess.HasExited) { $rapidAppProcess | Stop-Process; $rapidAppProcess.WaitForExit() }
    $rapidUninstaller = Join-Path $rapidInstallDir 'unins000.exe'
    if (Test-Path -LiteralPath $rapidUninstaller) { Start-Process -FilePath $rapidUninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait }
}
