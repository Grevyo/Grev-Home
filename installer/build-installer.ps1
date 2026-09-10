# Build a self-contained Windows installer; never package a failed publish.
$ErrorActionPreference = "Stop"
Push-Location (Join-Path $PSScriptRoot "..")
try {
    $publishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("GrevHome-publish-" + [guid]::NewGuid().ToString("N"))
    $prerequisiteDir = Join-Path ([System.IO.Path]::GetTempPath()) ("GrevHome-prerequisites-" + [guid]::NewGuid().ToString("N"))
    $engineDir = Join-Path ([System.IO.Path]::GetTempPath()) ("GrevHome-engine-" + [guid]::NewGuid().ToString("N"))
    $launcherDir = Join-Path ([System.IO.Path]::GetTempPath()) ("GrevHome-launcher-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $prerequisiteDir | Out-Null
    New-Item -ItemType Directory -Path $engineDir | Out-Null
    New-Item -ItemType Directory -Path $launcherDir | Out-Null
    $webViewBootstrapper = Join-Path $prerequisiteDir "MicrosoftEdgeWebview2Setup.exe"

    Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $webViewBootstrapper
    $webViewSignature = Get-AuthenticodeSignature -FilePath $webViewBootstrapper
    if ($webViewSignature.Status -ne "Valid" -or
        $webViewSignature.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
        throw "The downloaded WebView2 prerequisite was not validly signed by Microsoft Corporation."
    }

    dotnet publish src\GrevHome\GrevHome.csproj -c Release -r win-x64 --self-contained true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed; installer was not built." }
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { $iscc = (Get-Command ISCC.exe -ErrorAction Stop).Source }
    & $iscc "/DMyPublishDir=$publishDir" "/DMyWebViewBootstrapper=$webViewBootstrapper" "/DMyOutputDir=$engineDir" "/DMyOutputBaseFilename=GrevHomeSetupEngine" installer\GrevHome.iss
    if ($LASTEXITCODE -ne 0) { throw "Installer engine compilation failed." }

    $enginePath = Join-Path $engineDir "GrevHomeSetupEngine.exe"
    if (-not (Test-Path $enginePath)) { throw "Installer engine output was not created." }
    dotnet publish installer\Launcher\GrevHome.Installer.csproj -c Release -r win-x64 --self-contained true -o $launcherDir "-p:EnginePath=$enginePath"
    if ($LASTEXITCODE -ne 0) { throw "Custom installer publish failed." }

    New-Item -ItemType Directory -Path dist -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $launcherDir "GrevHomeSetup.exe") -Destination dist\GrevHomeSetup.exe -Force
    Get-FileHash dist\GrevHomeSetup.exe -Algorithm SHA256 |
        Format-List | Out-File dist\GrevHomeSetup.sha256.txt
} finally {
    if ($publishDir -and (Test-Path $publishDir)) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
    if ($prerequisiteDir -and (Test-Path $prerequisiteDir)) { Remove-Item -LiteralPath $prerequisiteDir -Recurse -Force }
    if ($engineDir -and (Test-Path $engineDir)) { Remove-Item -LiteralPath $engineDir -Recurse -Force }
    if ($launcherDir -and (Test-Path $launcherDir)) { Remove-Item -LiteralPath $launcherDir -Recurse -Force }
    Pop-Location
}
