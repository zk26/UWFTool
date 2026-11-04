param(
    [string]$Version = "2.0.16"
)
$ErrorActionPreference = "Stop"
$libDir = Join-Path $PSScriptRoot "..\lib\AntdUI"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$nupkg = Join-Path $libDir "AntdUI.nupkg"
$url = "https://www.nuget.org/api/v2/package/AntdUI/$Version"

Write-Host "Downloading AntdUI $Version from NuGet..."
Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing

Write-Host "Extracting package..."
$tmpDir = Join-Path $libDir "_tmp"
if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
$zip = $nupkg + ".zip"
Move-Item $nupkg $zip -Force
Expand-Archive -Path $zip -DestinationPath $tmpDir -Force

# Find the right DLL for .NET Framework
$net40 = Join-Path $tmpDir "lib\net40\AntdUI.dll"
$net45 = Join-Path $tmpDir "lib\net45\AntdUI.dll"
$net462 = Join-Path $tmpDir "lib\net462\AntdUI.dll"
$netstandard = Join-Path $tmpDir "lib\netstandard2.0\AntdUI.dll"

$src = $null
if (Test-Path $net40) { $src = $net40 }
elseif (Test-Path $net45) { $src = $net45 }
elseif (Test-Path $net462) { $src = $net462 }
elseif (Test-Path $netstandard) { $src = $netstandard }
else {
    # List available lib folders
    Write-Host "Available lib targets:"
    Get-ChildItem (Join-Path $tmpDir "lib") -Directory | ForEach-Object { Write-Host "  $($_.Name)" }
    # Pick the first available
    $first = Get-ChildItem (Join-Path $tmpDir "lib") -Directory | Select-Object -First 1
    if ($first) {
        $dll = Get-ChildItem $first.FullName -Filter "*.dll" | Select-Object -First 1
        if ($dll) { $src = $dll.FullName }
    }
}

if ($src) {
    Copy-Item $src (Join-Path $libDir "AntdUI.dll") -Force
    Write-Host "Copied: $src -> lib\AntdUI\AntdUI.dll"
} else {
    Write-Host "ERROR: Could not find AntdUI.dll in package"
    exit 1
}

# Cleanup
Remove-Item $tmpDir -Recurse -Force
if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "Done! AntdUI.dll is ready in lib\AntdUI\"
