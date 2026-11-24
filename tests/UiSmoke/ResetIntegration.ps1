$ErrorActionPreference = 'Stop'

# Exercises the real application, CLI, named pipe, ConPTY shell and session job.
# An interactive Windows desktop is required; no keyboard or mouse input is sent.
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appProject = Join-Path $repoRoot 'src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj'
$cliProject = Join-Path $repoRoot 'src\DockerinoLamborgino.Cli\DockerinoLamborgino.Cli.csproj'
$appExe = Join-Path $repoRoot 'src\DockerinoLamborgino.App\bin\Debug\net10.0-windows\DockerinoLamborgino.App.exe'
$cliExe = Join-Path $repoRoot 'src\DockerinoLamborgino.Cli\bin\Debug\net10.0\DockerinoLamborgino.Cli.exe'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('dockerino-reset-integration-' + [guid]::NewGuid().ToString('N'))
$store = Join-Path $root 'projects'
$fixture = Join-Path $root 'project'
$marker = Join-Path $root 'executions.txt'
$app = $null
$oldPipe = $env:DOCKERINO_PIPE_NAME
$oldCli = $env:DOCKERINO_CLI_PATH
$oldContext = $env:DOCKERINO_SESSION_ID
$env:DOCKERINO_PIPE_NAME = 'Dockerino.ResetIntegration.' + [guid]::NewGuid().ToString('N')
$env:DOCKERINO_CLI_PATH = $cliExe
$env:DOCKERINO_SESSION_ID = $null

function Invoke-Cli([string[]]$arguments, [int]$expectedExit = 0) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($cliExe)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) {
            $process.Kill($true)
            throw "CLI timed out: $($arguments -join ' ')"
        }
        $out = $stdout.GetAwaiter().GetResult()
        $err = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne $expectedExit) {
            throw "CLI $($arguments -join ' ') exited $($process.ExitCode), expected $expectedExit. stdout: $out stderr: $err"
        }
        if ($expectedExit -eq 0) { return ($out | ConvertFrom-Json) }
        if (-not $err.Trim()) { throw 'Expected a diagnostic on standard error.' }
    }
    finally { $process.Dispose() }
}

function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 45) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        if ($app) {
            $app.Refresh()
            if ($app.HasExited) { throw "Application exited unexpectedly: $($app.ExitCode)" }
        }
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $description."
}

function Get-Outcome($inspection, [string]$originalId) {
    @($inspection.resets | Where-Object { $_.originalSessionId -eq $originalId }) | Select-Object -First 1
}

