# 编译 UWF 教师控制台（不依赖 Visual Studio，使用系统自带的 .NET Framework 编译器）
# 用法：
#   powershell -ExecutionPolicy Bypass -File .\build.ps1              编译 + 签名，版本号自动 +1
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package     同上，并打包 dist\UWFtool-v<版本>（含 zip）
#   powershell -ExecutionPolicy Bypass -File .\build.ps1 -Version 4.2.0   显式指定版本号（不自动 +1）
param(
    [switch]$Package,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 .NET Framework 4.x 的 csc.exe' }

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# ---------------------------------------------------------------- 版本号
# 不指定 -Version 时自动把第三段 +1（4.1.1 -> 4.1.2 -> 4.1.3 …），
# 每次打包都有新版本号，不用手工改 AssemblyInfo.cs；界面里的 v4.1.2 也跟着变。
$asmPath = Join-Path $src 'AssemblyInfo.cs'
$asm = [System.IO.File]::ReadAllText($asmPath, [System.Text.Encoding]::UTF8)
$m = [regex]::Match($asm, 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)\.(\d+)"\)')
if (-not $m.Success) { throw "AssemblyInfo.cs 里找不到 AssemblyVersion（$asmPath）" }
$oldVer = '{0}.{1}.{2}' -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value
if ($Version -and $Version.Trim().Length -gt 0) {
    $newVer = $Version.Trim().TrimStart('v')
} else {
    $newVer = '{0}.{1}.{2}' -f $m.Groups[1].Value, $m.Groups[2].Value, ([int]$m.Groups[3].Value + 1)
}
$asm = $asm -replace 'AssemblyVersion\("[\d\.]+"\)', ('AssemblyVersion("' + $newVer + '.0")')
$asm = $asm -replace 'AssemblyFileVersion\("[\d\.]+"\)', ('AssemblyFileVersion("' + $newVer + '.0")')
[System.IO.File]::WriteAllText($asmPath, $asm, (New-Object System.Text.UTF8Encoding($true)))
Write-Host ('version : ' + $oldVer + ' -> ' + $newVer)

$out = Join-Path $dist 'UWFtool.exe'
$files = Get-ChildItem -Path $src -Filter *.cs | ForEach-Object { $_.FullName }

$cskin = Join-Path $root 'lib\CSkin\CSkin.dll'
if (-not (Test-Path $cskin)) { throw "找不到 $cskin" }

$antdUI = Join-Path $root 'lib\AntdUI\AntdUI.dll'
if (-not (Test-Path $antdUI)) { throw "找不到 $antdUI" }

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/codepage:65001',
    "/win32manifest:$src\app.manifest",
    "/out:$out",
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Management.dll',
    '/r:System.ServiceProcess.dll',
    "/r:$cskin",
    "/r:$antdUI"
) + $files

$icon = Join-Path $root 'assets\app.ico'
if (Test-Path $icon) { $cscArgs = @("/win32icon:$icon") + $cscArgs }

# 界面内嵌 logo（LogoView 显示用）：优先 256px 压缩版，缺了就退回原图
$logoRes = "$root\assets\logo-256.png"
if (-not (Test-Path -LiteralPath $logoRes)) { $logoRes = "$root\assets\logo.png" }
if (Test-Path -LiteralPath $logoRes) { $cscArgs = @("/resource:$logoRes,logo.png") + $cscArgs }

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

# CSkin / AntdUI 是外部控件库，随 exe 一起分发
# 程序正在运行时目标文件会被占用：大小一致就跳过复制，避免整个编译白白失败
$distSkin = Join-Path $dist 'CSkin.dll'
$needSkin = $true
if (Test-Path $distSkin) {
    if ((Get-Item $distSkin).Length -eq (Get-Item $cskin).Length) { $needSkin = $false }
}
if ($needSkin) { Copy-Item $cskin $distSkin -Force }

$distAntd = Join-Path $dist 'AntdUI.dll'
$needAntd = $true
if (Test-Path $distAntd) {
    if ((Get-Item $distAntd).Length -eq (Get-Item $antdUI).Length) { $needAntd = $false }
}
if ($needAntd) { Copy-Item $antdUI $distAntd -Force }

