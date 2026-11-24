param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repository "artifacts\release\$Version"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) {
    throw "Output directory already exists: $OutputRoot. Choose a new directory."
}

$projects = @(
    @{ Name = 'app'; Project = 'src\DockerinoLamborgino.App\DockerinoLamborgino.App.csproj'; Exe = 'DockerinoLamborgino.App.exe' },
    @{ Name = 'cli'; Project = 'src\DockerinoLamborgino.Cli\DockerinoLamborgino.Cli.csproj'; Exe = 'DockerinoLamborgino.Cli.exe' }
)

foreach ($item in $projects) {
    $output = Join-Path $OutputRoot $item.Name
    $project = Join-Path $repository $item.Project
    & dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:Version=$Version" -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing $($item.Name) failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $output $item.Exe) -PathType Leaf)) {
        throw "Expected executable missing from $output."
    }

    # Read the exact runtime pack versions selected by this project's restore.
    $assetsPath = Join-Path (Split-Path -Parent $project) 'obj\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    $frameworks = $assets.project.frameworks.PSObject.Properties.Value
    $downloads = @($frameworks | ForEach-Object { $_.downloadDependencies })
    $requiredPacks = @('Microsoft.NETCore.App.Runtime.win-x64')
    if ($item.Name -eq 'app') {
        $requiredPacks += 'Microsoft.WindowsDesktop.App.Runtime.win-x64'
    }
    foreach ($packName in $requiredPacks) {
        $matches = @($downloads | Where-Object { $_.name -eq $packName })
        if ($matches.Count -ne 1 -or $matches[0].version -notmatch '^\[([^,]+),\s*\1\]$') {
            throw "Cannot determine the exact restored runtime pack version for $packName."
        }
        $packVersion = $Matches[1]
        $packPath = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder "$($packName.ToLowerInvariant())\$packVersion"
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $packPath = $candidate
                break
            }
        }
        if (-not $packPath) { throw "Runtime pack not found: $packName $packVersion." }
        $destination = Join-Path $OutputRoot "licenses\$($item.Name)\$packName\$packVersion"
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        if ($packName -eq 'Microsoft.NETCore.App.Runtime.win-x64') {
            $requiredFiles = @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')
        } else {
            $requiredFiles = @('LICENSE')
        }
        foreach ($name in $requiredFiles) {
            $source = Join-Path $packPath $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Required runtime notice missing: $source."
            }
            Copy-Item -LiteralPath $source -Destination $destination
        }
    }
}

Write-Host "Published Windows x64 application and CLI to $OutputRoot"