try {
    New-Item -ItemType Directory -Path $store, $fixture -Force | Out-Null
    & dotnet build $appProject -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    & dotnet build $cliProject -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }

    $projectId = [guid]::NewGuid()
    $actionId = [guid]::NewGuid()
    $processId = [guid]::NewGuid()
    $otherProcessId = [guid]::NewGuid()
    # The first shell requests self-reset only once. Every execution records the
    # actual shell context, including the replacement created by the external CLI.
    $quotedMarker = $marker.Replace("'", "''")
    $record = "[System.IO.File]::AppendAllText('$quotedMarker', `$env:DOCKERINO_SESSION_ID + [Environment]::NewLine)"
    $selfReset = "if ([System.IO.File]::ReadAllLines('$quotedMarker').Length -eq 1) { dockerino reset }"
    $configuration = @{
        id = $projectId; name = 'Reset integration'; directory = $fixture; repositories = @()
        actions = @(@{
            id = $actionId; name = 'Reset integration'; color = '#2378D2'
            processes = @(
                @{ id = $processId; name = 'Reset target'; workingDirectory = $fixture
                   steps = @(@{ id = [guid]::NewGuid(); command = $record },
                             @{ id = [guid]::NewGuid(); command = $selfReset }) },
                @{ id = $otherProcessId; name = 'Independent'; workingDirectory = $fixture
                   steps = @(@{ id = [guid]::NewGuid(); command = 'Start-Sleep -Seconds 300' }) }
            )
        })
    } | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText((Join-Path $store "$projectId.json"), $configuration, [System.Text.UTF8Encoding]::new($false))

    $app = Start-Process -FilePath $appExe -ArgumentList @('--launcher-only', '--project-storage', "`"$store`"") -PassThru
    # Do not use a CLI command to probe readiness: most commands can start an app.
    Wait-For {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $env:DOCKERINO_PIPE_NAME)
        try { $pipe.Connect(100); return $true }
        catch [TimeoutException] { return $false }
        finally { $pipe.Dispose() }
    } 'application pipe' | Out-Null

    $launch = Invoke-Cli @('launch', 'start', "$projectId", "$actionId")
    $launchId = "$($launch.id)"
    $originalId = "$(($launch.processes | Where-Object { $_.processId -eq "$processId" }).sessionId)"
    $otherId = "$(($launch.processes | Where-Object { $_.processId -eq "$otherProcessId" }).sessionId)"
    if (-not $launchId -or -not $originalId -or -not $otherId) { throw 'Launch did not return the expected managed IDs.' }

    $selfOutcome = Wait-For {
        $inspection = Invoke-Cli @('launch', 'inspect', $launchId)
        $outcome = Get-Outcome $inspection $originalId
        if ($outcome.state -eq 'failed') { throw "Self-reset failed: $($outcome.error)" }
        if ($outcome.state -eq 'succeeded') { return $outcome }
    } 'self-reset outcome'
    $replacementId = "$($selfOutcome.replacementSessionId)"
    if (-not $replacementId -or $replacementId -eq $originalId) { throw 'Self-reset did not replace the original session.' }
    Wait-For {
        if ((Test-Path -LiteralPath $marker) -and [System.IO.File]::ReadAllLines($marker).Length -ge 2) { return $true }
    } 'self-reset sequence replay' | Out-Null

    # This invocation is a separate process with no terminal context.
    $acceptance = Invoke-Cli @('session', 'reset', $replacementId)
    if ("$($acceptance.launchId)" -ne $launchId -or "$($acceptance.originalSessionId)" -ne $replacementId) {
        throw 'External reset acceptance did not identify the requested launch and session.'
    }
    $externalOutcome = Wait-For {
        $inspection = Invoke-Cli @('launch', 'inspect', $launchId)
        $outcome = Get-Outcome $inspection $replacementId
        if ($outcome.state -eq 'failed') { throw "External reset failed: $($outcome.error)" }
        if ($outcome.state -eq 'succeeded') { return $outcome }
    } 'external reset outcome'
    $externalId = "$($externalOutcome.replacementSessionId)"
    if (-not $externalId -or $externalId -eq $replacementId) { throw 'External reset did not replace its target.' }
    Wait-For {
        if ([System.IO.File]::ReadAllLines($marker).Length -ge 3) { return $true }
    } 'external reset sequence replay' | Out-Null
    $executions = [System.IO.File]::ReadAllLines($marker)
    if ($executions.Count -ne 3 -or $executions[0] -ne $originalId -or
        $executions[1] -ne $replacementId -or $executions[2] -ne $externalId) {
        throw "Unexpected sequence executions: $($executions -join ', ')"
    }
    $inspection = Invoke-Cli @('launch', 'inspect', $launchId)
    $target = $inspection.processes | Where-Object { $_.processId -eq "$processId" }
    $other = $inspection.processes | Where-Object { $_.processId -eq "$otherProcessId" }
    if ("$($target.sessionId)" -ne $externalId -or "$($other.sessionId)" -ne $otherId -or $other.state -ne 1) {
        throw 'Inspection does not show the replacement and the still-running independent session.'
    }
    Invoke-Cli @('session', 'reset', $originalId) 1
    Invoke-Cli @('launch', 'stop', $launchId) | Out-Null
    'PASS: real self-reset and external reset, acceptance IDs, inspection, replay, stale ID rejection and independent session.'
}
finally {
    if ($app) {
        $app.Refresh()
        if (-not $app.HasExited) { Stop-Process -Id $app.Id -Force; $app.WaitForExit() }
    }
    $env:DOCKERINO_PIPE_NAME = $oldPipe
    $env:DOCKERINO_CLI_PATH = $oldCli
    $env:DOCKERINO_SESSION_ID = $oldContext
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
