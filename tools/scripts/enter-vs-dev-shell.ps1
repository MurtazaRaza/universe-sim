<#
.SYNOPSIS
Puts the Visual Studio x64 C++ toolchain (cl, link), CMake and Ninja on PATH in the current PowerShell session.

.DESCRIPTION
Dot-source it before the core's CMake commands on Windows:

    . tools/scripts/enter-vs-dev-shell.ps1
    cd core; cmake --preset dev; cmake --build --preset dev; ctest --preset dev

It finds the newest Visual Studio 2022 or newer with the "Desktop development with C++" tools (vswhere) and
enters its x64 developer shell, which also provides the CMake and Ninja that ship with Visual Studio. Visual
Studio 2022 (MSVC 19.30) is the minimum: older compilers contract multiply-adds under /fp:precise, which the core
rejects (core/cmake/UsimCompilerOptions.cmake). If an x64 cl.exe is already on PATH, nothing changes.

Works in Windows PowerShell 5.1 and PowerShell 7. Needs an execution policy that runs local scripts
(RemoteSigned, the usual default for the current user), or run the commands through
`powershell -ExecutionPolicy Bypass`.
#>

$ErrorActionPreference = 'Stop'

function Test-UsimX64Compiler {
    $cl = Get-Command cl.exe -ErrorAction SilentlyContinue
    return ($null -ne $cl) -and ($cl.Source -match '\\Host[xX]64\\x64\\cl\.exe$')
}

if (-not (Test-UsimX64Compiler)) {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    $vswhere = Join-Path $installer 'vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found in '$installer'. Install Visual Studio 2022 or newer with 'Desktop development with C++'."
    }

    $vsPath = & $vswhere -latest -products * -version '[17.0,' `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) {
        throw "No Visual Studio 2022 or newer with the x64 C++ tools found. Install the 'Desktop development with C++' workload."
    }

    # The developer shell's batch files call vswhere by name; without its folder on PATH they print
    # "'vswhere.exe' is not recognized" (harmless, but noisy).
    if (($env:PATH -split ';') -notcontains $installer) {
        $env:PATH = "$installer;$env:PATH"
    }

    & (Join-Path $vsPath 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null

    if (-not (Test-UsimX64Compiler)) {
        throw "Entered the developer shell of '$vsPath', but no x64 cl.exe is on PATH."
    }
}

foreach ($tool in 'cmake', 'ninja') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool not found. It ships with Visual Studio's 'C++ CMake tools for Windows' component."
    }
}

Write-Host "Visual Studio x64 developer environment: $((Get-Command cl.exe).Source)"
