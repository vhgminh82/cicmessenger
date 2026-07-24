# Builds a differential-update release package for CICMessenger.
#
# Produces, under dist/:
#   CICMessenger/                 the runnable app folder (double-click CICMessenger.exe)
#   CICMessenger/manifest.json    per-file SHA256 list, read by the in-app updater
#   CICMessenger-v<ver>-full.zip  the whole folder, for first install / runtime changes
#   release-assets/               the files to attach to the GitHub release
#
# Usage (from the repo root, in PowerShell):
#   ./Scripts/build-release.ps1
#
# It prints the exact `gh release create` command to run afterwards.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$uiProj   = Join-Path $repoRoot "CICMessenger.UI/CICMessenger.UI.csproj"
$distRoot = Join-Path (Split-Path -Parent $repoRoot) "dist"
$appDir   = Join-Path $distRoot "CICMessenger"
$assetDir = Join-Path $distRoot "release-assets"

# --- version from Directory.Build.props ---
[xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$version = ($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
$verParts = $version.Split(".")
$displayVer = "v$($verParts[0]).$($verParts[1])"      # e.g. v0.12  (shown to users)
$tag = "v$version"                                     # e.g. v0.12.0 (git/release tag)
Write-Host "Building CICMessenger $version (tag $tag)" -ForegroundColor Cyan

# --- clean + publish as a folder (NOT single file) ---
if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
New-Item -ItemType Directory -Path $appDir | Out-Null

dotnet publish $uiProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false `
    -o $appDir
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# --- manifest.json: every file with its SHA256 + size ---
Write-Host "Generating manifest..." -ForegroundColor Cyan
$files = @()
Get-ChildItem $appDir -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($appDir.Length + 1) -replace "\\", "/"
    if ($rel -eq "manifest.json") { return }
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    $files += [ordered]@{ path = $rel; sha256 = $hash; size = $_.Length }
}
$manifest = [ordered]@{ version = $version; tag = $tag; files = $files }
$manifestPath = Join-Path $appDir "manifest.json"
# Write UTF-8 WITHOUT a BOM — a BOM makes JsonDocument.Parse in the updater throw.
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5),
    (New-Object System.Text.UTF8Encoding $false))
Write-Host "  $($files.Count) files listed" -ForegroundColor Gray

# --- assemble release assets ---
New-Item -ItemType Directory -Path $assetDir | Out-Null

# 1. manifest.json
Copy-Item $manifestPath (Join-Path $assetDir "manifest.json")

# 2. app files individually (small, change every release) — root-level CICMessenger.* only
Get-ChildItem $appDir -File | Where-Object { $_.Name -like "CICMessenger*" -and $_.Name -ne "manifest.json" } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $assetDir $_.Name) }

# 3. full zip (first install / when a runtime file changes)
$zipName = "CICMessenger-$displayVer-full.zip"
$zipPath = Join-Path $assetDir $zipName
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zipPath -Force

$appAssetCount = (Get-ChildItem $assetDir -File).Count
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  App folder : $appDir" -ForegroundColor Gray
Write-Host "  Assets     : $assetDir ($appAssetCount files)" -ForegroundColor Gray
Write-Host ""
Write-Host "Publish the release with:" -ForegroundColor Cyan
Write-Host "  gh release create $tag `"$assetDir\*`" --repo vhgminh82/cicmessenger --title `"CICMessenger $displayVer`" --notes `"Mo ta thay doi`"" -ForegroundColor Yellow
