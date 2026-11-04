# 开发用：编译一个不需要管理员权限的测试版本（用于在没有 UWF 的机器上验证界面）
# 正常分发请用根目录的 build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src  = Join-Path $root 'src'
$outDir = Join-Path $root 'obj'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found' }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir 'UWFTeacherTest.exe'
$files = Get-ChildItem -Path $src -Filter *.cs | ForEach-Object { $_.FullName }
$cskin = Join-Path $root 'lib\CSkin\CSkin.dll'
if (-not (Test-Path $cskin)) { throw "CSkin.dll not found: $cskin" }
Copy-Item $cskin $outDir -Force
$cscArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001','/define:DEMO',
             "/win32manifest:$root\tools\app-asinvoker.manifest","/out:$out",
             '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Management.dll',
             "/r:$cskin") + $files
$icon = Join-Path $root 'assets\app.ico'
if (Test-Path $icon) { $cscArgs = @("/win32icon:$icon") + $cscArgs }
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed $LASTEXITCODE" }
Write-Host "built $out"