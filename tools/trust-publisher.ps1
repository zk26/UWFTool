# Trust the code-signing certificate of UWFtool.exe, so UAC shows the publisher name
# instead of "Unknown publisher".
#
# Why this is needed: the exe is signed with a self-signed certificate. Windows only
# shows the publisher when that exact certificate is present in
#   1) Trusted Root Certification Authorities
#   2) Trusted Publishers
# The certificate is read from the exe itself (not from a .cer file), so a rebuild can
# never leave a stale certificate trusted while the exe is signed with a newer one.
#
# Run:  double click trust-publisher.cmd   (or right click -> Run with PowerShell)
#       It asks for elevation once, installs the certificate for both the machine and
#       the current user, then verifies the signature.
#
# ASCII only on purpose: keeps the script encoding-proof on every Windows locale.

param(
    [switch]$Elevated,
    [string]$Exe
)

$ErrorActionPreference = 'Stop'

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path -Parent $here

# 两种布局都要能找到 exe：发布包（脚本和 exe 同目录）与源码目录（脚本在 tools\ 下）
if (-not $Exe -or $Exe.Length -eq 0) {
    $candidates = @(
        (Join-Path $here 'UWFtool.exe'),
        (Join-Path $root 'dist\UWFtool.exe'),
        (Join-Path $root 'UWFtool.exe')
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { $Exe = $c; break }
    }
    if (-not $Exe) { $Exe = $candidates[1] }
}

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host ('[ERROR] not found: ' + $Exe)
    Write-Host '        build the program first (build.ps1), then run this script again.'
    exit 1
}

# ---------------------------------------------------------------- elevate once
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin -and -not $Elevated) {
    Write-Host 'Asking for administrator rights (machine-wide trust, one time)...'
    try {
        $cliArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
                     '-Elevated', '-Exe', ('"' + $Exe + '"'))
        Start-Process -FilePath 'powershell.exe' -ArgumentList $cliArgs -Verb RunAs -Wait
    } catch {
        Write-Host ('[WARN] elevation cancelled: ' + $_.Exception.Message)
        Write-Host '       falling back to a per-user install (works for the current Windows account only).'
    }
    # after the elevated run (or a cancel) fall through and verify with the current token
}

# ---------------------------------------------------------------- find the signer
$signer = $null
try {
    $sig = Get-AuthenticodeSignature -FilePath $Exe
    if ($sig -and $sig.SignerCertificate) { $signer = $sig.SignerCertificate }
} catch {
    Write-Host ('[WARN] cannot read the signature: ' + $_.Exception.Message)
}

if (-not $signer) {
    $cer = Join-Path (Split-Path -Parent $Exe) 'UWFtool.cer'
    if (Test-Path -LiteralPath $cer) {
        $signer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
        Write-Host 'The exe is not signed; trusting UWFtool.cer instead.'
    }
}
if (-not $signer) {
    Write-Host '[ERROR] the exe has no digital signature and UWFtool.cer is missing.'
    Write-Host '        rebuild with build.ps1, then run this script again.'
    exit 1
}

$tmpCer = Join-Path ([System.IO.Path]::GetTempPath()) ('uwftool-trust-' + $signer.Thumbprint + '.cer')
[System.IO.File]::WriteAllBytes($tmpCer, $signer.RawData)

Write-Host ('certificate : ' + $signer.Subject)
Write-Host ('thumbprint  : ' + $signer.Thumbprint)
Write-Host ''

# ---------------------------------------------------------------- install
# certutil -f is quiet and idempotent; X509Store.Add pops a dialog that users can cancel
# by accident, which is exactly how a "trusted" build ends up showing Unknown publisher.
$stores = @('Root', 'TrustedPublisher')
$failures = 0
foreach ($store in $stores) {
    if ($isAdmin) {
        $out = certutil -addstore -f $store $tmpCer 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ('[WARN] machine store ' + $store + ' failed: ' + ($out -join ' '))
            $failures++
        } else {
            Write-Host ('trusted (machine ' + $store + ')')
        }
    }
    $out = certutil -user -addstore -f $store $tmpCer 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ('[WARN] user store ' + $store + ' failed: ' + ($out -join ' '))
        $failures++
    } else {
        Write-Host ('trusted (user ' + $store + ')')
    }
}
Remove-Item -LiteralPath $tmpCer -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- verify
$check = Get-AuthenticodeSignature -FilePath $Exe
Write-Host ''
Write-Host ('signature status: ' + $check.Status)
Write-Host ('signer          : ' + ($check.SignerCertificate.Subject))
if ($check.Status -ne 'Valid') {
    Write-Host ''
    Write-Host '[FAIL] signature is still not trusted:'
    Write-Host ('       ' + $check.StatusMessage)
    Write-Host '       Run this script as administrator, then check again.'
    exit 2
}

Write-Host ''
Write-Host 'OK - UAC now shows the publisher name. Close and reopen the program to see it.'
exit 0
