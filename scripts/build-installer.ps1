param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $OutputRoot,
    [string] $Compiler
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repository "artifacts\release\$Version"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (-not $Compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $Compiler = $command.Source
    } else {
        $Compiler = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'
    }
}
if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw 'Inno Setup compiler not found. Install Inno Setup 7 or pass -Compiler.'
}

& (Join-Path $PSScriptRoot 'publish-windows.ps1') -Version $Version -OutputRoot $OutputRoot
$installerOutput = Join-Path $OutputRoot 'installer'
New-Item -ItemType Directory -Path $installerOutput | Out-Null
& $Compiler "/DAppVersion=$Version" "/DPublishRoot=$OutputRoot" `
    "/DInstallerOutput=$installerOutput" (Join-Path $repository 'installer\DockerinoLamborgino.iss')
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
Write-Host "Installer built in $installerOutput"
