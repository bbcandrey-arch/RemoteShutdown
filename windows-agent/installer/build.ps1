# Пересобирает distrib/windows-agent/ (dotnet publish, self-contained single-file)
# и упаковывает его в инсталлятор через Inno Setup (ISCC.exe). Останавливает
# запущенный агент перед публикацией — иначе publish падает с "файл занят".
#
# Использование: из PowerShell, из любой директории —
#   pwsh windows-agent/installer/build.ps1
#
# ISCC.exe ищется по стандартным путям Inno Setup 6; если стоит в нестандартном
# месте — передайте свой путь: -IsccPath "C:\путь\до\ISCC.exe"

param(
    [string]$IsccPath = $null
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
$agentDir = Join-Path $root "windows-agent"
$distribDir = Join-Path $root "distrib\windows-agent"

Write-Host "Останавливаю запущенный агент (если есть)..." -ForegroundColor Cyan
Get-Process -Name "RemoteShutdown.Agent.*" -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Milliseconds 500

Write-Host "Публикую RemoteShutdown.Agent.Api (self-contained, single-file, win-x64)..." -ForegroundColor Cyan
dotnet publish "$agentDir\src\RemoteShutdown.Agent.Api\RemoteShutdown.Agent.Api.csproj" `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $distribDir
if ($LASTEXITCODE -ne 0) { throw "publish Api завершился с ошибкой" }

Write-Host "Публикую RemoteShutdown.Agent.Tray (self-contained, single-file, win-x64)..." -ForegroundColor Cyan
dotnet publish "$agentDir\src\RemoteShutdown.Agent.Tray\RemoteShutdown.Agent.Tray.csproj" `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $distribDir
if ($LASTEXITCODE -ne 0) { throw "publish Tray завершился с ошибкой" }

if (-not $IsccPath) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw "ISCC.exe не найден. Установите Inno Setup (winget install JRSoftware.InnoSetup) или передайте -IsccPath."
}

Write-Host "Компилирую инсталлятор ($IsccPath)..." -ForegroundColor Cyan
& $IsccPath "$agentDir\installer\setup.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с ошибкой" }

Write-Host "Готово: distrib\installer\" -ForegroundColor Green

