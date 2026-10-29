param([switch]$Portable, [switch]$Installer, [string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\RapidShot\RapidShot.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\RapidShot.Tests\RapidShot.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Проверки не прошли' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\RapidShot.Tests\RapidShot.Tests.csproj') -c Release --no-build -- --render
if ($LASTEXITCODE -ne 0) { throw 'Проверки интерфейса не прошли' }
if ($Portable -or $Installer) {
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $PSScriptRoot 'dist\RapidShot')
} else {
    dotnet publish $project -c Release --no-self-contained -o (Join-Path $PSScriptRoot 'dist\RapidShot')
}
if ($LASTEXITCODE -ne 0) { throw 'Публикация не удалась' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'dist\RapidShot\README.md') -Force
if ($Installer) {
    if (-not $InnoCompiler) {
        $rapidCompilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $rapidCompilerCandidates = @(
            $(if ($rapidCompilerCommand) { $rapidCompilerCommand.Source }),
            (Join-Path $PSScriptRoot 'artifacts\tools\InnoSetup6\ISCC.exe'),
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 7\ISCC.exe'
        )
        $InnoCompiler = $rapidCompilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    }
    if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Не найден Inno Setup. Установите Inno Setup 6/7 или укажите -InnoCompiler с путём к ISCC.exe.' }
    [xml]$rapidProjectXml = Get-Content -LiteralPath $project
    $rapidVersion = $rapidProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
    $rapidSource = Join-Path $PSScriptRoot 'dist\RapidShot'
    $rapidOutput = Join-Path $PSScriptRoot 'dist\Installer'
    & $InnoCompiler "/DAppVersion=$rapidVersion" "/DSourceDir=$rapidSource" "/DOutputDir=$rapidOutput" (Join-Path $PSScriptRoot 'installer\RapidShot.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика не удалась' }
}
