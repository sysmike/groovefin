<#
.SYNOPSIS
Removes CodeQL results in generated code from the SARIF files in a folder (#343).

.DESCRIPTION
Results located in an obj\ folder come from source generators and the XAML compiler (System.Text.Json
contexts, XamlTypeInfo.g.cs, ...). Groovefin cannot change that code, and it was most of the code scanning
alerts. The files are rewritten in place; the number of results removed per file is printed.
#>
param(
    [Parameter(Mandatory)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

function Test-Generated($result) {
    foreach ($location in @($result.locations)) {
        $uri = [string] $location.physicalLocation.artifactLocation.uri
        if ($uri -match '(^|/)obj/') { return $true }
    }
    return $false
}

$files = @(Get-ChildItem $Path -Filter *.sarif -Recurse)
if (-not $files) { throw "No SARIF file in $Path." }

foreach ($file in $files) {
    $sarif = Get-Content $file.FullName -Raw | ConvertFrom-Json -Depth 100
    $removed = 0
    foreach ($run in @($sarif.runs)) {
        $kept = @($run.results | Where-Object { -not (Test-Generated $_) })
        $removed += @($run.results).Count - $kept.Count
        $run.results = $kept
    }

    $sarif | ConvertTo-Json -Depth 100 -Compress | Set-Content $file.FullName -Encoding utf8NoBOM
    Write-Output "$($file.Name): removed $removed results in generated code."
}
