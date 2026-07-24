# Builds a differential-update release package for CICMessenger.
#
# Layout produced (under dist/):
#   CICMessenger/                     the install folder — root holds ONLY the launcher
#   CICMessenger/CICMessenger.exe     tiny launcher users double-click
#   CICMessenger/app/                 the real self-contained app + runtime (~400 files)
#   CICMessenger/app/manifest.json    per-file SHA256 list, read by the in-app updater
#   release-assets/                   files to attach to the GitHub release
#
# Usage (from the repo root, in PowerShell):
#   ./Scripts/build-release.ps1
#
# It prints the exact `gh release create` command to run afterwards.

$ErrorActionPreference = "Stop"

$repoRoot     = Split-Path -Parent $PSScriptRoot
$uiProj       = Join-Path $repoRoot "CICMessenger.UI/CICMessenger.UI.csproj"
$launcherProj = Join-Path $repoRoot "CICMessenger.Launcher/CICMessenger.Launcher.csproj"
$distRoot     = Join-Path (Split-Path -Parent $repoRoot) "dist"
$installDir   = Join-Path $distRoot "CICMessenger"      # root of the install (has the launcher)
$appDir       = Join-Path $installDir "app"             # the real app lives here
$assetDir     = Join-Path $distRoot "release-assets"

# --- version from Directory.Build.props ---
[xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$version = ($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
$verParts = $version.Split(".")
$displayVer = "v$($verParts[0]).$($verParts[1])"
$tag = "v$version"
Write-Host "Building CICMessenger $version (tag $tag)" -ForegroundColor Cyan

# --- clean ---
if (Test-Path $distRoot) { Remove-Item $distRoot -Recurse -Force }
New-Item -ItemType Directory -Path $appDir | Out-Null

# --- publish the real app into app\ (multi-file so updates replace only what changed) ---
dotnet publish $uiProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false `
    -o $appDir
if ($LASTEXITCODE -ne 0) { throw "app publish failed" }

# --- publish the launcher into the install root (single tiny exe) ---
dotnet publish $launcherProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true `
    -o $installDir
if ($LASTEXITCODE -ne 0) { throw "launcher publish failed" }
# keep the root clean: drop the launcher's stray pdb/debug files
Get-ChildItem $installDir -File | Where-Object { $_.Name -ne "CICMessenger.exe" } | Remove-Item -Force

# --- manifest.json over app\ (paths relative to app\, which is the app's base dir) ---
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
# UTF-8 WITHOUT a BOM — a BOM makes JsonDocument.Parse in the updater throw.
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5),
    (New-Object System.Text.UTF8Encoding $false))
Write-Host "  $($files.Count) files listed" -ForegroundColor Gray

# --- assemble release assets ---
New-Item -ItemType Directory -Path $assetDir | Out-Null

# 1. manifest.json
Copy-Item $manifestPath (Join-Path $assetDir "manifest.json")

# 2. app files individually (small, change every release) — root-level CICMessenger.* in app\
Get-ChildItem $appDir -File | Where-Object { $_.Name -like "CICMessenger*" -and $_.Name -ne "manifest.json" } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $assetDir $_.Name) }

# 3. full setup zip (launcher + app\) for first install / runtime changes
$zipName = "CICMessenger-$displayVer-setup.zip"
Compress-Archive -Path $installDir -DestinationPath (Join-Path $assetDir $zipName) -Force

$appAssetCount = (Get-ChildItem $assetDir -File).Count
Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Install folder : $installDir  (root = CICMessenger.exe only)" -ForegroundColor Gray
Write-Host "  Assets         : $assetDir ($appAssetCount files)" -ForegroundColor Gray
Write-Host ""
Write-Host "Publish the release with:" -ForegroundColor Cyan
Write-Host "  gh release create $tag `"$assetDir\*`" --repo vhgminh82/cicmessenger --title `"CICMessenger $displayVer`" --notes `"Mo ta thay doi`"" -ForegroundColor Yellow
