<#
    Run this on your main computer AFTER building all three projects
    (Hook, Settings, Setup) in Release / x64.

    Gathers just the files a destination machine actually needs - Setup.exe,
    plus the Hook DLL and Settings EXE it installs - into .\dist. Zip that
    folder (or just copy it as-is) and send it to the test VM. Nothing else
    in this repo (source code, other build artifacts) needs to go along.
#>
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"

$SettingsBuildOutput = Join-Path $PSScriptRoot "Settings\bin\x64\Release\net48"
$SetupBuildOutput = Join-Path $PSScriptRoot "Setup\bin\x64\Release\net48"

foreach ($path in @($SettingsBuildOutput, $SetupBuildOutput)) {
    if (-not (Test-Path $path)) {
        throw "Could not find $path. Build all three projects (Release, x64) first."
    }
}

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -Path $OutputDir -ItemType Directory | Out-Null

# Settings' output already includes a copy of FolderMoveProtector.Hook.dll
# (MSBuild copies referenced project DLLs automatically), so both files this
# needs come from the same folder.
Copy-Item -Path (Join-Path $SettingsBuildOutput "FolderMoveProtector.Hook.dll") -Destination $OutputDir
Copy-Item -Path (Join-Path $SettingsBuildOutput "FolderMoveProtector.Settings.exe") -Destination $OutputDir
Copy-Item -Path (Join-Path $SetupBuildOutput "Setup.exe") -Destination $OutputDir

Write-Host "Packaged to $OutputDir :" -ForegroundColor Green
Get-ChildItem $OutputDir | Select-Object Name, Length | Format-Table -AutoSize
Write-Host "Zip this folder (or copy it as-is) to the test machine, then run Setup.exe there."
