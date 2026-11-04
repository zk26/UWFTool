# 从 assets\logo.png 生成应用图标 assets\app.ico（多尺寸）与界面内嵌资源 assets\logo-256.png
# 用法：powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$srcPng = Join-Path $root 'assets\logo.png'
if (-not (Test-Path $srcPng)) { throw "找不到源图 $srcPng" }
$outDir = Join-Path $root 'assets'
$icoPath = Join-Path $outDir 'app.ico'
$uiPngPath = Join-Path $outDir 'logo-256.png'

$src = [System.Drawing.Image]::FromFile($srcPng)

function Get-ResizedBitmap([System.Drawing.Image]$img, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($img, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40)
    $bw.Write([Int32]$size)
    $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]0)
    $bw.Write([UInt32]($size * $size * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0)
    $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $buffer = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buffer, 0, $buffer.Length)
    $bmp.UnlockBits($data)
    for ($y = $size - 1; $y -ge 0; $y--) {
        $bw.Write($buffer, $y * $stride, $size * 4)
    }
    $maskRow = [int]([Math]::Floor(($size + 31) / 32) * 4)
    $zeros = New-Object byte[] -ArgumentList $maskRow
    for ($y = 0; $y -lt $size; $y++) { $bw.Write($zeros, 0, $maskRow) }
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Close(); $ms.Dispose()
    return , $bytes
}

# 界面内嵌资源：256px PNG（exe 内显示用，体积小且足够清晰）
$uiBmp = Get-ResizedBitmap $src 256
$uiBmp.Save($uiPngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$uiBmp.Dispose()
Write-Host ("界面 logo 已生成：" + $uiPngPath + "  (" + (Get-Item $uiPngPath).Length + " bytes)")

# 多尺寸图标
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = @()
foreach ($s in $sizes) {
    $bmp = Get-ResizedBitmap $src $s
    $data = if ($s -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $entries += ,([pscustomobject]@{ Size = $s; Data = $data })
    $bmp.Dispose()
}

$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$e.Data.Length); $bw.Write([UInt32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Flush(); $bw.Close(); $fs.Close()
$src.Dispose()

Write-Host ("图标已生成：" + $icoPath + "  (" + (Get-Item $icoPath).Length + " bytes)")
