# Run with Windows PowerShell: powershell.exe -NoProfile -STA -File tests/UiSmoke/GitRepositoryManagementUiSmoke.ps1
# Requires an interactive Windows desktop, .NET 10 SDK, and Git.
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run this script in an STA Windows PowerShell process.'
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$tempBase = Join-Path $env:TEMP 'opencode'
if (-not (Test-Path -LiteralPath $tempBase -PathType Container)) { $tempBase = $env:TEMP }
$root = Join-Path $tempBase ("dockerino-git-ui-smoke-" + [guid]::NewGuid().ToString('N'))
$app = $null
$oldGitGlobal = $env:GIT_CONFIG_GLOBAL
$oldGitSystem = $env:GIT_CONFIG_NOSYSTEM
$oldTerminalPrompt = $env:GIT_TERMINAL_PROMPT

function Git([string]$directory, [string[]]$gitArgs) {
    $output = & git.exe -C $directory @gitArgs 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $gitArgs failed: $output" }
    return ($output -join "`n")
}
function Wait-For([scriptblock]$condition, [string]$description) {
    $end = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $end)
    throw "Timed out waiting for $description"
}
function Find-Id($parent, [string]$id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-Name($parent, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Button($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Select-Item($element) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($element) {
        $pattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
            return
        }
        $element = $walker.GetParent($element)
    }
    throw 'No selectable item found for the requested element.'
}
function Text-Value($element) {
    return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

try {
    New-Item -ItemType Directory -Path $root | Out-Null
    $env:GIT_CONFIG_GLOBAL = Join-Path $root 'empty-gitconfig'
    $env:GIT_CONFIG_NOSYSTEM = '1'
    $env:GIT_TERMINAL_PROMPT = '0'
    $repo = Join-Path $root 'repo'
    $store = Join-Path $root 'store'
    $output = Join-Path $root 'build'
    New-Item -ItemType Directory -Path $repo, $store | Out-Null
    Git $repo @('init', '-b', 'main') | Out-Null
    Git $repo @('config', 'user.name', 'UI Smoke') | Out-Null
    Git $repo @('config', 'user.email', 'ui-smoke@example.invalid') | Out-Null
    $tree = Git $repo @('mktree')
    $commit = Git $repo @('commit-tree', $tree, '-m', 'initial')
    Git $repo @('update-ref', 'refs/heads/main', $commit) | Out-Null
    Git $repo @('branch', 'feature') | Out-Null
    Git $repo @('update-ref', 'refs/remotes/origin/remote-feature', 'HEAD') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'untracked.txt'), 'status fixture')

    $firstId = [guid]::NewGuid().ToString()
    $secondId = [guid]::NewGuid().ToString()
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $firstJson = @{ id = $firstId; name = 'Smoke project'; directory = $repo; repositories = @($repo); actions = @() } | ConvertTo-Json -Depth 8
    $secondJson = @{ id = $secondId; name = 'Empty project'; directory = $repo; repositories = @(); actions = @() } | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $store "$firstId.json"), $firstJson, $utf8)
    [IO.File]::WriteAllText((Join-Path $store "$secondId.json"), $secondJson, $utf8)

    $project = Join-Path $PSScriptRoot '..\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj'
    & dotnet build $project --nologo "-p:OutputPath=$output\"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $exe = Join-Path $output 'DockerinoLamborgino.App.exe'
    $app = Start-Process -FilePath $exe -ArgumentList @('--project-storage', "`"$store`"") -PassThru
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $main = Wait-For {
        $all = $desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)))
        foreach ($window in $all) { if ($window.Current.Name -eq 'dockerino-lamborgino') { return $window } }
    } 'main window'
    $open = Wait-For { Find-Name $main ("Manage Git" + [char]0x2026) } 'Manage Git button'
    Invoke-Button $open
    $window = Wait-For {
        $all = $desktop.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)))
        foreach ($item in $all) { if ($item.Current.Name -eq 'Git repositories') { return $item } }
    } 'Git window'
    $projects = Wait-For { Find-Id $window 'Projects' } 'project selector'
    $projects.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Select-Item (Wait-For { Find-Name $window 'Smoke project' } 'Smoke project option')
    $repositories = Wait-For { Find-Id $window 'Repositories' } 'repository list'
    $repoItem = Wait-For { Find-Name $repositories $repo } 'repository item'
    # The path can be exposed as a TextBlock inside the selectable ListBoxItem.
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($repoItem -and $repoItem.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $repoItem = $walker.GetParent($repoItem)
    }
    Require ($null -ne $repoItem) 'Repository list item was not selectable.'
    Select-Item $repoItem
    $status = Wait-For { Find-Id $window 'Status' } 'status field'
    Wait-For { if ((Text-Value $status) -match 'untracked\.txt') { return $true } } 'untracked status' | Out-Null
    $local = Find-Id $window 'LocalBranches'
    $remote = Find-Id $window 'RemoteBranches'
    Wait-For { Find-Name $local 'main' } 'local main branch' | Out-Null
    $feature = Wait-For { Find-Name $local 'feature' } 'local feature branch'
    Wait-For { Find-Name $remote 'origin/remote-feature' } 'remote branch' | Out-Null
    Select-Item $feature
    Invoke-Button (Find-Id $window 'SwitchButton')
    Wait-For { if ((Git $repo @('branch', '--show-current')) -eq 'feature') { return $true } } 'Git switch to feature' | Out-Null
    $refresh = Find-Id $window 'RefreshButton'
    Wait-For { if ($refresh.Current.IsEnabled) { return $true } } 'switch refresh completion' | Out-Null
    Require ((Text-Value $status) -match 'untracked\.txt') 'Status was not retained after switch.'
    Invoke-Button $refresh
    Wait-For { if ($refresh.Current.IsEnabled) { return $true } } 'manual refresh completion' | Out-Null
    Require ($null -ne (Find-Name $local 'feature')) 'Local branch missing after refresh.'
    Require ($null -ne (Find-Name $remote 'origin/remote-feature')) 'Remote branch missing after refresh.'
    Require ((Text-Value $status) -match 'untracked\.txt') 'Status missing after refresh.'
    $projects.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $smokeOption = Wait-For { Find-Name $window 'Smoke project' } 'selected project option'
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $selectionPattern = $null
    while ($smokeOption -and -not $smokeOption.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selectionPattern)) {
        $smokeOption = $walker.GetParent($smokeOption)
    }
    Require ($null -ne $selectionPattern -and $selectionPattern.Current.IsSelected) 'Project selection was not preserved.'
    Select-Item (Wait-For { Find-Name $window 'Empty project' } 'Empty project option')
    Wait-For { if ($refresh.Current.IsEnabled) { return $true } } 'empty project load' | Out-Null
    Require ($null -eq (Find-Name $repositories $repo)) 'Repository from previous project remained visible.'
    $projects.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Select-Item (Wait-For { Find-Name $window 'Smoke project' } 'Smoke project option again')
    Wait-For { Find-Name $repositories $repo } 'repository after reselecting project' | Out-Null
    Write-Host 'PASS: project selection, status, distinct local/remote branch lists, local switch, refresh, and project reselection.'
}
finally {
    if ($app -and -not $app.HasExited) {
        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(5000)) { $app.Kill(); $app.WaitForExit() }
    }
    $env:GIT_CONFIG_GLOBAL = $oldGitGlobal
    $env:GIT_CONFIG_NOSYSTEM = $oldGitSystem
    $env:GIT_TERMINAL_PROMPT = $oldTerminalPrompt
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
