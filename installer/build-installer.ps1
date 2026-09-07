# Builds Grev Home and packages it into dist\GrevHomeSetup.exe.
# Requires the .NET SDK and Inno Setup 6 (https://jrsoftware.org/isdl.php) on PATH or in the
# default Program Files location.

cd $PSScriptRoot\..

dotnet publish src\GrevHome\GrevHome.csproj -c Release -r win-x64 --self-contained true -o publish

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "ISCC.exe" }

& $iscc installer\GrevHome.iss
