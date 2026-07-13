<#
.SYNOPSIS
    Makes double-click on .svg files open SVG Preview for the current user.

.DESCRIPTION
    Windows stores the double-click choice in a protected UserChoice registry
    key (Explorer adds a Deny ACE so apps cannot silently overwrite it). The
    current user still OWNS that key, so this script legitimately removes the
    Deny ACE, deletes the stale UserChoice entry (e.g. one pointing at a
    browser), and lets Explorer fall back to the ProgId default that
    install.ps1 registered — which is SVG Preview. Finally it notifies the
    Shell so the change takes effect immediately.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$svgFileExtsSubKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.svg'
$userChoiceKeyName     = 'UserChoice'

$svgFileExtsKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($svgFileExtsSubKeyPath, $true)

if ($null -eq $svgFileExtsKey) {
    Write-Host 'No per-extension key exists — double-click already uses the ProgId default.' -ForegroundColor Green
}
else {
    try {
        $existingUserChoiceKey = $svgFileExtsKey.OpenSubKey(
            $userChoiceKeyName,
            [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree,
            ([System.Security.AccessControl.RegistryRights]::ChangePermissions -bor
             [System.Security.AccessControl.RegistryRights]::ReadKey))

        if ($null -eq $existingUserChoiceKey) {
            Write-Host 'No UserChoice key present — double-click already uses the ProgId default.' -ForegroundColor Green
        }
        else {
            $previousProgId = ''
            try { $previousProgId = $existingUserChoiceKey.GetValue('ProgId', '') } catch {}

            # Remove the Deny ACE Explorer placed on the key so it can be deleted.
            $userChoiceAcl = $existingUserChoiceKey.GetAccessControl()
            $denyRulesToRemove = $userChoiceAcl.GetAccessRules($true, $false, [System.Security.Principal.NTAccount]) |
                Where-Object { $_.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Deny }
            foreach ($denyRule in $denyRulesToRemove) {
                $userChoiceAcl.RemoveAccessRuleAll($denyRule)
            }
            $existingUserChoiceKey.SetAccessControl($userChoiceAcl)
            $existingUserChoiceKey.Close()

            $svgFileExtsKey.DeleteSubKeyTree($userChoiceKeyName)
            Write-Host "Removed stale UserChoice (was: $previousProgId)." -ForegroundColor Cyan
        }
    }
    finally {
        $svgFileExtsKey.Close()
    }
}

# Tell the Shell the association changed so Explorer picks it up immediately.
Add-Type -Namespace SvgPreviewDefaults -Name ShellNotify -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, uint uFlags, System.IntPtr dwItem1, System.IntPtr dwItem2);
'@
# SHCNE_ASSOCCHANGED = 0x08000000, SHCNF_IDLIST = 0x0000
[SvgPreviewDefaults.ShellNotify]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host 'Double-clicking .svg files now opens SVG Preview.' -ForegroundColor Green
