# Compatibility wrapper (Chinese file name kept for old shortcuts / docs).
# The real logic lives in trust-publisher.ps1 - ASCII name on purpose, so batch files and
# shortcuts never have to deal with non-ASCII paths.
param(
    [switch]$Elevated,
    [string]$Exe
)

$impl = Join-Path $PSScriptRoot 'trust-publisher.ps1'
$forward = @{}
if ($Elevated) { $forward['Elevated'] = $true }
if ($Exe) { $forward['Exe'] = $Exe }

& $impl @forward
exit $LASTEXITCODE
