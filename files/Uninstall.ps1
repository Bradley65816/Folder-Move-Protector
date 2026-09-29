#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "C:\Program Files\Folder Move Protector",
    [switch]$RemoveConfiguration
)

$HandlerName = "Folder Move Protector"
$HookKeyPath = "HKLM:\SOFTWARE\Classes\Directory\shellex\CopyHookHandlers\$HandlerName"
$InstalledHookDll = Join-Path $InstallDir "FolderMoveProtector.Hook.dll"

if (Test-Path $HookKeyPath) {
    Write-Host "Removing copy-hook registration..." -ForegroundColor Cyan
    Remove-Item -Path $HookKeyPath -Force
}

$RegAsm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if ((Test-Path $RegAsm) -and (Test-Path $InstalledHookDll)) {
    Write-Host "Unregistering COM server..." -ForegroundColor Cyan
    & $RegAsm $InstalledHookDll /unregister
}

$ShortcutPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Folder Move Protector.lnk"
if (Test-Path $ShortcutPath) {
    Remove-Item $ShortcutPath -Force
}

if (Test-Path $InstallDir) {
    Write-Host "Removing installed files..." -ForegroundColor Cyan
    Remove-Item -Path $InstallDir -Recurse -Force
}

if ($RemoveConfiguration) {
    Write-Host "Removing saved protected-folder list..." -ForegroundColor Cyan
    Remove-Item -Path "HKLM:\SOFTWARE\Folder Move Protector" -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Uninstalled. Restart Explorer:" -ForegroundColor Green
Write-Host "  Stop-Process -Name explorer -Force; Start-Process explorer"
