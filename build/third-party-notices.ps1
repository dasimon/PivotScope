<#
.SYNOPSIS
    Writes THIRD-PARTY-NOTICES.txt for the deliverable.

.DESCRIPTION
    The add-in ships other people's code: the NuGet packages merged into the
    .xll (Excel-DNA, ADOMD, SQLite, ...) and the npm packages bundled into the
    pane (Monaco, Vue, DOMPurify, ...). Their licenses ask for their notices to
    travel with the software.

    Generated from what is actually resolved, never maintained by hand: a list
    written once drifts with the first upgrade. NuGet packages come from
    `dotnet list package --include-transitive` on the add-in project, npm
    packages from `npm ls --omit=dev`. The npm side can list a few packages
    that end up tree-shaken out of the bundle: listing one too many is
    harmless, forgetting one is not.

    Requires a restored add-in project and an installed node_modules.

.EXAMPLE
    pwsh build\third-party-notices.ps1 -Output artifacts\PivotScope\THIRD-PARTY-NOTICES.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo

try {
    $text = [System.Text.StringBuilder]::new()
    $rule = '=' * 78

    function Add-Entry([string]$Name, [string]$Version, [string]$License, [string]$Url,
                       [string]$Copyright, [string]$Body) {
        [void]$text.AppendLine($rule)
        [void]$text.AppendLine("$Name $Version")
        if ($License)   { [void]$text.AppendLine("License: $License") }
        if ($Url)       { [void]$text.AppendLine("URL: $Url") }
        if ($Copyright) { [void]$text.AppendLine($Copyright) }
        if ($Body) {
            [void]$text.AppendLine()
            [void]$text.AppendLine($Body.Trim())
        }
        [void]$text.AppendLine()
    }

    [void]$text.AppendLine('PivotScope - third-party notices')
    [void]$text.AppendLine()
    [void]$text.AppendLine('PivotScope is distributed under the MIT license (see LICENSE). It includes')
    [void]$text.AppendLine('the components below, each under its own license.')
    [void]$text.AppendLine()

    # --- NuGet: what the add-in project resolves -----------------------------
    $listing = dotnet list src/PivotScope.AddIn/PivotScope.AddIn.csproj package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw 'dotnet list package failed: restore the add-in project first.' }
    $graph = ($listing | Out-String) | ConvertFrom-Json

    $packages = @{}
    foreach ($project in $graph.projects) {
        foreach ($framework in $project.frameworks) {
            foreach ($p in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($p -and $p.id) { $packages["$($p.id)|$($p.resolvedVersion)"] = $p }
            }
        }
    }

    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }

    [void]$text.AppendLine('NuGet packages')
    [void]$text.AppendLine()
    foreach ($p in $packages.Values | Sort-Object id) {
        $folder = Join-Path $cache (Join-Path $p.id.ToLowerInvariant() $p.resolvedVersion)
        $nuspec = Get-ChildItem $folder -Filter *.nuspec -ErrorAction SilentlyContinue | Select-Object -First 1
        $license = $null; $url = $null; $copyright = $null; $body = $null

        if ($nuspec) {
            [xml]$xml = Get-Content $nuspec.FullName -Raw
            $meta = $xml.package.metadata
            $copyright = $meta.copyright
            $url = if ($meta.projectUrl) { $meta.projectUrl } else { $meta.licenseUrl }
            $node = $meta.license
            if ($node -and $node.type -eq 'expression') {
                $license = $node.'#text'
            } elseif ($node -and $node.type -eq 'file') {
                $license = 'see below'
                $file = Join-Path $folder $node.'#text'
                if (Test-Path $file) { $body = Get-Content $file -Raw }
            } elseif ($meta.licenseUrl) {
                $license = "see $($meta.licenseUrl)"
            }
        }

        Add-Entry $p.id $p.resolvedVersion $license $url $copyright $body
    }

    # --- npm: what the pane bundles -------------------------------------------
    $web = 'src/PivotScope.Web'
    # npm ls exits non-zero on any tree warning (an override, a peer): the
    # paths it prints are still the installed ones. Windows PowerShell 5.1
    # would turn its stderr into a terminating error under 'Stop'.
    $ErrorActionPreference = 'Continue'
    $paths = npm ls --omit=dev --all --parseable --prefix $web 2>$null
    $ErrorActionPreference = 'Stop'
    $root = (Resolve-Path $web).Path

    [void]$text.AppendLine('npm packages (task pane)')
    [void]$text.AppendLine()
    $seen = @{}
    foreach ($path in $paths | Where-Object { $_ -and $_ -ne $root } | Sort-Object -Unique) {
        $manifest = Join-Path $path 'package.json'
        if (-not (Test-Path $manifest)) { continue }
        $pkg = Get-Content $manifest -Raw | ConvertFrom-Json
        $key = "$($pkg.name)|$($pkg.version)"
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true

        $licenseFile = Get-ChildItem $path -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(LICEN[CS]E|COPYING|NOTICE)' } |
            Select-Object -First 1
        $body = if ($licenseFile) { Get-Content $licenseFile.FullName -Raw } else { $null }
        $license = if ($pkg.license -is [string]) { $pkg.license } else { $pkg.license.type }
        $url = if ($pkg.homepage) { $pkg.homepage } else { $null }

        Add-Entry $pkg.name $pkg.version $license $url $null $body
    }

    # Resolved against the repository, relative or absolute alike.
    $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    # UTF-8: license texts carry names with accents.
    [System.IO.File]::WriteAllText($target, $text.ToString(), [System.Text.UTF8Encoding]::new($false))

    Write-Host "Notices: $Output ($($packages.Count) NuGet, $($seen.Count) npm packages)"
}
finally {
    Pop-Location
}
