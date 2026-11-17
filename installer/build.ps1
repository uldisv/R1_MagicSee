# Builds the R1 Keeper installer.
#   1. Publishes the program self-contained for x64 into dist\app (no .NET install needed
#      on the target machine).
#   2. Compiles installer\R1Keeper.iss with Inno Setup 7 into dist\R1KeeperSetup-<version>.exe.
#
# Usage: powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Iscc <path to ISCC.exe>]

param([string]$Iscc)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'dist\app'

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
dotnet publish (Join-Path $root 'src\R1Keeper') -c Release -r win-x64 --self-contained -o $app
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $Iscc) {
    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
    )
    $Iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $Iscc) { throw 'Inno Setup 7 (ISCC.exe) not found. Install it or pass -Iscc <path>.' }
}

& $Iscc (Join-Path $PSScriptRoot 'R1Keeper.iss')
exit $LASTEXITCODE
