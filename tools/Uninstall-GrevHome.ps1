[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallPath = (Join-Path $env:LOCALAPPDATA 'Programs\GrevHome'),
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$targetExe = Join-Path $InstallPath 'GrevHome.exe'
$backupPath = "$InstallPath.previous"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'Grev Home'
$startMenuLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Grev Home.lnk'
$dataPath = if ([string]::IsNullOrWhiteSpace($env:GREV_HOME_ROOT)) { 'C:\GrevHome' } else { [IO.Path]::GetFullPath($env:GREV_HOME_ROOT) }

if (Test-Path $targetExe) {
    Get-Process -Name 'GrevHome' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and [string]::Equals([IO.Path]::GetFullPath($_.Path), $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
                if ($PSCmdlet.ShouldProcess("Grev Home process $($_.Id)", 'Stop')) {
                    Stop-Process -Id $_.Id -Force
                    $_.WaitForExit(10000) | Out-Null
                }
            }
        }
        catch {
            throw "Could not stop the installed Grev Home process: $($_.Exception.Message)"
        }
    }
}

if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath $startMenuLink -Force -ErrorAction SilentlyContinue

# Move away from the install folder before deleting it so the script can also be run from the
# installed payload itself.
Set-Location $env:TEMP

if ((Test-Path $InstallPath) -and $PSCmdlet.ShouldProcess($InstallPath, 'Remove Grev Home application files')) {
    Remove-Item -LiteralPath $InstallPath -Recurse -Force
}
if ((Test-Path $backupPath) -and $PSCmdlet.ShouldProcess($backupPath, 'Remove Grev Home rollback application files')) {
    Remove-Item -LiteralPath $backupPath -Recurse -Force
}

if ($RemoveUserData) {
    if ((Test-Path $dataPath) -and $PSCmdlet.ShouldProcess($dataPath, 'PERMANENTLY remove Grev Home profiles, saves, settings and user data')) {
        Remove-Item -LiteralPath $dataPath -Recurse -Force
        Write-Host 'Grev Home application and user data removed.' -ForegroundColor Yellow
    }
}
else {
    Write-Host 'Grev Home application removed. Persistent user data was preserved.' -ForegroundColor Green
    Write-Host "Data retained at: $dataPath"
    Write-Host 'Use -RemoveUserData only when permanent profile/save removal is explicitly intended.'
}
