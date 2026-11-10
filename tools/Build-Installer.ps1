<#
.SYNOPSIS
    Construit le paquet MSI de RelevPro (WiX 4, par machine) a partir du dossier publish, puis le signe.

.DESCRIPTION
    A lancer apres `dotnet publish`, deplacement de LatoFont et signature de l'exe (Release.ps1 enchaine tout).
    Prerequis : outil `wix` 4.0.x (`dotnet tool install --global wix --version 4.0.6`) et son extension Util
    (`wix extension add -g WixToolset.Util.wixext/4.0.6`). Le MSI embarque l'exe deja signe ; le MSI lui-meme
    est signe avec le certificat global (Sign-Release.ps1 -ExePath).

.EXAMPLE
    .\tools\Build-Installer.ps1 -Version 1.9.3 -OutFile C:\temp\RelevPro-v1.9.3-win-x64.msi
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$PublishDir
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $PublishDir) { $PublishDir = Join-Path $root "src\RelevPro\bin\Release\net8.0-windows\win-x64\publish" }
$wixExe = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
if (-not (Test-Path $wixExe)) { $wixExe = (Get-Command wix -ErrorAction Stop).Source }
$ico = Join-Path $root "src\RelevPro\Assets\app.ico"
$lato = Join-Path $PublishDir "RelevData\LatoFont"

# Les fichiers de police sont listes ici (l'element <Files> n'existe pas en WiX 4.0.6) : un composant par fichier.
$work = Join-Path $env:TEMP "relevpro-installer-$Version"
New-Item -ItemType Directory -Force $work | Out-Null
$frag = Join-Path $work "LatoFiles.wxs"
$files = @()
if (Test-Path $lato) {
    if (@(Get-ChildItem $lato -Directory).Count -gt 0) { throw "Sous-dossiers dans LatoFont : non pris en charge par Build-Installer.ps1." }
    $files = @(Get-ChildItem $lato -File)
}
$sb = New-Object Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><ComponentGroup Id="DataFiles" Directory="LATODIR">')
foreach ($f in $files) {
    $src = [Security.SecurityElement]::Escape($f.FullName)
    [void]$sb.AppendLine("<Component Guid=`"*`"><File Source=`"$src`" KeyPath=`"yes`" /></Component>")
}
[void]$sb.AppendLine('</ComponentGroup></Fragment></Wix>')
[IO.File]::WriteAllText($frag, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))

if (Test-Path $OutFile) { Remove-Item $OutFile -Force }
& $wixExe build (Join-Path $root "installer\Product.wxs") $frag -arch x64 -ext WixToolset.Util.wixext `
    -d "Version=$Version" -d "PublishDir=$PublishDir" -d "IconPath=$ico" -o $OutFile
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $OutFile)) { throw "wix build a echoue (code $LASTEXITCODE)." }

& (Join-Path $PSScriptRoot "Sign-Release.ps1") -ExePath $OutFile | Select-Object -Last 3 | ForEach-Object { Write-Host "  $_" }
if (-not (Get-AuthenticodeSignature $OutFile).SignerCertificate) { throw "Le MSI n'est pas signe." }
Write-Host ("[installer] {0} ({1:N1} Mo)" -f (Split-Path $OutFile -Leaf), ((Get-Item $OutFile).Length / 1MB))
