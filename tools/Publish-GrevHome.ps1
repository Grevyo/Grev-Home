[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\GrevHome\GrevHome.csproj'
$profilePath = Join-Path $repoRoot 'src\GrevHome\Properties\PublishProfiles\win-x64.pubxml'

if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'artifacts\GrevHome-win-x64'
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

Write-Host "Publishing Grev Home to $OutputPath"
dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true -p:PublishProfile=$profilePath -o $OutputPath --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $OutputPath 'GrevHome.exe'
if (-not (Test-Path $exePath)) {
    throw "Publish completed without GrevHome.exe at $exePath"
}

Copy-Item (Join-Path $PSScriptRoot 'Install-GrevHome.ps1') (Join-Path $OutputPath 'Install-GrevHome.ps1') -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall-GrevHome.ps1') (Join-Path $OutputPath 'Uninstall-GrevHome.ps1') -Force

Write-Host "Grev Home release payload ready: $OutputPath" -ForegroundColor Green
Write-Host "Persistent Grev Home data remains outside this payload under C:\GrevHome (or GREV_HOME_ROOT)."
