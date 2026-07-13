<#
.SYNOPSIS
    Builds and installs SVG Preview for the current user (no admin required).

.DESCRIPTION
    1. Publishes the SvgViewer app and the SvgThumbnailProvider shell extension.
    2. Copies both into %LOCALAPPDATA%\SvgPreview.
    3. Registers, under HKCU only:
         - the COM thumbnail provider so Explorer shows SVG thumbnails,
         - a ProgId + file association so double-clicking .svg opens the viewer.
    4. Notifies the Shell so the changes take effect without a reboot.

.PARAMETER SkipBuild
    Reuse the existing publish output in .\dist instead of rebuilding.
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- paths -----
$repositoryRoot        = Split-Path -Parent $PSScriptRoot
$publishOutputRoot     = Join-Path $repositoryRoot 'dist'
$installRoot           = Join-Path $env:LOCALAPPDATA 'SvgPreview'
$viewerInstallFolder   = Join-Path $installRoot 'viewer'
$thumbnailInstallFolder = Join-Path $installRoot 'thumbnail'

# CLSID of SvgThumbnailHandler — must match the [Guid] on the C# class.
$thumbnailProviderClsid = '{D8E9B2C4-8F5A-4E1B-9C3D-7A6F2B1E0D5C}'
# Shell extension category for thumbnail providers — fixed by Windows.
$thumbnailShellExCategory = '{e357fccd-a995-4576-b01f-234630154e96}'
$svgProgId = 'SvgPreview.svg'

# ---------------------------------------------------------------- build -----
if (-not $SkipBuild) {
    Write-Host 'Publishing SvgViewer...' -ForegroundColor Cyan
    dotnet publish (Join-Path $repositoryRoot 'src\SvgViewer') -c Release -o (Join-Path $publishOutputRoot 'viewer')
    if ($LASTEXITCODE -ne 0) { throw 'SvgViewer publish failed.' }

    Write-Host 'Publishing SvgThumbnailProvider...' -ForegroundColor Cyan
    dotnet publish (Join-Path $repositoryRoot 'src\SvgThumbnailProvider') -c Release -o (Join-Path $publishOutputRoot 'thumbnail')
    if ($LASTEXITCODE -ne 0) { throw 'SvgThumbnailProvider publish failed.' }
}

# ----------------------------------------------------------------- copy -----
Write-Host "Installing to $installRoot..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force $viewerInstallFolder, $thumbnailInstallFolder | Out-Null

try {
    Copy-Item (Join-Path $publishOutputRoot 'viewer\*')    $viewerInstallFolder    -Recurse -Force
    Copy-Item (Join-Path $publishOutputRoot 'thumbnail\*') $thumbnailInstallFolder -Recurse -Force
}
catch {
    Write-Warning 'Copy failed — the thumbnail DLL may be loaded by Explorer.'
    Write-Warning 'Close all Explorer windows (or run: taskkill /f /im dllhost.exe) and retry.'
    throw
}

$viewerExecutablePath   = Join-Path $viewerInstallFolder 'SvgViewer.exe'
$thumbnailComHostPath   = Join-Path $thumbnailInstallFolder 'SvgThumbnailProvider.comhost.dll'

if (-not (Test-Path $viewerExecutablePath))  { throw "Viewer executable missing: $viewerExecutablePath" }
if (-not (Test-Path $thumbnailComHostPath))  { throw "COM host DLL missing: $thumbnailComHostPath" }

# ------------------------------------------------------------- registry -----
Write-Host 'Registering thumbnail provider (HKCU)...' -ForegroundColor Cyan

$classesRoot = 'HKCU:\Software\Classes'

# COM class registration pointing Explorer at the .NET COM host DLL.
$clsidKeyPath = "$classesRoot\CLSID\$thumbnailProviderClsid"
New-Item -Path "$clsidKeyPath\InProcServer32" -Force | Out-Null
Set-ItemProperty -Path $clsidKeyPath -Name '(default)' -Value 'SVG Preview Thumbnail Provider'
Set-ItemProperty -Path "$clsidKeyPath\InProcServer32" -Name '(default)' -Value $thumbnailComHostPath
Set-ItemProperty -Path "$clsidKeyPath\InProcServer32" -Name 'ThreadingModel' -Value 'Both'

# Attach the provider to .svg and .svgz through the thumbnail shellex category.
foreach ($svgExtension in @('.svg', '.svgz')) {
    $extensionKeyPath = "$classesRoot\$svgExtension"
    New-Item -Path "$extensionKeyPath\shellex\$thumbnailShellExCategory" -Force | Out-Null
    Set-ItemProperty -Path "$extensionKeyPath\shellex\$thumbnailShellExCategory" -Name '(default)' -Value $thumbnailProviderClsid
    Set-ItemProperty -Path $extensionKeyPath -Name 'Content Type' -Value 'image/svg+xml'
    Set-ItemProperty -Path $extensionKeyPath -Name 'PerceivedType' -Value 'image'
}

