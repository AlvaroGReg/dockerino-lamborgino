[CmdletBinding()]
param(
    [string]$ResultsRoot = "artifacts/verification"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $IsWindows) {
    throw "Verification requires Windows and PowerShell 7."
}

$runId = "{0}-{1}" -f (Get-Date -Format "yyyyMMdd-HHmmss"), [guid]::NewGuid().ToString("N")
$results = Join-Path ([System.IO.Path]::GetFullPath($ResultsRoot, $root)) $runId
New-Item -ItemType Directory -Path $results -Force | Out-Null

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    Write-Host "> $Program $($Arguments -join ' ')"
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Program exited with code $LASTEXITCODE."
    }
}

Start-Transcript -Path (Join-Path $results "verification.log") | Out-Null
try {
    Write-Host "Started (UTC): $([DateTime]::UtcNow.ToString('o'))"
    Write-Host "Results: $results"
    Write-Host "OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
    Write-Host "Architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)"
    Write-Host "PowerShell: $($PSVersionTable.PSVersion)"
    Invoke-Checked git @("rev-parse", "HEAD")
    Write-Host "Working tree status (including untracked files):"
    Invoke-Checked git @("status", "--short", "--untracked-files=normal")
    Invoke-Checked git @("--version")
    Invoke-Checked dotnet @("--info")
    Invoke-Checked pwsh @("-NoProfile", "-Command", '$PSVersionTable.PSVersion.ToString()')

    Invoke-Checked dotnet @("restore", "DockerinoLamborgino.sln", "-p:RestoreLockedMode=true")
    foreach ($configuration in @("Debug", "Release")) {
        Invoke-Checked dotnet @("build", "DockerinoLamborgino.sln", "--no-restore", "-c", $configuration)
    }
    Invoke-Checked dotnet @(
        "test", "DockerinoLamborgino.sln", "--no-build", "--no-restore", "-c", "Release",
        "--logger", "trx", "--collect", "Code Coverage;Format=Cobertura",
        "--results-directory", (Join-Path $results "test-results")
    )
    $trx = @(Get-ChildItem (Join-Path $results "test-results") -Filter "*.trx" -Recurse)
    $coverage = @(Get-ChildItem (Join-Path $results "test-results") -Filter "*.cobertura.xml" -Recurse)
    if ($trx.Count -lt 2 -or $coverage.Count -lt 2) {
        throw "Expected TRX and Cobertura files for both xUnit projects; found $($trx.Count) TRX and $($coverage.Count) coverage files."
    }
    # This project is not in the solution; keep its reports separate.
    $serverResults = Join-Path $results "server-test-results"
    Invoke-Checked dotnet @(
        "test", "tests/DockerinoLamborgino.LocalCommandServer.Tests/DockerinoLamborgino.LocalCommandServer.Tests.csproj",
        "-c", "Release", "-p:RestoreLockedMode=true", "--logger", "trx",
        "--collect", "Code Coverage;Format=Cobertura", "--results-directory", $serverResults
    )
    $serverTrx = @(Get-ChildItem $serverResults -Filter "*.trx" -Recurse)
    $serverCoverage = @(Get-ChildItem $serverResults -Filter "*.cobertura.xml" -Recurse)
    if ($serverTrx.Count -lt 1 -or $serverCoverage.Count -lt 1) {
        throw "Expected TRX and Cobertura files for the local command server tests."
    }
    Invoke-Checked dotnet @(
        "run", "--project", "tests/DockerinoLamborgino.App.SequenceChecks/DockerinoLamborgino.App.SequenceChecks.csproj",
        "--no-build", "--no-restore", "-c", "Release"
    )
    # This standalone WPF check is not in the solution and does not open a window.
    Invoke-Checked dotnet @(
        "run", "--project", "tests/GitPanelSelectionChecks/GitPanelSelectionChecks.csproj",
        "-c", "Release", "-p:RestoreLockedMode=true"
    )
    Write-Host "Verification passed (UTC): $([DateTime]::UtcNow.ToString('o'))"
}
catch {
    Write-Host "Verification failed (UTC): $([DateTime]::UtcNow.ToString('o'))"
    Write-Host $_
    throw
}
finally {
    Stop-Transcript | Out-Null
}
