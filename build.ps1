param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\RapidShot\RapidShot.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\RapidShot.Tests\RapidShot.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Проверки не прошли' }
dotnet run --project (Join-Path $PSScriptRoot 'tests\RapidShot.Tests\RapidShot.Tests.csproj') -c Release --no-build -- --render
if ($LASTEXITCODE -ne 0) { throw 'Проверки интерфейса не прошли' }
if ($Portable) {
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $PSScriptRoot 'dist\RapidShot')
} else {
    dotnet publish $project -c Release --no-self-contained -o (Join-Path $PSScriptRoot 'dist\RapidShot')
}
if ($LASTEXITCODE -ne 0) { throw 'Публикация не удалась' }
