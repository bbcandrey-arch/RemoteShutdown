# Пересобирает distrib/windows-agent/ (dotnet publish, self-contained single-file)
# и упаковывает его в инсталлятор через Inno Setup (ISCC.exe). Останавливает
# запущенные Tray/Service перед публикацией — иначе publish падает с "файл занят".
#
# v2: публикует Service.exe (постоянно запущенный Windows Service — см.
# docs/roadmap.md, "Windows Service") вместо Api.exe. Если служба
# RemoteShutdownAgent уже установлена и запущена, для её остановки (sc stop)
# нужны права администратора — запускайте этот скрипт из elevated PowerShell,
# если служба уже стоит.
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

Write-Host "Останавливаю службу агента, если установлена и запущена..." -ForegroundColor Cyan
try { sc.exe stop RemoteShutdownAgent | Out-Null } catch { }

Write-Host "Останавливаю запущенные Tray/Service (если есть)..." -ForegroundColor Cyan
Get-Process -Name "RemoteShutdown.Agent.*" -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
Start-Sleep -Milliseconds 1000

# distrib/windows-agent/ копится между запусками (dotnet publish не чистит чужие файлы) —
# без очистки сюда мог попасть, например, устаревший Api.exe от публикации до v2
# (Api.exe больше не входит в дистрибутив, это dev-only exe, см. AgentHost.cs).
if (Test-Path $distribDir) { Remove-Item -Path "$distribDir\*" -Recurse -Force }

Write-Host "Публикую RemoteShutdown.Agent.Service (self-contained, single-file, win-x64)..." -ForegroundColor Cyan
dotnet publish "$agentDir\src\RemoteShutdown.Agent.Service\RemoteShutdown.Agent.Service.csproj" `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $distribDir
if ($LASTEXITCODE -ne 0) { throw "publish Service завершился с ошибкой" }

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

