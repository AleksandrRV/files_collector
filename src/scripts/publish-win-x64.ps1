[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$sourceRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $sourceRoot "FilesCollector.App/FilesCollector.App.csproj"
$outputPath = Join-Path $sourceRoot ".artifacts/win-x64"

if (Test-Path $outputPath) {
    Remove-Item -Path $outputPath -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    --output $outputPath

$executablePath = Join-Path $outputPath "FilesCollector.App.exe"
if (-not (Test-Path $executablePath)) {
    throw "The publish operation did not produce FilesCollector.App.exe."
}

Rename-Item -Path $executablePath -NewName "FilesCollector.exe"
Write-Host "Published portable executable: $(Join-Path $outputPath 'FilesCollector.exe')"
