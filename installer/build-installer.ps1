# Build a self-contained Windows installer; never package a failed publish.
$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    $publishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("GrevHome-publish-" + [guid]::NewGuid().ToString("N"))
    dotnet publish src\GrevHome\GrevHome.csproj -c Release -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed; installer was not built." }
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }
    & $iscc "/DMyPublishDir=$publishDir" installer\GrevHome.iss
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
    Get-FileHash dist\GrevHomeSetup.exe -Algorithm SHA256 |
        Format-List | Out-File dist\GrevHomeSetup.sha256.txt
} finally {
    Pop-Location
}
