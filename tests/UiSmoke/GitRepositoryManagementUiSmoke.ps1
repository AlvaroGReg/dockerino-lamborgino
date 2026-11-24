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
$oldPipeName = $env:DOCKERINO_PIPE_NAME
$env:DOCKERINO_PIPE_NAME = 'Dockerino.GitUiSmoke.' + [guid]::NewGuid().ToString('N')

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
function Find-NameLike($parent, [string]$pattern) {
    $all = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $all) { if ($item.Current.Name -like $pattern) { return $item } }
    return $null
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

    $project = Join-Path $PSScriptRoot '..\..\src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj'
    & dotnet build $project --nologo "-p:OutputPath=$output\"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $exe = Join-Path $output 'DockerinoLamborgino.App.exe'
    $app = Start-Process -FilePath $exe -ArgumentList @('--project-storage', "`"$store`"") -PassThru
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $main = Wait-For {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $app.Id)
        $windows = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        foreach ($item in $windows) { if ($item.Current.Name -eq 'dockerino-lamborgino') { return $item } }
    } 'dashboard window'

    # Git is an inline project tool, not a separate window. Select the fixture
    # project before opening its repository expander.
    Select-Item (Wait-For { Find-Name (Find-Id $main 'ProjectsList') 'Smoke project' } 'Smoke project row')
    $expander = Wait-For { Find-Id $main 'GitExpander' } 'Git repositories expander'
    $expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $repositories = Wait-For { Find-Id $main 'Repositories' } 'associated repository list'
    $repoName = Wait-For { Find-Name $repositories 'Smoke project (project)' } 'repository entry'
    $branchSummary = Wait-For { Find-Name $repositories 'Current branch: main' } 'current branch summary'
    Wait-For { Find-NameLike $repositories '*untracked.txt*' } 'untracked file status' | Out-Null

    $branches = Wait-For { Find-Id $repositories 'BranchSelector' } 'repository branch selector'
    $branches.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $feature = Wait-For {
        $items = $branches.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($item in $items) { if ($item.Current.Name -match 'feature$') { return $item } }
    } 'local feature branch option'
    Select-Item $feature
    Wait-For { (Git $repo @('branch', '--show-current')) -eq 'feature' } 'branch switch to feature' | Out-Null
    Wait-For { Find-Name $repositories 'Current branch: feature' } 'updated current branch' | Out-Null
    Wait-For { Find-NameLike $repositories '*untracked.txt*' } 'untracked status after branch switch' | Out-Null

    Invoke-Button (Wait-For { Find-Id $main 'RefreshButton' } 'Git refresh button')
    Wait-For {
        $button = Find-Id $main 'RefreshButton'
        if ($button -and $button.Current.IsEnabled) { return $true }
    } 'manual Git refresh completion' | Out-Null
    Require ($null -ne (Find-Name $repositories 'Current branch: feature')) 'Refresh lost the selected branch.'
    Require ($null -ne (Find-NameLike $repositories '*untracked.txt*')) 'Refresh lost the untracked-file status.'

    Select-Item (Wait-For { Find-Name (Find-Id $main 'ProjectsList') 'Empty project' } 'empty project row')
    Wait-For {
        if (Find-Name $main 'No Git repositories are associated with this project.') { return $true }
    } 'empty project repository state' | Out-Null
    Require ($null -eq (Find-Name (Find-Id $main 'Repositories') 'Smoke project (project)')) `
        'The previous project repository remained visible.'
    Select-Item (Wait-For { Find-Name (Find-Id $main 'ProjectsList') 'Smoke project' } 'reselected project row')
    Wait-For { Find-Name (Find-Id $main 'Repositories') 'Smoke project (project)' } 'repository after project reselection' | Out-Null
    Write-Host 'PASS: inline Git panel, project-scoped repository status, branch selector, local branch switch, refresh, and project reselection.'
}
finally {
    if ($app -and -not $app.HasExited) {
        $app.CloseMainWindow() | Out-Null
        if (-not $app.WaitForExit(5000)) { $app.Kill(); $app.WaitForExit() }
    }
    $env:GIT_CONFIG_GLOBAL = $oldGitGlobal
    $env:GIT_CONFIG_NOSYSTEM = $oldGitSystem
    $env:GIT_TERMINAL_PROMPT = $oldTerminalPrompt
    $env:DOCKERINO_PIPE_NAME = $oldPipeName
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
