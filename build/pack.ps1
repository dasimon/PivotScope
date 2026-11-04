<#
.SYNOPSIS
    Produces the PivotScope deliverable: a self-contained folder and its zip.

.DESCRIPTION
    ExcelDnaPack merges all managed assemblies into a single .xll
    (~6 MB). The NATIVE DLLs, however, are not embedded: the
    ExcelDnaPackNativeLibraryDependencies property is set in the .csproj but has
    no observable effect with ExcelDna.AddIn 1.9. So they are placed in
    runtimes\win-x64\native\, where .NET resolves them.

    Result: 5 files instead of the 76 in the build folder (with the
    third-party notices).

.EXAMPLE
    pwsh build\pack.ps1
    pwsh build\pack.ps1 -Version 0.3.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    # 'release' only from release.yml: it is what makes the shipped add-in look for updates
    # (AssemblyMetadata UpdateChannel). A local pack leaves it empty and never asks GitHub.
    [string]$Channel
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo

try {
    # Excel keeps the .xll locked: better to say so right away than to
    # let MSBuild fail with an UnauthorizedAccessException.
    if (Get-Process -Name EXCEL -ErrorAction SilentlyContinue) {
        throw 'Excel is open and locks the .xll. Close it, or uncheck ' +
              'PivotScope in Options → Add-ins → Go.'
    }

    Write-Host '== SPA ==' -ForegroundColor Cyan
    npm --prefix src/PivotScope.Web ci
    npm --prefix src/PivotScope.Web run build
    if ($LASTEXITCODE -ne 0) { throw 'SPA build failed.' }

    Write-Host '== Add-in ==' -ForegroundColor Cyan
    $publishArgs = @('-c', $Configuration, '-p:SkipSpaBuild=true')
    if ($Version) { $publishArgs += "-p:Version=$Version" }
    if ($Channel) { $publishArgs += "-p:UpdateChannel=$Channel" }
    dotnet publish src/PivotScope.AddIn @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Add-in publish failed.' }

    $bin = "src/PivotScope.AddIn/bin/$Configuration/net10.0-windows"
    $packed = Join-Path $bin 'publish/PivotScope64-packed.xll'
    if (-not (Test-Path $packed)) { throw "Not found: $packed" }

    $out = 'artifacts/PivotScope'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    $native = Join-Path $out 'runtimes/win-x64/native'
    New-Item -ItemType Directory -Path $native -Force | Out-Null

    Copy-Item $packed (Join-Path $out 'PivotScope64.xll')
    Copy-Item "$bin/runtimes/win-x64/native/*.dll" $native

    Copy-Item 'README.md' $out
    Copy-Item 'LICENSE' $out

    # The licenses of the bundled components travel with the add-in.
    & (Join-Path $PSScriptRoot 'third-party-notices.ps1') -Output (Join-Path $out 'THIRD-PARTY-NOTICES.txt')

    $zip = 'artifacts/PivotScope.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$out/*" -DestinationPath $zip

    # The checksum the built-in update checks the zip against, in sha256sum's text format
    # ("<hash>  PivotScope.zip"), ASCII, one line: what CubeScope.Core's Sha256Sums reads.
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$hash  PivotScope.zip" -Encoding ascii

    Write-Host ''
    Write-Host '== Deliverable ==' -ForegroundColor Green
    Get-ChildItem $out -Recurse -File |
        Select-Object @{n = 'File'; e = { Resolve-Path -Relative $_.FullName } },
                      @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } |
        Format-Table -AutoSize

    Write-Host "Zip: $zip" -ForegroundColor Green
    Write-Host "SHA-256: $hash" -ForegroundColor Green
    Write-Host ''
    Write-Host 'The check that counts: extract this zip into an ISOLATED folder' -ForegroundColor Yellow
    Write-Host '(outside the repository) and load the .xll from there. A deliverable' -ForegroundColor Yellow
    Write-Host 'that only works from bin\ is not a deliverable.' -ForegroundColor Yellow
}
finally {
    Pop-Location
}
