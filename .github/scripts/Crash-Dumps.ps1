<#
.SYNOPSIS
Crash dumps of Groovefin in CI (#329). -Enable makes Windows Error Reporting write a full dump to the folder
when Groovefin.exe crashes. -Analyze prints the analysis of each dump there with cdb, the Windows debugger,
when the runner has it.

.DESCRIPTION
The smoke test sets GROOVEFIN_CRASH_ON_XAML_ERROR=1: a XAML error from native code, such as the random
"Unspecified error", then ends the app with a stowed exception instead of being reported. Its dump keeps the
native stack where the error came from, which the log cannot show; !analyze -v prints it. Symbols come from
Microsoft's symbol server and, for Groovefin's own code, the app's folder.
#>
param(
    [switch] $Enable,

    [switch] $Analyze,

    [Parameter(Mandatory)]
    [string] $Directory,

    # The app's folder, for Groovefin's own symbols.
    [string] $Symbols = ''
)

$ErrorActionPreference = 'Stop'

if ($Enable) {
    New-Item -ItemType Directory -Force $Directory | Out-Null
    $wer = 'HKLM:\SOFTWARE\Microsoft\Windows\Windows Error Reporting'
    $key = Join-Path $wer 'LocalDumps\Groovefin.exe'
    New-Item -Force $key | Out-Null
    New-ItemProperty $key -Name DumpFolder -Value $Directory -PropertyType ExpandString -Force | Out-Null
    # 2: a full dump, with the heap the stowed exception's error information lives in.
    New-ItemProperty $key -Name DumpType -Value 2 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $key -Name DumpCount -Value 3 -PropertyType DWord -Force | Out-Null
    # No dialog, and on even where the runner image turned reporting off.
    New-ItemProperty $wer -Name DontShowUI -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $wer -Name Disabled -Value 0 -PropertyType DWord -Force | Out-Null
    Set-Service WerSvc -StartupType Manual -ErrorAction SilentlyContinue
    Start-Service WerSvc -ErrorAction SilentlyContinue
    Write-Output "A crash of Groovefin.exe writes a full dump to $Directory."
}

if ($Analyze) {
    # WerFault writes the dump after the app has gone: wait until it is done, at most a minute.
    $until = (Get-Date).AddMinutes(1)
    while ((Get-Process WerFault -ErrorAction SilentlyContinue) -and (Get-Date) -lt $until) { Start-Sleep -Seconds 1 }
    $dumps = @(Get-ChildItem $Directory -Filter *.dmp -ErrorAction SilentlyContinue)
    if (-not $dumps) {
        Write-Output 'Groovefin wrote no crash dump.'
        return
    }

    $cdb = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x64\cdb.exe",
        "$env:ProgramFiles\Windows Kits\10\Debuggers\x64\cdb.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $cdb) {
        Write-Output "::warning title=Crash dump::This runner has no cdb.exe. Download the crash-dumps artifact and open $($dumps[0].Name) in WinDbg (!analyze -v)."
        return
    }

    $cache = Join-Path ([System.IO.Path]::GetTempPath()) 'symbols'
    $path = "srv*$cache*https://msdl.microsoft.com/download/symbols"
    if ($Symbols) {
        $path = "$Symbols;$path"
    }

    foreach ($dump in $dumps) {
        Write-Output "::group::Analysis of $($dump.Name) ($([Math]::Round($dump.Length / 1MB)) MB)"
        # The stowed exception and its stack, the exception record's stack, and every thread's top frames.
        & $cdb -z $dump.FullName -y $path -c '!analyze -v; .ecxr; kn 60; ~*kn 25; q' 2>&1 | ForEach-Object { "$_" }
        Write-Output '::endgroup::'
    }

    Write-Output "::error title=Crash dump::Groovefin crashed and wrote $($dumps.Count) dump(s); the analysis is above, the dumps are in the crash-dumps artifact."
}
