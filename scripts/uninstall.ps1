<#
.SYNOPSIS
    Removes SVG Preview from the current user: registry entries and app files.

.DESCRIPTION
    Reverses everything install.ps1 did — unregisters the thumbnail provider,
    restores the previous .svg default ProgId when one was saved, removes the
    "Open with" registration, deletes %LOCALAPPDATA%\SvgPreview, and notifies
    the Shell.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$installRoot              = Join-Path $env:LOCALAPPDATA 'SvgPreview'
$thumbnailProviderClsid   = '{D8E9B2C4-8F5A-4E1B-9C3D-7A6F2B1E0D5C}'
$thumbnailShellExCategory = '{e357fccd-a995-4576-b01f-234630154e96}'
$svgProgId                = 'SvgPreview.svg'
$classesRoot              = 'HKCU:\Software\Classes'

Write-Host 'Removing registry entries...' -ForegroundColor Cyan

# Restore the previous .svg default ProgId if install.ps1 saved one.
$progIdKeyPath = "$classesRoot\$svgProgId"
$savedPreviousProgId = (Get-ItemProperty -Path $progIdKeyPath -ErrorAction SilentlyContinue).PreviousDefaultProgId
$svgExtensionKeyPath = "$classesRoot\.svg"
$currentDefaultProgId = (Get-ItemProperty -Path $svgExtensionKeyPath -ErrorAction SilentlyContinue).'(default)'

if ($currentDefaultProgId -eq $svgProgId) {
    if ($savedPreviousProgId) {
        Set-ItemProperty -Path $svgExtensionKeyPath -Name '(default)' -Value $savedPreviousProgId
    }
    else {
        try { Remove-ItemProperty -Path $svgExtensionKeyPath -Name '(default)' -ErrorAction Stop } catch {}
    }
}

# Remove the thumbnail provider attachment from .svg and .svgz.
foreach ($svgExtension in @('.svg', '.svgz')) {
    $shellExKeyPath = "$classesRoot\$svgExtension\shellex\$thumbnailShellExCategory"
    if (Test-Path $shellExKeyPath) {
        Remove-Item -Path $shellExKeyPath -Recurse -Force
    }
}

# Remove the COM class, ProgId, Open-with entries.
foreach ($registryKeyPath in @(
    "$classesRoot\CLSID\$thumbnailProviderClsid",
    $progIdKeyPath,
    "$classesRoot\Applications\SvgViewer.exe"
)) {
    if (Test-Path $registryKeyPath) {
        Remove-Item -Path $registryKeyPath -Recurse -Force
    }
}

# Remove the OpenWithProgids reference without deleting other apps' entries.
$openWithKeyPath = "$classesRoot\.svg\OpenWithProgids"
if (Test-Path $openWithKeyPath) {
    try { Remove-ItemProperty -Path $openWithKeyPath -Name $svgProgId -ErrorAction Stop } catch {}
}

Write-Host 'Removing installed files...' -ForegroundColor Cyan
if (Test-Path $installRoot) {
    try {
        Remove-Item -Path $installRoot -Recurse -Force
    }
    catch {
        Write-Warning "Could not delete $installRoot (files may be in use)."
        Write-Warning 'Close Explorer windows or sign out, then delete the folder manually.'
    }
}

Write-Host 'Notifying Windows Shell...' -ForegroundColor Cyan
Add-Type -Namespace SvgPreviewUninstall -Name ShellNotify -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
[SvgPreviewUninstall.ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host 'SVG Preview has been uninstalled.' -ForegroundColor Green
