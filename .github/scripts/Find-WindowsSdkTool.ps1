<#
.SYNOPSIS
Returns the path of a Windows SDK tool (signtool.exe, makeappx.exe) from the newest installed SDK.
#>
param(
    [Parameter(Mandatory)]
    [string] $Name
)

$ErrorActionPreference = 'Stop'

$tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$Name" -ErrorAction SilentlyContinue |
    Where-Object { $_.Directory.Parent.Name -match '^\d+(\.\d+){3}$' } |
    Sort-Object { [version] $_.Directory.Parent.Name } -Descending |
    Select-Object -First 1

if (-not $tool) {
    throw "$Name was not found in the Windows SDK."
}

$tool.FullName
