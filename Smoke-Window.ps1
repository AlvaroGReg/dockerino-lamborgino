$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$exe = Join-Path $PSScriptRoot 'src\DockerinoLamborgino.App\bin\Debug\net10.0-windows\DockerinoLamborgino.App.exe'
if (-not (Test-Path $exe)) { throw "Build Debug first: $exe" }
$app = Start-Process -FilePath $exe -PassThru
try {
    $window = $null
    for ($i = 0; $i -lt 100 -and -not $window; $i++) {
        Start-Sleep -Milliseconds 100
        $app.Refresh()
        if ($app.HasExited) { throw "Application exited unexpectedly: $($app.ExitCode)" }
        if ($app.MainWindowHandle -ne 0) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
        }
    }
    if (-not $window) { throw 'Window did not appear.' }
    function Invoke-Button([string]$name) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (-not $button) { throw "Button not found: $name" }
        $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    }
    Invoke-Button 'Collapse'
    Invoke-Button 'Expand'
    Invoke-Button 'Hide menu'
    Invoke-Button 'Show menu'
    Invoke-Button 'New terminal'
    Invoke-Button 'New terminal'
    $items = @()
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    for ($i = 0; $i -lt 150; $i++) {
        Start-Sleep -Milliseconds 100
        $items = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition))
        if ($items.Count -eq 2 -and @($items | Where-Object { $_.Current.Name -like '*Terminal running*' }).Count -eq 2) { break }
    }
    if ($items.Count -ne 2 -or @($items | Where-Object { $_.Current.Name -like '*Terminal running*' }).Count -ne 2) {
        throw "Both terminals did not reach running state: $($items.Current.Name -join ', ')"
    }
    Invoke-Button 'Hide menu'
    Invoke-Button 'Show menu'
    Invoke-Button 'Collapse'
    Invoke-Button 'Expand'
    $items = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition))
    if (@($items | Where-Object { $_.Current.Name -like '*Terminal running*' }).Count -ne 2) {
        throw 'A terminal did not remain running after hiding the menu and collapsing.'
    }
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Invoke-Button 'Stop'
    $items = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition))
    if ($items[0].Current.Name -notlike '*Terminal stopped*' -or $items[1].Current.Name -notlike '*Terminal running*') {
        throw "Stopping one terminal changed unexpected states: $($items.Current.Name -join ', ')"
    }
    $items[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if ($app.HasExited) { throw 'Application exited while opening terminals.' }
    Invoke-Button 'Exit'
    if (-not $app.WaitForExit(10000)) { throw 'Application did not exit.' }
    if ($app.ExitCode -ne 0) { throw "Application exited with code $($app.ExitCode)" }
    'PASS: window, collapse/expand, menu toggle, two running terminals, selection, individual stop, and app exit.'
    'Running is the application-reported state; this does not verify a visible prompt, keyboard interaction, or OS-level isolation.'
}
finally {
    $app.Refresh()
    if (-not $app.HasExited) {
        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(5000)) { Stop-Process -Id $app.Id -Force }
    }
}
