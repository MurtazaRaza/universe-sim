<#
.SYNOPSIS
Builds the native core library (universesim.dll, Release, x64) and copies it into the Unity project.

.DESCRIPTION
The Windows counterpart of build-core-for-unity.sh (which calls this script when run from Git Bash):

    powershell -ExecutionPolicy Bypass -File tools/scripts/build-core-for-unity.ps1

It enters the Visual Studio developer shell (enter-vs-dev-shell.ps1), builds the unity-windows CMake preset, checks
that the DLL is x64 (the Unity 6000.5 Windows Editor and player are 64-bit), and copies it to
UniverseSim/Assets/UniverseSim/Plugins/Windows/x86_64/. The plugin binary is a build artifact and is not committed
(docs/adr/0002). Its import settings are applied by
UniverseSim/Assets/UniverseSim/Editor/NativePluginImportSettings.cs.

The Unity Editor keeps native plugins loaded once they are used, and Windows locks a loaded DLL: close the Editor
before running this script, or the copy fails.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$coreDir = Join-Path $repoRoot 'core'
$preset = 'unity-windows'
$library = 'universesim.dll'
$destDir = Join-Path $repoRoot 'UniverseSim\Assets\UniverseSim\Plugins\Windows\x86_64'

. (Join-Path $PSScriptRoot 'enter-vs-dev-shell.ps1')

Push-Location $coreDir
try {
    cmake --preset $preset
    if ($LASTEXITCODE -ne 0) { throw "cmake --preset $preset failed (exit code $LASTEXITCODE)" }
    cmake --build --preset $preset
    if ($LASTEXITCODE -ne 0) { throw "cmake --build --preset $preset failed (exit code $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

$built = Join-Path $coreDir "build\$preset\capi\$library"

# The PE header names the target machine (IMAGE_FILE_MACHINE_AMD64 = 0x8664).
$bytes = [System.IO.File]::ReadAllBytes($built)
$peOffset = [System.BitConverter]::ToInt32($bytes, 0x3C)
$machine = [System.BitConverter]::ToUInt16($bytes, $peOffset + 4)
if ($machine -ne 0x8664) {
    throw ("$library is not an x64 binary (PE machine 0x{0:X4}); run from an x64 developer shell." -f $machine)
}

New-Item -ItemType Directory -Force $destDir | Out-Null
try {
    Copy-Item $built (Join-Path $destDir $library) -Force
}
catch [System.IO.IOException] {
    throw "Cannot replace $library in the Unity project: it is probably loaded by an open Unity Editor. Close the Editor and run this script again."
}

Write-Host "Copied $library to UniverseSim/Assets/UniverseSim/Plugins/Windows/x86_64/"
Write-Host "Restart the Unity Editor if it was open, so it loads the new binary."
