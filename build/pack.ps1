<#
.SYNOPSIS
    Produit le livrable PivotScope : un dossier autonome et son zip.

.DESCRIPTION
    ExcelDnaPack fusionne toutes les assemblies managées dans un seul .xll
    (~6 Mo). Les DLL NATIVES, elles, ne sont pas embarquées : la propriété
    ExcelDnaPackNativeLibraryDependencies est posée dans le .csproj mais reste
    sans effet observable avec ExcelDna.AddIn 1.9. On les place donc dans
    runtimes\win-x64\native\, là où .NET les résout.

    Résultat : 4 fichiers au lieu des 76 du dossier de build.

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
    # Excel garde le .xll verrouillé : mieux vaut le dire tout de suite que de
    # laisser MSBuild échouer sur un UnauthorizedAccessException.
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
