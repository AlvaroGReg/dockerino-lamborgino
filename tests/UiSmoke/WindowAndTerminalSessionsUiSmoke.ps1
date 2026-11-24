$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectFile = Join-Path $repoRoot 'src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj'
$cliProject = Join-Path $repoRoot 'src\DockerinoLamborgino.Cli\DockerinoLamborgino.Cli.csproj'
$cliDll = Join-Path $repoRoot 'src\DockerinoLamborgino.Cli\bin\Debug\net10.0\DockerinoLamborgino.Cli.dll'
$exe = Join-Path $repoRoot 'src\DockerinoLamborgino.App\bin\Debug\net10.0-windows\DockerinoLamborgino.App.exe'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('dockerino-terminal-smoke-' + [guid]::NewGuid().ToString('N'))
$store = Join-Path $root 'projects'
$fixture = Join-Path $root 'project'
$app = $null
$manualFollow = $null
$managedFollow = $null
$previousPipeName = $env:DOCKERINO_PIPE_NAME
$env:DOCKERINO_PIPE_NAME = 'Dockerino.UiSmoke.' + [guid]::NewGuid().ToString('N')

function Find-ByProperty($parent, $property, $value) {
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $value)
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-ByName($parent, [string]$name) {
    Find-ByProperty $parent ([System.Windows.Automation.AutomationElement]::NameProperty) $name
}
function Find-ById($parent, [string]$id) {
    Find-ByProperty $parent ([System.Windows.Automation.AutomationElement]::AutomationIdProperty) $id
}
function Wait-For([scriptblock]$condition, [string]$description, [int]$timeoutSeconds = 20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $value = & $condition
        if ($value) { return $value }
        if ($app) {
            $app.Refresh()
            if ($app.HasExited) { throw "Application exited unexpectedly: $($app.ExitCode)" }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $description."
}
function Get-AppWindows {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
    [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $condition)
}
function Find-AppWindow([string]$name) {
    foreach ($window in (Get-AppWindows)) {
        if ($window.Current.Name -eq $name) { return $window }
    }
    return $null
}
function Invoke-Element($element) {
    if (-not $element) { throw 'Cannot invoke a missing UI element.' }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Select-Item($element) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($element) {
        $pattern = $null
        if ($element.TryGetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
            return
        }
        $element = $walker.GetParent($element)
    }
    throw 'No selectable list item was found.'
}
function Get-SessionList { Find-ById $main 'SessionsList' }
function Get-SessionCount {
    $list = Get-SessionList
    if (-not $list) { return 0 }
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $list.FindAll([System.Windows.Automation.TreeScope]::Children, $condition).Count
}
function Find-SessionItem([string]$label) {
    $list = Get-SessionList
    if (-not $list) { return $null }
    $element = Find-ByName $list $label
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($element -and $element.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $element = $walker.GetParent($element)
    }
    $element
}
function Find-ProjectItem([string]$label) {
    $element = Find-ByName (Find-ById $main 'ProjectsList') $label
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($element -and $element.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $element = $walker.GetParent($element)
    }
    $element
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
        return $response.data
    } finally { $pipe.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $store, $fixture -Force | Out-Null
    & dotnet build $projectFile -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    & dotnet build $cliProject -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Application executable not found: $exe" }

    # Use an isolated project store so this smoke test never reads or edits user projects.
    $projectId = [guid]::NewGuid()
    $actionId = [guid]::NewGuid()
    $processId = [guid]::NewGuid()
    $stepId = [guid]::NewGuid()
    $configuration = @{
        id = $projectId
        name = 'Terminal session smoke'
        directory = $fixture
        repositories = @()
        actions = @(@{
            id = $actionId
            name = 'Configured failure'
            color = '#2378D2'
            description = 'Smoke test for configured command failure status'
            isQuickLaunch = $false
            processes = @(@{
                id = $processId
                name = 'Configured failure'
                workingDirectory = $fixture
                steps = @(@{ id = $stepId; command = "throw 'configured failure'" })
            })
        })
    } | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText(
        (Join-Path $store "$projectId.json"), $configuration, [System.Text.UTF8Encoding]::new($false))

    $app = Start-Process -FilePath $exe `
        -ArgumentList @('--project-storage', "`"$store`"") -PassThru
    $main = Wait-For { Find-AppWindow 'dockerino-lamborgino' } 'dashboard window'
    $projectText = Wait-For {
        Find-ByName (Find-ById $main 'ProjectsList') 'Terminal session smoke'
    } 'isolated project row'
    Select-Item $projectText

    # These are the actual "New terminal" commands exposed by the dashboard.
    Invoke-Element (Find-ById $main 'NewTerminalButton')
    Invoke-Element (Wait-For { Find-ById $main 'NewTerminalButton' } 'New terminal button')
    $first = Wait-For { Find-SessionItem 'Terminal 1' } 'first terminal tab'
    $second = Wait-For { Find-SessionItem 'Terminal 2' } 'second terminal tab'
    Wait-For {
        $label = Find-ByName $first 'Terminal 1'
        if ($label -and $label.Current.HelpText -eq 'Terminal running.') { $label }
    } 'first terminal startup completion' 30 | Out-Null
    Wait-For {
        $label = Find-ByName $second 'Terminal 2'
        if ($label -and $label.Current.HelpText -eq 'Terminal running.') { $label }
    } 'second terminal startup completion' 30 | Out-Null
    if ((Get-SessionCount) -ne 2) { throw 'Expected exactly two terminal tabs.' }
    if (-not (Find-AppWindow 'Terminal status: normal')) {
        throw 'The floating launcher did not expose the normal terminal summary.'
    }
    if (Find-ByName (Find-ProjectItem 'Terminal session smoke') 'Terminal status: Error') {
        throw 'Unassociated New terminal sessions changed the selected project status.'
    }

    # A configured launch command failure marks its managed session/project red,
    # while the interactive PowerShell process remains alive.
    Invoke-Element (Wait-For { Find-ByName (Find-ById $main 'LaunchActionsList') 'Configured failure' } 'configured launch action')
    $failedLaunchTab = Wait-For { Find-SessionItem 'Configured failure' } 'configured launch terminal'
    Wait-For { (Get-SessionCount) -eq 3 } 'three terminal tabs' | Out-Null
    Wait-For {
        Find-ByName $failedLaunchTab 'Terminal status: Error'
    } 'configured command error indicator' | Out-Null
    Wait-For {
        Find-ByName (Find-ProjectItem 'Terminal session smoke') 'Terminal status: Error'
    } 'project command error indicator' | Out-Null
    Wait-For {
        Find-AppWindow 'Terminal status: error'
    } 'floating launcher command error indicator' | Out-Null
    if (Find-ByName $second 'Terminal status: Error') {
        throw 'A New terminal session inherited a configured launch command error.'
    }

    $listed = @(Send-LocalCommand @{ operation = 'session.list' })
    $manualId = ($listed | Where-Object { $_.id } | Select-Object -First 1).id
    $managedId = ($listed | Where-Object { $_.sessionId } | Select-Object -First 1).sessionId
    if (-not $manualId -or -not $managedId) { throw 'Could not identify both session types.' }
    $beforeManual = Send-LocalCommand @{ operation = 'session.output'; id = $manualId; offset = 0 }
    $beforeManaged = Send-LocalCommand @{ operation = 'session.output'; id = $managedId; offset = 0 }
    $manualFollow = Start-Process dotnet -ArgumentList @("`"$cliDll`"", 'session', 'follow', $manualId) `
        -RedirectStandardOutput (Join-Path $root 'manual-follow.txt') -RedirectStandardError (Join-Path $root 'manual-error.txt') -PassThru
    $managedFollow = Start-Process dotnet -ArgumentList @("`"$cliDll`"", 'session', 'follow', $managedId) `
        -RedirectStandardOutput (Join-Path $root 'managed-follow.txt') -RedirectStandardError (Join-Path $root 'managed-error.txt') -PassThru
    Wait-For {
        $manualFollow.Refresh()
        $managedFollow.Refresh()
        if ($manualFollow.HasExited -or $managedFollow.HasExited) { throw 'A follower exited before tab close.' }
        if ((Test-Path (Join-Path $root 'manual-follow.txt')) -and
            (Test-Path (Join-Path $root 'managed-follow.txt')) -and
            (Get-Item (Join-Path $root 'manual-follow.txt')).Length -gt 0 -and
            (Get-Item (Join-Path $root 'managed-follow.txt')).Length -gt 0) { $true }
    } 'both followers to receive output before tab close' 30 | Out-Null

    # Select and close one real tab; the other session must remain running.
    Select-Item $first
    Select-Item $second
    Invoke-Element (Find-ByName $first 'Close terminal tab')
    Wait-For { (Get-SessionCount) -eq 2 } 'first tab to close' | Out-Null
    $remaining = Wait-For { Find-SessionItem 'Terminal 2' } 'unrelated terminal to remain'
    $remainingLabel = Find-ByName $remaining 'Terminal 2'
    if (-not $remainingLabel -or $remainingLabel.Current.HelpText -ne 'Terminal running.') {
        throw 'Closing one tab changed the remaining terminal state.'
    }
    if ($app.HasExited) { throw 'Closing a terminal tab exited the application.' }

    Invoke-Element (Find-ByName $remaining 'Close terminal tab')
    Wait-For { (Get-SessionCount) -eq 1 } 'second tab to close' | Out-Null
    Invoke-Element (Find-ByName $failedLaunchTab 'Close terminal tab')
    Wait-For { (Get-SessionCount) -eq 0 } 'configured failure tab to close' | Out-Null
    foreach ($case in @(
        @{ id = $manualId; before = $beforeManual; follower = $manualFollow; file = 'manual-follow.txt' },
        @{ id = $managedId; before = $beforeManaged; follower = $managedFollow; file = 'managed-follow.txt' }
    )) {
        $id = $case.id
        $after = Wait-For {
            $value = Send-LocalCommand @{ operation = 'session.output'; id = $id; offset = 0 }
            if ($value.completed) { $value }
        } "closed session output completion $id" 30
        $suffix = Send-LocalCommand @{ operation = 'session.output'; id = $id; offset = $case.before.nextOffset }
        if (-not $after.text.StartsWith($case.before.text) -or
            $suffix.text -ne $after.text.Substring([int]$case.before.nextOffset)) {
            throw "Output or offset changed after closing session $id."
        }
        $null = Send-LocalCommand @{ operation = 'session.inspect'; id = $id }
        if (-not $case.follower.WaitForExit(30000) -or $case.follower.ExitCode -ne 0) {
            throw "Follower did not finish successfully for session $id."
        }
        # The CLI writes using the inherited Windows console output code page,
        # which is not necessarily UTF-8 when redirected to a file.
        $followText = [System.IO.File]::ReadAllText((Join-Path $root $case.file), [Console]::OutputEncoding)
        if ($followText -cne $after.text) {
            $diff = 0
            while ($diff -lt [Math]::Min($followText.Length, $after.text.Length) -and $followText[$diff] -ceq $after.text[$diff]) { $diff++ }
            $a = if ($diff -lt $followText.Length) { [int][char]$followText[$diff] } else { -1 }
            $b = if ($diff -lt $after.text.Length) { [int][char]$after.text[$diff] } else { -1 }
            throw "Follower output differs from retained output for session $id. Lengths: $($followText.Length), $($after.text.Length); first difference at $diff (characters $a, $b)."
        }
    }
    if ($app.HasExited) { throw 'Closing the last terminal tab exited the application.' }

    'PASS: isolated project selection, New terminal isolation, configured command failure indicators, floating status summary, per-tab close, unrelated-session survival, and dashboard lifetime.'
    'PASS: independent and configured session inspection, retained output and offsets after tab close, and both pre-existing CLI followers completing with matching output.'
    'Status is asserted through UI Automation names; this test does not assert pixel colors, prompt interaction, or OS-level process containment.'
}
finally {
    foreach ($follower in @($manualFollow, $managedFollow)) {
        if ($follower) {
            $follower.Refresh()
            if (-not $follower.HasExited) {
                Stop-Process -Id $follower.Id -Force
                $follower.WaitForExit()
            }
        }
    }
    if ($app) {
        $app.Refresh()
        if (-not $app.HasExited) {
            # This smoke covers session-tab lifecycle, not the separate launcher exit flow.
            Stop-Process -Id $app.Id -Force
            $app.WaitForExit()
        }
    }
    $env:DOCKERINO_PIPE_NAME = $previousPipeName
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
