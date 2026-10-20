<#
.SYNOPSIS
Signs MSIX packages and bundles for a release.

.DESCRIPTION
This is the only place that knows how release packages are signed. Today it uses signtool with the
PFX that release.yml decodes from the SIGNING_CERT_PFX_BASE64 secret. To switch to Azure Artifact
Signing, replace the body of this script and the certificate steps in release.yml; the callers stay
the same.

The certificate subject must match the Publisher in src/Groovefin.App/Package.appxmanifest
(CN=Groovefin), otherwise signtool rejects the package.
#>
param(
    [Parameter(Mandatory)]
    [string[]] $Path
)

$ErrorActionPreference = 'Stop'

if (-not $env:SIGNING_CERT_PATH -or -not (Test-Path $env:SIGNING_CERT_PATH)) {
    throw 'SIGNING_CERT_PATH does not point to the signing certificate.'
}

$signtool = & "$PSScriptRoot/Find-WindowsSdkTool.ps1" -Name signtool.exe
$timestamp = 'http://timestamp.digicert.com'

& $signtool sign /fd SHA256 /f $env:SIGNING_CERT_PATH /p $env:SIGNING_CERT_PASSWORD /tr $timestamp /td SHA256 @Path
if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE."
}