Write-Host 'Registering viewer file association (HKCU)...' -ForegroundColor Cyan

# ProgId describing how to open SVG files with the viewer.
$progIdKeyPath = "$classesRoot\$svgProgId"
New-Item -Path "$progIdKeyPath\shell\open\command" -Force | Out-Null
New-Item -Path "$progIdKeyPath\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path $progIdKeyPath -Name '(default)' -Value 'Scalable Vector Graphics image'
Set-ItemProperty -Path "$progIdKeyPath\DefaultIcon" -Name '(default)' -Value "$viewerExecutablePath,0"
Set-ItemProperty -Path "$progIdKeyPath\shell\open\command" -Name '(default)' -Value "`"$viewerExecutablePath`" `"%1`""

# Remember the previous default ProgId so uninstall can restore it.
$svgExtensionKeyPath = "$classesRoot\.svg"
$previousDefaultProgId = (Get-ItemProperty -Path $svgExtensionKeyPath -ErrorAction SilentlyContinue).'(default)'
if ($previousDefaultProgId -and $previousDefaultProgId -ne $svgProgId) {
    Set-ItemProperty -Path $progIdKeyPath -Name 'PreviousDefaultProgId' -Value $previousDefaultProgId
}

# Make the viewer the ProgId-level default and list it under "Open with".
Set-ItemProperty -Path $svgExtensionKeyPath -Name '(default)' -Value $svgProgId
New-Item -Path "$svgExtensionKeyPath\OpenWithProgids" -Force | Out-Null
New-ItemProperty -Path "$svgExtensionKeyPath\OpenWithProgids" -Name $svgProgId -Value '' -PropertyType String -Force | Out-Null

# Register SVG Preview as a first-class app in Windows "Default Apps"
# (Settings > Apps > Default apps) so the user can pick it as the default
# handler through the supported UI. Windows 10/11 cryptographically protect
# the final double-click choice (UserChoice hash), so this registration plus
# one user click in the picker is the sanctioned path to becoming default.
$capabilitiesKeyPath = 'HKCU:\Software\SvgPreview\Capabilities'
New-Item -Path "$capabilitiesKeyPath\FileAssociations" -Force | Out-Null
Set-ItemProperty -Path $capabilitiesKeyPath -Name 'ApplicationName' -Value 'winSVG'
Set-ItemProperty -Path $capabilitiesKeyPath -Name 'ApplicationDescription' -Value 'Lightweight SVG image viewer with Explorer thumbnail support'
Set-ItemProperty -Path "$capabilitiesKeyPath\FileAssociations" -Name '.svg' -Value $svgProgId
Set-ItemProperty -Path "$capabilitiesKeyPath\FileAssociations" -Name '.svgz' -Value $svgProgId

$registeredApplicationsKeyPath = 'HKCU:\Software\RegisteredApplications'
if (-not (Test-Path $registeredApplicationsKeyPath)) {
    New-Item -Path $registeredApplicationsKeyPath -Force | Out-Null
}
Set-ItemProperty -Path $registeredApplicationsKeyPath -Name 'winSVG' -Value 'Software\SvgPreview\Capabilities'

# Register the executable itself so it appears in the "Open with" app list.
$applicationKeyPath = "$classesRoot\Applications\SvgViewer.exe"
New-Item -Path "$applicationKeyPath\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path $applicationKeyPath -Name 'FriendlyAppName' -Value 'winSVG'
Set-ItemProperty -Path "$applicationKeyPath\shell\open\command" -Name '(default)' -Value "`"$viewerExecutablePath`" `"%1`""
New-Item -Path "$applicationKeyPath\SupportedTypes" -Force | Out-Null
New-ItemProperty -Path "$applicationKeyPath\SupportedTypes" -Name '.svg' -Value '' -PropertyType String -Force | Out-Null

# --------------------------------------------------------- notify shell -----
Write-Host 'Notifying Windows Shell of the association changes...' -ForegroundColor Cyan

Add-Type -Namespace SvgPreviewInstall -Name ShellNotify -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
# SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
[SvgPreviewInstall.ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

# Refresh the per-user icon cache so new thumbnails/icons appear promptly.
Start-Process -FilePath "$env:SystemRoot\System32\ie4uinit.exe" -ArgumentList '-show' -NoNewWindow -Wait

Write-Host ''
Write-Host 'SVG Preview installed successfully!' -ForegroundColor Green
Write-Host "  Viewer:     $viewerExecutablePath"
Write-Host "  Thumbnails: $thumbnailComHostPath"
Write-Host ''
Write-Host 'Notes:' -ForegroundColor Yellow
Write-Host '  - Open any folder with .svg files: thumbnails render automatically.'
Write-Host '  - If a folder was open during install, press F5 in Explorer to refresh.'
Write-Host '  - If double-click still opens another app, right-click an .svg file,'
Write-Host '    choose "Open with > Choose another app", pick "SVG Preview" and'
Write-Host '    tick "Always" (Windows requires one manual confirmation for defaults).'
