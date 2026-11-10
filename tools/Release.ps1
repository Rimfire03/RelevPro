<#
.SYNOPSIS
    Release complete de RelevPro : publish, deplacement de LatoFont, signature, zip, release GitHub.

.DESCRIPTION
    Version = <Version> de src\RelevPro\RelevPro.csproj (a incrementer et committer AVANT).
    Etapes : controles (arbre propre, commits pousses, tag libre) -> dotnet publish -> LatoFont dans
    RelevData\ -> tools\Sign-Release.ps1 -> zip RelevPro-v<version>-win-x64.zip -> MSI RelevPro-v<version>-win-x64.msi (Build-Installer.ps1) -> notes (fournies, ou
    generees en local par Ollama a partir des commits depuis le dernier tag) -> gh release create.
    Sortie volontairement courte. Les notes generees sont ecrites dans le dossier temporaire.

.EXAMPLE
    .\tools\Release.ps1 -NoPublish                 # tout sauf la publication GitHub (test)
    .\tools\Release.ps1 -NotesFile notes.md
    .\tools\Release.ps1 -Notes "Correctif X"
#>
param(
    [string]$Notes,
    [string]$NotesFile,
    [switch]$NoPublish,
    [ValidateSet("main","dev")][string]$Channel = "main",
    [string]$Repo = "Rimfire03/RelevPro"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root "src\RelevPro\RelevPro.csproj"
$publishDir = Join-Path $root "src\RelevPro\bin\Release\net8.0-windows\win-x64\publish"

function Git { $ErrorActionPreference = "Continue"; & git.exe -C $root @args 2>&1 | ForEach-Object { "$_" } }
function Step([string]$m) { Write-Host "[release] $m" }

# --- Version
$m = [regex]::Match([IO.File]::ReadAllText($csproj), '<Version>(\d+\.\d+\.\d+)</Version>')
if (-not $m.Success) { throw "Balise <Version> introuvable dans $csproj" }
$version = $m.Groups[1].Value
$dev = ($Channel -eq "dev")
$tag = if ($dev) { "v$version-dev" } else { "v$version" }
Step "version $version"

# --- Controles
if (-not $NoPublish) {
    if (@(Git status --porcelain).Count -gt 0) { throw "Arbre de travail non propre : committer d'abord (git-commit.ps1)." }
    Git fetch --quiet | Out-Null
    $ahead = [int](Git rev-list --count "@{u}..HEAD")
    if ($ahead -gt 0) { throw "$ahead commit(s) non pousse(s) : pousser d'abord (git-sync.ps1)." }
    # "release not found" sort sur stderr : sous $ErrorActionPreference = Stop (PowerShell 5.1), c'est une erreur
    # terminante alors que c'est le cas normal ici. On ne se fie donc qu'au code de sortie.
    $ErrorActionPreference = "Continue"
    & gh.exe release view $tag --repo $Repo 2>&1 | Out-Null
    $exists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = "Stop"
    if ($exists) { throw "La release $tag existe deja sur $Repo : incrementer <Version>." }
}

# --- Publish
Step "dotnet publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& dotnet publish (Join-Path $root "src\RelevPro\RelevPro.csproj") -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true -p:DebugType=None $(if ($dev) { "-p:ReleaseChannel=dev" }) `
    --nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a echoue (code $LASTEXITCODE)." }

# --- LatoFont -> RelevData\LatoFont
$lato = Join-Path $publishDir "LatoFont"
if (Test-Path $lato) {
    $dest = Join-Path $publishDir "RelevData\LatoFont"
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Move-Item $lato $dest
    Step "LatoFont deplace dans RelevData\"
}

# --- Signature (obligatoire avant le zip)
Step "signature"
& (Join-Path $PSScriptRoot "Sign-Release.ps1") | Select-Object -Last 3 | ForEach-Object { Write-Host "  $_" }
$exe = Join-Path $publishDir "RelevPro.exe"
if (-not (Get-AuthenticodeSignature $exe).SignerCertificate) { throw "RelevPro.exe n'est pas signe : zip annule." }

# --- Zip
$work = Join-Path $env:TEMP "relevpro-release-$version"
New-Item -ItemType Directory -Force $work | Out-Null
$zip = Join-Path $work "RelevPro-$tag-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip -CompressionLevel Optimal
Step ("zip {0} ({1:N0} Mo)" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB))

