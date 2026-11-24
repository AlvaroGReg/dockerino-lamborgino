$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
public static class ShutdownSmokeNative {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    public static Task InvokeAsync(object pattern) =>
        Task.Run(() => pattern.GetType().GetMethod("Invoke").Invoke(pattern, null));
}
'@

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $repoRoot 'src\DockerinoLamborgino.App\bin\Debug\net10.0-windows\DockerinoLamborgino.App.exe'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('dockerino-shutdown-smoke-' + [guid]::NewGuid().ToString('N'))
$store = Join-Path $root 'projects'
$fixture = Join-Path $root 'fixture'
$app = $null
$sentinel = $null
$children = @()
$previousPipe = $env:DOCKERINO_PIPE_NAME
$env:DOCKERINO_PIPE_NAME = 'Dockerino.ShutdownSmoke.' + [guid]::NewGuid().ToString('N')
$cursor = [ShutdownSmokeNative+Point]::new()
$hasCursor = [ShutdownSmokeNative]::GetCursorPos([ref]$cursor)

function Wait-For([scriptblock]$condition, [string]$description, [int]$seconds = 30) {
    $until = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $result = & $condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $until)
    throw "Timed out waiting for $description."
}
function Find-AppWindow([string]$name) {
    $pidCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $pidCondition)
    foreach ($window in $windows) { if ($window.Current.Name -eq $name) { return $window } }
}
function Find-ByProperty($parent, $property, $value) {
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $value)
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Send-LocalCommand($request) {
    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $env:DOCKERINO_PIPE_NAME,
        [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(5000)
        $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 4096, $true)
        $writer.AutoFlush = $true
        $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::UTF8, $false, 4096, $true)
        $writer.WriteLine(($request | ConvertTo-Json -Compress))
        $response = $reader.ReadLine() | ConvertFrom-Json
        if (-not $response.success) { throw "Local command failed: $($response.error)" }
        $response.data
    } finally { $pipe.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $store, $fixture | Out-Null
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Build the app first: $exe" }
    $treeScript = Join-Path $root 'tree.ps1'
    @'
param([string]$Directory, [int]$Depth)
[System.IO.File]::WriteAllText((Join-Path $Directory "$Depth.pid"), "$PID")
if ($Depth -lt 2) {
    Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList @(
        '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
        '-Directory', "`"$Directory`"", '-Depth', ($Depth + 1)
    ) | Out-Null
}
# Even if containment fails, no test-created process runs indefinitely.
Start-Sleep -Seconds 120
'@ | Set-Content -LiteralPath $treeScript -Encoding UTF8
    $projectId, $actionId, $processId, $stepId = 1..4 | ForEach-Object { [guid]::NewGuid() }
    $command = "& '$($treeScript.Replace("'", "''"))' -Directory '$($root.Replace("'", "''"))' -Depth 0"
    $config = @{
        id = $projectId; name = 'Shutdown smoke'; directory = $fixture; repositories = @()
        actions = @(@{
            id = $actionId; name = 'Owned tree'; color = '#2378D2'
            description = 'Bounded test process tree'; isQuickLaunch = $false
            processes = @(@{
                id = $processId; name = 'Owned tree'; workingDirectory = $fixture
                steps = @(@{ id = $stepId; command = $command })
            })
        })
    } | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText((Join-Path $store "$projectId.json"), $config,
        [System.Text.UTF8Encoding]::new($false))

    $pwsh = (Get-Process -Id $PID).Path
    $sentinel = Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 120') -PassThru
    $null = $sentinel.Handle
    $app = Start-Process -FilePath $exe -ArgumentList @('--project-storage', "`"$store`"") -PassThru
    $null = $app.Handle
    Wait-For { Find-AppWindow 'dockerino-lamborgino' } 'isolated dashboard' | Out-Null
    $launch = Send-LocalCommand @{ operation = 'launch.start'; projectId = $projectId; actionId = $actionId }
    if (-not $launch.id) { throw 'Launch did not return a managed launch ID.' }
    foreach ($depth in 0..2) {
        $pidFile = Join-Path $root "$depth.pid"
        $process = Wait-For {
            if (Test-Path -LiteralPath $pidFile) {
                $value = 0
                if ([int]::TryParse([System.IO.File]::ReadAllText($pidFile), [ref]$value)) {
                    $p = [System.Diagnostics.Process]::GetProcessById($value)
                    $null = $p.Handle
                    return $p
                }
            }
        } "owned process depth $depth"
        $children += $process
        if ($process.HasExited) { throw "Owned process depth $depth exited before shutdown." }
    }
    if ($sentinel.HasExited) { throw 'Independent sentinel exited before shutdown.' }
    $beforeExit = Send-LocalCommand @{ operation = 'launch.inspect'; id = $launch.id }
    if ($beforeExit.state -ne 0 -or $beforeExit.processes[0].state -ne 1) {
        throw 'Managed launch was not running immediately before shutdown.'
    }

    $launcher = Wait-For { Find-AppWindow 'Terminal status: normal' } 'isolated launcher'
    $rect = $launcher.Current.BoundingRectangle
    [ShutdownSmokeNative]::SetCursorPos([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2)) | Out-Null
    $exit = Wait-For {
        Find-ByProperty $launcher ([System.Windows.Automation.AutomationElement]::NameProperty) 'Exit application'
    } 'launcher exit button'
    # The modal confirmation can block Invoke; dispatch the invocation separately.
    $invokeStarted = [DateTime]::UtcNow
    $invoke = [ShutdownSmokeNative]::InvokeAsync($exit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern))
    try {
        $dialog = Wait-For {
            if ($app.HasExited) { throw "App exited before confirmation with code $($app.ExitCode)." }
            $window = Find-AppWindow 'Exit dockerino-lamborgino'
            if ($window) { return $window }
            $dashboard = Find-AppWindow 'dockerino-lamborgino'
            if ($dashboard) {
                Find-ByProperty $dashboard ([System.Windows.Automation.AutomationElement]::NameProperty) 'Exit dockerino-lamborgino'
            }
        } 'exit confirmation' 10
    } catch {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
        $names = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children, $condition) | ForEach-Object { $_.Current.Name })
        $exitCode = if ($app.HasExited) { $app.ExitCode } else { 'running' }
        if ($app.HasExited) {
            $states = foreach ($process in $children) {
                $ended = $process.WaitForExit(15000)
                "depth $([array]::IndexOf($children, $process)): exited=$ended"
            }
            "Observed after launcher Exit: $($states -join ', '); sentinel exited=$($sentinel.HasExited)."
        }
        throw "Exit confirmation not found. Elapsed: $([DateTime]::UtcNow - $invokeStarted); invoke status: $($invoke.Status); error: $($invoke.Exception); app exit: $exitCode; window names: $($names -join ', ')."
    }
    $yes = Wait-For {
        Find-ByProperty $dialog ([System.Windows.Automation.AutomationElement]::AutomationIdProperty) '6'
    } 'confirmation Yes button'
    $yesInvoke = [ShutdownSmokeNative]::InvokeAsync($yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern))
    if (-not $app.WaitForExit(30000)) { throw 'Application did not exit after confirmation.' }
    foreach ($process in $children) {
        if (-not $process.WaitForExit(15000)) { throw "Owned process $($process.Id) survived application exit." }
    }
    if ($sentinel.HasExited) { throw 'Independent sentinel was terminated by application exit.' }
    "PASS: confirmed launcher Exit closed the isolated application and all three owned tree processes; independent sentinel remained alive."
} finally {
    if ($hasCursor) { [ShutdownSmokeNative]::SetCursorPos($cursor.X, $cursor.Y) | Out-Null }
    if ($app -and -not $app.HasExited) { $app.Kill(); $app.WaitForExit() }
    if ($sentinel -and -not $sentinel.HasExited) { $sentinel.Kill(); $sentinel.WaitForExit() }
    foreach ($process in $children) { $process.Dispose() }
    if ($app) { $app.Dispose() }
    if ($sentinel) { $sentinel.Dispose() }
    $env:DOCKERINO_PIPE_NAME = $previousPipe
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
