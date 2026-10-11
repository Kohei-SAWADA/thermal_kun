param([string]$OutputPath = (Join-Path $PSScriptRoot '..\thermal-kun-diagnostics.json'))
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\ThermalKun.exe'
if (!(Test-Path $exe)) { throw 'Extract the entire portable ZIP first. This script must remain inside its scripts folder.' }
$destination = [System.IO.Path]::GetFullPath($OutputPath)
$process = Start-Process -FilePath $exe -ArgumentList @('--diagnose', '--output', ('"' + $destination + '"')) -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Diagnostic failed. Inspect $destination" }
Get-Content -Raw $destination
