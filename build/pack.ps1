<#
.SYNOPSIS
    Produces the PivotScope deliverable: a self-contained folder and its zip.

.DESCRIPTION
    ExcelDnaPack merges all managed assemblies into a single .xll
    (~6 MB). The NATIVE DLLs, however, are not embedded: the
    ExcelDnaPackNativeLibraryDependencies property is set in the .csproj but has
    no observable effect with ExcelDna.AddIn 1.9. So they are placed in
    runtimes\win-x64\native\, where .NET resolves them.

    Result: 4 files instead of the 76 in the build folder.

.EXAMPLE
    pwsh build\pack.ps1
    pwsh build\pack.ps1 -Version 0.3.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo

try {
    # Excel keeps the .xll locked: better to say so right away than to
    # let MSBuild fail with an UnauthorizedAccessException.
    if (Get-Process -Name EXCEL -ErrorAction SilentlyContinue) {
        throw 'Excel est ouvert et verrouille le .xll. Fermez-le, ou décochez ' +
              'PivotScope dans Options → Compléments → Atteindre.'
    }

    Write-Host '== SPA ==' -ForegroundColor Cyan
    npm --prefix src/PivotScope.Web ci
    npm --prefix src/PivotScope.Web run build
    if ($LASTEXITCODE -ne 0) { throw 'Build de la SPA en échec.' }

    Write-Host '== Add-in ==' -ForegroundColor Cyan
    $versionArg = if ($Version) { "-p:Version=$Version" } else { '' }
    dotnet publish src/PivotScope.AddIn -c $Configuration -p:SkipSpaBuild=true $versionArg
    if ($LASTEXITCODE -ne 0) { throw 'Publish de l''add-in en échec.' }

    $bin = "src/PivotScope.AddIn/bin/$Configuration/net10.0-windows"
    $packed = Join-Path $bin 'publish/PivotScope64-packed.xll'
    if (-not (Test-Path $packed)) { throw "Introuvable : $packed" }

    $out = 'artifacts/PivotScope'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    $native = Join-Path $out 'runtimes/win-x64/native'
    New-Item -ItemType Directory -Path $native -Force | Out-Null

    Copy-Item $packed (Join-Path $out 'PivotScope64.xll')
    Copy-Item "$bin/runtimes/win-x64/native/*.dll" $native

    Copy-Item 'README.md' $out
    Copy-Item 'LICENSE' $out

    $zip = 'artifacts/PivotScope.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path "$out/*" -DestinationPath $zip

    Write-Host ''
    Write-Host '== Livrable ==' -ForegroundColor Green
    Get-ChildItem $out -Recurse -File |
        Select-Object @{n = 'Fichier'; e = { Resolve-Path -Relative $_.FullName } },
                      @{n = 'Mo'; e = { [math]::Round($_.Length / 1MB, 2) } } |
        Format-Table -AutoSize

    Write-Host "Zip : $zip" -ForegroundColor Green
    Write-Host ''
    Write-Host 'Vérification qui compte : extraire ce zip dans un dossier ISOLÉ' -ForegroundColor Yellow
    Write-Host '(hors du dépôt) et y charger le .xll. Un livrable qui ne marche' -ForegroundColor Yellow
    Write-Host 'que depuis bin\ n''est pas un livrable.' -ForegroundColor Yellow
}
finally {
    Pop-Location
}