# --- Installateur MSI (par machine ; licence dans le registre) - exe deja signe, MSI signe a son tour
Step "installateur MSI"
$msi = Join-Path $work "RelevPro-$tag-win-x64.msi"
& (Join-Path $PSScriptRoot "Build-Installer.ps1") -Version $version -OutFile $msi -PublishDir $publishDir | Select-Object -Last 1 | ForEach-Object { Write-Host "  $_" }
if (-not (Test-Path $msi)) { throw "MSI non genere." }

# --- Notes
$notesPath = Join-Path $work "notes.md"
if ($NotesFile) { Copy-Item $NotesFile $notesPath -Force }
elseif ($Notes) { [IO.File]::WriteAllText($notesPath, $Notes, (New-Object Text.UTF8Encoding($false))) }
else {
    $prev = (Git describe --tags --abbrev=0 "HEAD^" 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $prev) { $prev = $null }
    $range = if ($prev) { "$prev..HEAD" } else { "HEAD" }
    $log = (Git log $range --no-merges --format="- %s%n%b") -join "`n"
    if ($log.Length -gt 12000) { $log = $log.Substring(0, 12000) }
    $body = $null
    try {
        . "$env:USERPROFILE\.claude\tools\Invoke-LocalLlm.ps1"
        $body = Invoke-LocalLlm @"
Redige en francais les notes de release (markdown) de la version $version de l'application RelevPro, a partir de ces messages de commit. Regroupe en 2 a 6 puces claires pour l'utilisateur final, sans jargon technique, sans inventer de fonctionnalite, sans titre ni introduction. Reponds uniquement par les puces.

$log
"@
    } catch { Write-Warning "Notes locales indisponibles ($($_.Exception.Message)) : notes minimales." }
    if (-not $body) { $body = $log }
    [IO.File]::WriteAllText($notesPath, $body, (New-Object Text.UTF8Encoding($false)))
}

# --- Version du schema de base (derniere migration de DatabaseMigrations.All) : marqueur lu par l'application
# pour ne proposer un retour Dev -> Main que si la base est compatible.
$migText = [IO.File]::ReadAllText((Join-Path $root "src\RelevPro.Core\Data\Migrations\DatabaseMigrations.cs"))
$allBlock = [regex]::Match($migText, 'All\s*=\s*\r?\n\s*\{(?<b>.*?)\r?\n\s*\};', 'Singleline')
$schema = $null
if ($allBlock.Success) {
    $nums = [regex]::Matches($allBlock.Groups['b'].Value, '(?m)^ {8}\((\d+),') | ForEach-Object { [int]$_.Groups[1].Value }
    if ($nums) { $schema = ($nums | Measure-Object -Maximum).Maximum }
}
if (-not $schema) { throw "Version du schema de base introuvable dans DatabaseMigrations.cs." }
[IO.File]::AppendAllText($notesPath, "`n`n<!-- db-schema: $schema -->`n", (New-Object Text.UTF8Encoding($false)))
Step "schema de base $schema"
if ($NoPublish) {
    Write-Host "[release] -NoPublish : arret avant GitHub. Zip : $zip ; MSI : $msi ; notes : $notesPath"
    return
}

# --- Publication
Step "gh release create $tag"
$sha = (Git rev-parse HEAD | Select-Object -First 1).Trim()
& gh.exe release create $tag $zip $msi --repo $Repo --target $sha --title "Version $version$(if ($dev) { ' (dev)' })" --notes-file $notesPath $(if ($dev) { "--prerelease" } else { "--latest" })
if ($LASTEXITCODE -ne 0) { throw "gh release create a echoue (code $LASTEXITCODE)." }
Write-Host "[release] OK : https://github.com/$Repo/releases/tag/$tag"
