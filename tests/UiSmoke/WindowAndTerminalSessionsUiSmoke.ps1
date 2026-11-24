$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projectFile = Join-Path $repoRoot 'src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj'
$exe = Join-Path $repoRoot 'src\DockerinoLamborgino.App\bin\Debug\net10.0-windows\DockerinoLamborgino.App.exe'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('dockerino-terminal-smoke-' + [guid]::NewGuid().ToString('N'))
$store = Join-Path $root 'projects'
$fixture = Join-Path $root 'project'
$app = $null
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
function Get-SessionItems {
    $list = Get-SessionList
    if (-not $list) { return @() }
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    @($list.FindAll([System.Windows.Automation.TreeScope]::Children, $condition))
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

try {
    New-Item -ItemType Directory -Path $store, $fixture -Force | Out-Null
    & dotnet build $projectFile -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Application executable not found: $exe" }

    # Use an isolated project store so this smoke test never reads or edits user projects.
    $projectId = [guid]::NewGuid()
    $configuration = @{
        id = $projectId
        name = 'Terminal session smoke'
        directory = $fixture
        repositories = @()
        actions = @()
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
    if ((Get-SessionItems).Count -ne 2) { throw 'Expected exactly two terminal tabs.' }
    if (-not (Find-AppWindow 'Terminal status: normal')) {
        throw 'The floating launcher did not expose the normal terminal summary.'
    }
    if (Find-ByName $projectText 'Terminal status: Error') {
        throw 'Unassociated New terminal sessions changed the selected project status.'
    }

    # Select and close one real tab; the other session must remain running.
    Select-Item $first
    Select-Item $second
    Invoke-Element (Find-ByName $first 'Close terminal tab')
    Wait-For { (Get-SessionItems).Count -eq 1 } 'first tab to close' | Out-Null
    $remaining = Wait-For { Find-SessionItem 'Terminal 2' } 'unrelated terminal to remain'
    $remainingLabel = Find-ByName $remaining 'Terminal 2'
    if (-not $remainingLabel -or $remainingLabel.Current.HelpText -ne 'Terminal running.') {
        throw 'Closing one tab changed the remaining terminal state.'
    }
    if ($app.HasExited) { throw 'Closing a terminal tab exited the application.' }

    Invoke-Element (Find-ByName $remaining 'Close terminal tab')
    Wait-For { (Get-SessionItems).Count -eq 0 } 'second tab to close' | Out-Null
    if ($app.HasExited) { throw 'Closing the last terminal tab exited the application.' }

    'PASS: isolated project selection, two running New terminal sessions, floating status summary, per-tab close, unrelated-session survival, and dashboard lifetime.'
    'Running is the app-reported terminal state; this test does not assert pixel colors, prompt interaction, or OS-level process containment.'
}
finally {
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
