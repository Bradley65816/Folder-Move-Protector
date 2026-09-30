#Requires -RunAsAdministrator
<#
    Installs Folder Move Protector:
      1. Copies the built files to C:\Program Files\Folder Move Protector
      2. Registers the hook DLL as a COM server, from its installed location
      3. Adds it as a folder copy-hook handler
      4. Creates a Start Menu shortcut (visible to every user) for the Settings tool

    Run this AFTER building BOTH projects in Release / x64.
#>
param(
    [string]$SettingsBuildOutput = (Join-Path $PSScriptRoot "Settings\bin\x64\Release\net48"),
    [string]$InstallDir = "C:\Program Files\Folder Move Protector"
)

$ErrorActionPreference = "Stop"

# Must match the [Guid("...")] attribute on FolderMoveHook in Hook\FolderMoveHook.cs
$Clsid = "{678B7C88-AFC9-43A9-83D2-EF0CC0855E58}"
$HandlerName = "Folder Move Protector"

if (-not (Test-Path $SettingsBuildOutput)) {
    throw "Could not find $SettingsBuildOutput. Build the Settings project (Release, x64) first."
}

Write-Host "Installing to $InstallDir ..." -ForegroundColor Cyan
New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null

# Settings' build output already includes FolderMoveProtector.Hook.dll - MSBuild
# copies referenced project DLLs automatically - so copying that whole folder
# gives us everything in one place.
Copy-Item -Path (Join-Path $SettingsBuildOutput "*") -Destination $InstallDir -Recurse -Force

$InstalledHookDll = Join-Path $InstallDir "FolderMoveProtector.Hook.dll"
if (-not (Test-Path $InstalledHookDll)) {
    throw "FolderMoveProtector.Hook.dll wasn't found in $InstallDir after copying. Build the Hook project too."
}

$RegAsm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path $RegAsm)) {
    throw "Could not find 64-bit RegAsm.exe at $RegAsm."
}

Write-Host "Registering the copy-hook COM server..." -ForegroundColor Cyan
# Register from the INSTALLED path, not the build folder, so the registry's
# CodeBase entry points somewhere permanent.
& $RegAsm $InstalledHookDll /codebase
if ($LASTEXITCODE -ne 0) { throw "RegAsm failed with exit code $LASTEXITCODE" }

Write-Host "Adding folder copy-hook registration..." -ForegroundColor Cyan
$HookKeyPath = "HKLM:\SOFTWARE\Classes\Directory\shellex\CopyHookHandlers\$HandlerName"
New-Item -Path $HookKeyPath -Force | Out-Null
Set-ItemProperty -Path $HookKeyPath -Name "(default)" -Value $Clsid

Write-Host "Creating Start Menu shortcut for all users..." -ForegroundColor Cyan
$StartMenuDir = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
$ShortcutPath = Join-Path $StartMenuDir "Folder Move Protector.lnk"
$WScriptShell = New-Object -ComObject WScript.Shell
$Shortcut = $WScriptShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = Join-Path $InstallDir "FolderMoveProtector.Settings.exe"
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "Configure which folders prompt for confirmation before being moved"
$Shortcut.Save()

Write-Host ""
Write-Host "Installed." -ForegroundColor Green
Write-Host "Restart Explorer for the copy-hook to take effect:"
Write-Host "  Stop-Process -Name explorer -Force; Start-Process explorer"
Write-Host "Find 'Folder Move Protector' in the Start Menu to add protected folders."
