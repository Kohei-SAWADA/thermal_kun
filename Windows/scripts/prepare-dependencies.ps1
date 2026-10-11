# Downloads verified build inputs only. Does not run installers or change policy.
$ErrorActionPreference = 'Stop'
$dependencyRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\upstream'))
New-Item -ItemType Directory -Path $dependencyRoot -Force | Out-Null
$downloads = @(
    @('LibreHardwareMonitor.NET.10.zip', 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.NET.10.zip', '29739c4959b01b348fddad87664066634bcfd4f46e9bf41e4e916c318bcfdb99'),
    @('PawnIO_setup-2.2.0.exe', 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe', '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'),
    @('LibreHardwareMonitor-0.9.6-source.zip', 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/archive/refs/tags/v0.9.6.zip', '28100b87fcd7a9a213c58fabe1174e4f3064975b5ab11799224766993ca1fbfc'),
    @('PawnIO.Modules-0.1.6.zip', 'https://github.com/namazso/PawnIO.Modules/archive/refs/tags/0.1.6.zip', '665b2f7a028f8a317bc35cd6fbf64986115909239019ff2d30d19260d7dd0982')
)
foreach ($item in $downloads) {
    $targetFile = Join-Path $dependencyRoot $item[0]
    if (!(Test-Path -LiteralPath $targetFile)) { Invoke-WebRequest -Uri $item[1] -OutFile $targetFile }
    $actualHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $item[2]) { throw "Hash mismatch: $($item[0]). Preserve and inspect the file before retrying." }
    Write-Output "Verified $($item[0]): $actualHash"
}
Expand-Archive -LiteralPath (Join-Path $dependencyRoot 'LibreHardwareMonitor.NET.10.zip') -DestinationPath (Join-Path $dependencyRoot 'lhm-0.9.6') -Force
Write-Output 'Dependencies ready. No installer has been executed.'
