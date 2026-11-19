param(
    [string] $Command,
    [int] $TimeoutSeconds = 30,
    [string] $WindowTitle = "TerminalSpike-Fase1-startup",
    [string] $PromptPattern = 'PS [^\r\n]*> ',
    [switch] $NoBuild
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "TerminalSpike.csproj"
$output = Join-Path $PSScriptRoot "bin\Debug\net10.0-windows\TerminalSpike.exe"
$runId = [guid]::NewGuid().ToString('N')
$log = Join-Path $env:TEMP "TerminalSpike-Fase1-startup-$runId.log"
if ([string]::IsNullOrWhiteSpace($Command)) {
    $Command = "pwsh.exe -NoLogo -NoProfile -NoExit"
}

if (-not $NoBuild) {
    dotnet build $project --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo compilar TerminalSpike (exit code $LASTEXITCODE). Cierra las ventanas TerminalSpike que usan ese ejecutable."
    }
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new($output)
$startInfo.UseShellExecute = $false
foreach ($argument in @(
        "--command", $Command,
        "--log-file", $log,
        "--title", $WindowTitle
    )) {
    $startInfo.ArgumentList.Add($argument)
}
$app = [System.Diagnostics.Process]::Start($startInfo)
Write-Host "Ventana de aceptación: '$WindowTitle' (PID de la app $($app.Id))."
Write-Host "Esperando PID del shell y salida inicial en $log ..."

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$logContent = ""
$replayedOutput = $null
while ([DateTime]::UtcNow -lt $deadline) {
    if ($app.HasExited) {
        throw "La app terminó con código $($app.ExitCode). Registro:`n$logContent"
    }
    if (Test-Path -LiteralPath $log) {
        $logContent = Get-Content -LiteralPath $log -Raw
        $replayedOutput = [regex]::Match($logContent, "INITIAL_OUTPUT_REPLAYED chars=(\d+)")
        if ($logContent.Contains("TERM_READY_CONTROL_CONNECTED") -and
            $logContent.Contains("PROCESS_ACTIVE") -and
            [regex]::IsMatch($logContent, $PromptPattern) -and
            $replayedOutput.Success) {
            break
        }
    }
    Start-Sleep -Milliseconds 250
}

if (-not $logContent.Contains("TERM_READY_CONTROL_CONNECTED") -or
    -not $logContent.Contains("PROCESS_ACTIVE") -or
    -not $replayedOutput.Success -or
    -not [regex]::IsMatch($logContent, $PromptPattern)) {
    throw "No se observó conexión, PID activo, handoff y prompt en ConPTY antes del timeout. Registro:`n$logContent"
}

$pidMatch = [regex]::Match($logContent, "PROCESS_ACTIVE PID (\d+)")
if (-not $pidMatch.Success) {
    throw "El registro no incluye el PID real del shell:`n$logContent"
}
$shellPid = [int] $pidMatch.Groups[1].Value
$shell = Get-Process -Id $shellPid -ErrorAction Stop
1..6 | ForEach-Object {
    Start-Sleep -Milliseconds 500
    $shell.Refresh()
    if ($shell.HasExited) {
        throw "El shell PID $shellPid terminó durante la comprobación de 3 segundos (código $($shell.ExitCode))."
    }
}
Write-Host "PASS: shell PID $shellPid ($($shell.ProcessName)) sigue activo y el prompt apareció en ConPTY; handoff inicial: $($replayedOutput.Groups[1].Value) caracteres. No se ha probado el teclado físico."
Write-Host "La ventana queda abierta para aceptación manual:"
Write-Host "  1. Comprueba que el prompt está visible."
Write-Host "  2. En pwsh escribe: [IO.File]::WriteAllText('$log.physical-ok','TERMINALSPIKE_PHYSICAL_KEYBOARD_MARKER') y pulsa Enter."
Write-Host "  3. Comprueba el archivo $log.physical-ok; el log también muestra la entrada y la salida."
Write-Host "  4. Cambia el tamaño de la ventana y confirma que el terminal refluye."
Write-Host "No se envían teclas automatizadas. Windows puede activar la ventana nueva."