# ---------------------------------------------------------------- 签名
# 目标：UAC 上显示发布者「小可」，而不是“未知发布者”。
# 三个条件缺一不可：
#   1. exe 带 Authenticode 签名；
#   2. 那张证书在「受信任的根证书颁发机构」里（否则证书链不成立 -> 未知发布者）；
#   3. 那张证书在「受信任的发布者」里。
# 另外每次编译都必须复用同一张证书：重新生成会让之前的信任全部作废，UAC 又变回“未知”。
# 注意：脚本里不写中文，避免 ps1 编码把证书名弄乱；「小可」用字符码拼出。
$cn = 'CN=' + [string][char]0x5C0F + [string][char]0x53EF

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $cn -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $cn -FriendlyName 'UWFtoolSigner' `
             -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10) `
             -KeyExportPolicy Exportable
    Write-Host ('signer created: ' + $cert.Subject + '  ' + $cert.Thumbprint)
} else {
    Write-Host ('signer reused : ' + $cert.Subject + '  ' + $cert.Thumbprint)
}

# 1) 签名（优先带时间戳：证书过期后签名仍然有效；无网络时退化为不带时间戳）
$sig = $null
try {
    $sig = Set-AuthenticodeSignature -FilePath $out -Certificate $cert -HashAlgorithm SHA256 `
           -TimestampServer 'http://timestamp.digicert.com'
} catch {
    Write-Host ('timestamp failed, signing without it: ' + $_.Exception.Message)
}
if (-not $sig -or -not $sig.SignerCertificate) {
    $sig = Set-AuthenticodeSignature -FilePath $out -Certificate $cert -HashAlgorithm SHA256
}

# 2) 导出公钥证书，方便拷到别的电脑上手动信任
$cerPath = Join-Path $dist 'UWFtool.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

# 3) 装进受信任存储（幂等：重复执行只是刷新同一条记录）
#    certutil -addstore 比 X509Store.Add 可靠：不会因为弹窗被点“否”而静默失败
foreach ($store in @('Root', 'TrustedPublisher')) {
    $log = certutil -user -addstore -f $store $cerPath 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host ('trust (user ' + $store + ') failed: ' + ($log -join ' ')) }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    # 本机存储：换 Windows 账号 / 别的管理员登录也照样识别发布者
    foreach ($store in @('Root', 'TrustedPublisher')) {
        $log = certutil -addstore -f $store $cerPath 2>&1
        if ($LASTEXITCODE -ne 0) { Write-Host ('trust (machine ' + $store + ') failed: ' + ($log -join ' ')) }
    }
}

# 4) 校验：状态必须是 Valid，UAC 才会显示发布者
$check = Get-AuthenticodeSignature -FilePath $out
Write-Host ('sign status   : ' + $check.Status + '    signer: ' + ($check.SignerCertificate.Subject))
if ($check.Status -ne 'Valid') {
    Write-Host 'WARNING: signature is still not trusted. Run tools\trust-publisher.cmd as administrator once, then check again.'
    Write-Host ('         detail: ' + $check.StatusMessage)
}

Write-Host "已生成：$out"
Get-Item $out, $distSkin, $distAntd, $cerPath | Select-Object Name, Length, LastWriteTime | Format-List

# ---------------------------------------------------------------- 打包（可选）
if ($Package) {
    # 4.1.1.0 -> 4.1.1：装配版本最后一位固定是 0，不必出现在目录名里
    $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($out).FileVersion -replace '\.0$', ''
    $pkg = Join-Path $dist ('UWFtool-v' + $ver)
    if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pkg | Out-Null
    Copy-Item $out (Join-Path $pkg 'UWFtool.exe') -Force
    Copy-Item $distSkin (Join-Path $pkg 'CSkin.dll') -Force
    Copy-Item $distAntd (Join-Path $pkg 'AntdUI.dll') -Force
    Copy-Item $cerPath (Join-Path $pkg 'UWFtool.cer') -Force
    foreach ($doc in @('README.md', '快速上手.txt')) {
        $p = Join-Path $root $doc
        if (Test-Path $p) { Copy-Item $p (Join-Path $pkg $doc) -Force }
    }
    # 信任发布者：cmd 只是外壳，真正干活的是同目录的 trust-publisher.ps1，两个都要带上
    $trust = Join-Path $root 'tools\trust-publisher.cmd'
    if (Test-Path $trust) { Copy-Item $trust (Join-Path $pkg '信任发布者.cmd') -Force }
    $trustPs1 = Join-Path $root 'tools\trust-publisher.ps1'
    if (Test-Path $trustPs1) { Copy-Item $trustPs1 (Join-Path $pkg 'trust-publisher.ps1') -Force }
    $zip = Join-Path $dist ('UWFtool-v' + $ver + '.zip')
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -Force
    Write-Host ('package: ' + $pkg)
    Write-Host ('zip    : ' + $zip)
}
