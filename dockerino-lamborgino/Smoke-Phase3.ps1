$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = Join-Path $PSScriptRoot 'src\DockerinoLamborgino.App\bin\Debug\net10.0-windows\DockerinoLamborgino.App.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Build Debug first: $exe" }
$base = Join-Path ([System.IO.Path]::GetTempPath()) ("dockerino-phase3-" + [guid]::NewGuid().ToString('N'))
$store = Join-Path $base 'store'
$project = Join-Path $base 'project'
$marker = Join-Path $base 'command-executed.txt'
$app = $null

function Find-Element($root, $property, $value) {
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $value)
    $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-Element($root, $property, $value) {
    for ($i = 0; $i -lt 100; $i++) {
        $found = Find-Element $root $property $value
        if ($found) { return $found }
        Start-Sleep -Milliseconds 100
    }
    throw "UI element not found: $value"
}
function By-Name($root, [string]$name) {
    Wait-Element $root ([System.Windows.Automation.AutomationElement]::NameProperty) $name
}
function By-Id($root, [string]$id) {
    Wait-Element $root ([System.Windows.Automation.AutomationElement]::AutomationIdProperty) $id
}
function Click($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Value($element, [string]$value) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}
function Value($element) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Assert-NoMarker {
    if (Test-Path -LiteralPath $marker) { throw 'The configured marker command was executed.' }
}
function Window-ForProcess([string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    for ($i = 0; $i -lt 100; $i++) {
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id))
        $found = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($found) { return $found }
        $mainCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $mainCondition)
        foreach ($window in $windows) {
            $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
            if ($found) { return $found }
        }
        $app.Refresh()
        if ($app.HasExited) { throw "App exited unexpectedly: $($app.ExitCode)" }
        Start-Sleep -Milliseconds 100
    }
    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id))
    $nested = if ($windows.Count) { $windows[0].FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)) }
    throw "Window not found: $name. Top-level: $(@($windows | ForEach-Object { $_.Current.Name }) -join ', '); nested: $(@($nested | ForEach-Object { $_.Current.Name }) -join ', ')"
}
function Start-TestApp {
    # An explicit absolute directory is the App's isolated storage contract.
    $script:app = Start-Process -FilePath $exe -ArgumentList @('--project-storage', "`"$store`"") -PassThru
    $main = Window-ForProcess 'dockerino-lamborgino'
    Click (By-Name $main 'Manage projects…')
    Window-ForProcess 'Project configuration'
}
function Save-Draft($manager) {
    Click (By-Name $manager 'Review and save…')
    $review = Window-ForProcess 'Review before saving'
    Click (By-Name $review 'Save')
    for ($i = 0; $i -lt 100; $i++) {
        $status = Value (By-Id $manager 'ErrorsBox')
        if ($status -match 'Configuration saved') { Assert-NoMarker; return }
        if ($status -and $status -notmatch 'Configuration saved') { throw "Save failed: $status" }
        Start-Sleep -Milliseconds 100
    }
    throw 'Save did not complete.'
}
function Select-Project($manager, [string]$name) {
    $list = By-Id $manager 'ProjectsList'
    $item = By-Name $list $name
    while ($item -and $item.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $item = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($item)
    }
    if (-not $item) { throw "Project item not found: $name" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Stop-TestApp {
    if ($null -eq $app) { return }
    $app.Refresh()
    if (-not $app.HasExited) {
        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(5000)) { Stop-Process -Id $app.Id -Force }
    }
    $script:app = $null
}

try {
    New-Item -ItemType Directory -Path $store, $project -Force | Out-Null
    $manager = Start-TestApp
    $actionId = [guid]::NewGuid().ToString()
    $processId = [guid]::NewGuid().ToString()
    $stepId = [guid]::NewGuid().ToString()
    $actions = @(@{
        id = $actionId; name = 'Marker action'; color = '#123456'; description = 'Must not execute'
        processes = @(@{
            id = $processId; name = 'Test process'; workingDirectory = $project
            steps = @(@{ id = $stepId; command = "cmd /c echo executed > `"$marker`""; dependsOnSuccessOf = $null })
        })
    }) | ConvertTo-Json -Depth 10
    # ConvertTo-Json unwraps single-element pipelines: preserve the array.
    if (-not $actions.TrimStart().StartsWith('[')) { $actions = "[$actions]" }
    Set-Value (By-Id $manager 'NameBox') 'Phase3 original'
    Set-Value (By-Id $manager 'DirectoryBox') $project
    Set-Value (By-Id $manager 'ConfigurationBox') $actions
    Save-Draft $manager
    Select-Project $manager 'Phase3 original'
    $label = By-Name (By-Id $manager 'ActionsList') 'Marker action — #123456 — Must not execute'
    if (-not $label) { throw 'Saved action not displayed.' }
    Click (By-Name $manager 'Edit selected')
    Set-Value (By-Id $manager 'NameBox') 'Phase3 edited'
    $editedActions = $actions.Replace('Marker action', 'Edited marker action')
    Set-Value (By-Id $manager 'ConfigurationBox') $editedActions
    Save-Draft $manager
    Select-Project $manager 'Phase3 edited'
    Click (By-Name $manager 'Edit selected')
    if ((Value (By-Id $manager 'ConfigurationBox')) -notmatch 'Edited marker action') { throw 'Edited action was not persisted.' }
    # Change the file behind the editor and verify the stale revision is rejected.
    $files = @(Get-ChildItem -LiteralPath $store -Filter '*.json' -File)
    if ($files.Count -ne 1) { throw "Expected exactly one saved project; found $($files.Count)." }
    $external = [System.IO.File]::ReadAllText($files[0].FullName).Replace('Must not execute', 'External change')
    [System.IO.File]::WriteAllText($files[0].FullName, $external)
    Set-Value (By-Id $manager 'NameBox') 'Stale edit'
    Click (By-Name $manager 'Review and save…')
    Click (By-Name (Window-ForProcess 'Review before saving') 'Save')
    $status = Value (By-Id $manager 'ErrorsBox')
    if ($status -notmatch 'changed or already exists') { throw "Expected revision conflict, got: $status" }
    if ([System.IO.File]::ReadAllText($files[0].FullName) -notmatch 'External change') { throw 'Conflict overwrote external data.' }
    if ((Value (By-Id $manager 'NameBox')) -ne 'Stale edit') { throw 'Conflict discarded the unsaved edit.' }
    Assert-NoMarker
    Click (By-Name $manager 'Close')
    Stop-TestApp
    # A corrupt sibling must not suppress a valid project.
    Set-Content -LiteralPath (Join-Path $store 'broken.json') -Value '{' -Encoding UTF8
    $manager = Start-TestApp
    Select-Project $manager 'Phase3 edited'
    if ((Value (By-Id $manager 'ErrorsBox')) -notmatch 'broken.json') { throw 'Corrupt project error was not displayed.' }
    Click (By-Name $manager 'Edit selected')
    if ((Value (By-Id $manager 'ConfigurationBox')) -notmatch 'Edited marker action') { throw 'Restart lost edited action.' }
    Assert-NoMarker
    'PASS: isolated create, review, save, edit, revision conflict, restart, action display, corrupt sibling, and no marker execution.'
}
finally {
    Stop-TestApp
    if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force }
}
