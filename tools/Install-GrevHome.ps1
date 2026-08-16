[CmdletBinding()]
param(
    [string]$SourcePath = $PSScriptRoot,
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'Programs\GrevHome'),
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$SourcePath = [IO.Path]::GetFullPath($SourcePath)
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$sourceExe = Join-Path $SourcePath 'GrevHome.exe'
$targetExe = Join-Path $InstallPath 'GrevHome.exe'
$parent = Split-Path -Parent $InstallPath
$stagingPath = "$InstallPath.update-$PID"
$backupPath = "$InstallPath.previous"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'Grev Home'
$startMenuLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Grev Home.lnk'

if (-not (Test-Path $sourceExe)) {
    throw "GrevHome.exe was not found in the release payload: $SourcePath"
}

if ([string]::Equals($SourcePath.TrimEnd('\'), $InstallPath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Install source and target must be different folders. Run Install-GrevHome.ps1 from an extracted release payload.'
}

function Stop-InstalledGrevHome {
    if (-not (Test-Path $targetExe)) { return }

    Get-Process -Name 'GrevHome' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and [string]::Equals([IO.Path]::GetFullPath($_.Path), $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
                Write-Host "Stopping installed Grev Home process $($_.Id)..."
                Stop-Process -Id $_.Id -Force
                $_.WaitForExit(10000)
            }
        }
        catch {
            throw "Could not stop the installed Grev Home process: $($_.Exception.Message)"
        }
    }
}

New-Item -ItemType Directory -Path $parent -Force | Out-Null
if (Test-Path $stagingPath) {
    Remove-Item $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

Write-Host 'Staging Grev Home release payload...'
Get-ChildItem -LiteralPath $SourcePath -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stagingPath -Recurse -Force
}

if (-not (Test-Path (Join-Path $stagingPath 'GrevHome.exe'))) {
    Remove-Item $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
    throw 'Staged release payload is missing GrevHome.exe.'
}

Stop-InstalledGrevHome

$hadExistingInstall = Test-Path $InstallPath
try {
    if (Test-Path $backupPath) {
        Remove-Item $backupPath -Recurse -Force
    }

    if ($hadExistingInstall) {
        Write-Host "Moving current installation to rollback slot: $backupPath"
        Move-Item -LiteralPath $InstallPath -Destination $backupPath
    }

    Move-Item -LiteralPath $stagingPath -Destination $InstallPath

    if (-not (Test-Path $targetExe)) {
        throw 'Installed payload is missing GrevHome.exe after the directory swap.'
    }

    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name $runValueName -Value ('"' + $targetExe + '"') -PropertyType String -Force | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startMenuLink)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $InstallPath
    $shortcut.Description = 'Grev Home'
    $shortcut.Save()

    Write-Host "Grev Home installed to $InstallPath" -ForegroundColor Green
    Write-Host 'Persistent data under C:\GrevHome (or GREV_HOME_ROOT) was not changed.'

    if (-not $NoLaunch) {
        Start-Process -FilePath $targetExe -WorkingDirectory $InstallPath
    }
}
catch {
    Write-Warning "Install/upgrade failed: $($_.Exception.Message)"

    if (Test-Path $InstallPath) {
        Remove-Item $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($hadExistingInstall -and (Test-Path $backupPath)) {
        Move-Item -LiteralPath $backupPath -Destination $InstallPath
        Write-Warning 'Previous Grev Home installation was restored.'
    }

    if (Test-Path $stagingPath) {
        Remove-Item $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    throw
}
