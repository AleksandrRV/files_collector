[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$sourceRoot = Split-Path -Parent $PSScriptRoot
$packageRoot = Split-Path -Parent $sourceRoot
$projectPath = Join-Path $sourceRoot "FilesCollector.App/FilesCollector.App.csproj"
$artifactsRoot = Join-Path $sourceRoot ".artifacts/release"
$publishPath = Join-Path $artifactsRoot "publish"
$stagePath = Join-Path $artifactsRoot "files-collector"
$zipPath = Join-Path $artifactsRoot "FilesCollector-$Version-win-x64.zip"

if (Test-Path $artifactsRoot) {
    Remove-Item -Path $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

dotnet clean $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean failed with exit code $LASTEXITCODE."
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:Version=$Version `
    --output $publishPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $publishPath "FilesCollector.App.exe"
if (-not (Test-Path $publishedExecutable)) {
    throw "The publish operation did not produce FilesCollector.App.exe."
}

New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
Copy-Item -Path $publishedExecutable -Destination (Join-Path $stagePath "FilesCollector.exe")
Copy-Item -Path (Join-Path $packageRoot "docs") -Destination (Join-Path $stagePath "docs") -Recurse
New-Item -ItemType Directory -Path (Join-Path $stagePath "outputs") -Force | Out-Null

$sourceDestination = Join-Path $stagePath "src"
New-Item -ItemType Directory -Path $sourceDestination -Force | Out-Null
Get-ChildItem -Path $sourceRoot -Force | Where-Object {
    $_.Name -notin @("bin", "obj", ".artifacts", ".github")
} | Copy-Item -Destination $sourceDestination -Recurse -Force

Compress-Archive -Path $stagePath -DestinationPath $zipPath -Force
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -Encoding utf8

Write-Host "Release ZIP: $zipPath"
Write-Host "SHA-256: $hash"
