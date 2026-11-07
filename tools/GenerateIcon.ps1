Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$icoPath = Join-Path $assets 'app.ico'
$preview256 = Join-Path $assets 'icon-preview-256.png'
$preview48 = Join-Path $assets 'icon-preview-48.png'

$masterSize = 1024

function New-ShieldPath([single]$s) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddLine(0.205 * $s, 0.125 * $s, 0.795 * $s, 0.125 * $s)
    $p.AddArc(0.745 * $s, 0.125 * $s, 0.100 * $s, 0.100 * $s, 270, 90)
    $p.AddLine(0.845 * $s, 0.225 * $s, 0.845 * $s, 0.500 * $s)
    $p.AddBezier(0.845 * $s, 0.500 * $s, 0.845 * $s, 0.750 * $s, 0.700 * $s, 0.860 * $s, 0.500 * $s, 0.935 * $s)
    $p.AddBezier(0.500 * $s, 0.935 * $s, 0.300 * $s, 0.860 * $s, 0.155 * $s, 0.750 * $s, 0.155 * $s, 0.500 * $s)
    $p.AddLine(0.155 * $s, 0.500 * $s, 0.155 * $s, 0.225 * $s)
    $p.AddArc(0.155 * $s, 0.125 * $s, 0.100 * $s, 0.100 * $s, 180, 90)
    $p.CloseFigure()
    return $p
}

$master = New-Object System.Drawing.Bitmap($masterSize, $masterSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$shield = New-ShieldPath $masterSize

# main blue gradient
$blueTop = [System.Drawing.ColorTranslator]::FromHtml('#3B82F6')
$blueBottom = [System.Drawing.ColorTranslator]::FromHtml('#1D4ED8')
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    ([System.Drawing.PointF]::new([single]0, [single](0.12 * $masterSize))),
    ([System.Drawing.PointF]::new([single]0, [single](0.95 * $masterSize))),
    $blueTop, $blueBottom)
$g.FillPath($brush, $shield)

# subtle rim to keep edges clean on light and dark backgrounds
$rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(56, 255, 255, 255), [single](0.012 * $masterSize))
$rimPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
$g.DrawPath($rimPen, $shield)

# keyhole
$white = [System.Drawing.Color]::FromArgb(255, 255, 255)
$kx = 0.500 * $masterSize
$ky = 0.430 * $masterSize
$kr = 0.088 * $masterSize
$g.FillEllipse((New-Object System.Drawing.SolidBrush($white)), [single]($kx - $kr), [single]($ky - $kr), [single](2 * $kr), [single](2 * $kr))

$stem = New-Object System.Drawing.Drawing2D.GraphicsPath
$p1 = [System.Drawing.PointF]::new([single](0.455 * $masterSize), [single](0.470 * $masterSize))
$p2 = [System.Drawing.PointF]::new([single](0.545 * $masterSize), [single](0.470 * $masterSize))
$p3 = [System.Drawing.PointF]::new([single](0.588 * $masterSize), [single](0.672 * $masterSize))
$p4 = [System.Drawing.PointF]::new([single](0.412 * $masterSize), [single](0.672 * $masterSize))
$stem.AddPolygon([System.Drawing.PointF[]]@($p1, $p2, $p3, $p4))
$g.FillPath((New-Object System.Drawing.SolidBrush($white)), $stem)

$g.Dispose()

function Resize-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $gg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $gg.DrawImage($master, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)), 0, 0, $master.Width, $master.Height, [System.Drawing.GraphicsUnit]::Pixel)
    $gg.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $stride = $s * 4
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $locked = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($locked)

    $ms = New-Object System.IO.MemoryStream
    $w32 = { param($v) [BitConverter]::GetBytes([int]$v) }
    $ms.Write((& $w32 40), 0, 4)
    $ms.Write((& $w32 $s), 0, 4)
    $ms.Write((& $w32 (2 * $s)), 0, 4)
    $ms.Write(([BitConverter]::GetBytes([int16]1)), 0, 2)
    $ms.Write(([BitConverter]::GetBytes([int16]32)), 0, 2)
    $ms.Write((& $w32 0), 0, 4)
    $ms.Write((& $w32 0), 0, 4)
    $ms.Write((& $w32 0), 0, 4)
    $ms.Write((& $w32 0), 0, 4)
    $ms.Write((& $w32 0), 0, 4)

    for ($y = $s - 1; $y -ge 0; $y--) {
        $ms.Write($pixels, ($y * $stride), $stride)
    }

    $maskRow = [int]([math]::Floor(($s + 31) / 32)) * 4
    $mask = New-Object byte[] ($maskRow * $s)
    $ms.Write($mask, 0, $mask.Length)
    $ms.Flush()
    return $ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($s in $sizes) {
    $images += ,@($s, (Get-DibBytes (Resize-Icon $s)))
}

$headerSize = 6 + 16 * $images.Count
$offset = $headerSize
$ico = New-Object System.IO.MemoryStream
$ico.Write([byte[]]@(0, 0), 0, 2)
$ico.Write([byte[]]@(1, 0), 0, 2)
$ico.Write(([BitConverter]::GetBytes([int16]$images.Count)), 0, 2)

foreach ($img in $images) {
    $s = [int]$img[0]
    $data = $img[1]
    $w = if ($s -ge 256) { 0 } else { $s }
    $ico.Write([byte[]]@([byte]$w, [byte]$w, 0, 0), 0, 4)
    $ico.Write(([BitConverter]::GetBytes([int16]1)), 0, 2)
    $ico.Write(([BitConverter]::GetBytes([int16]32)), 0, 2)
    $ico.Write(([BitConverter]::GetBytes([int]$data.Length)), 0, 4)
    $ico.Write(([BitConverter]::GetBytes([int]$offset)), 0, 4)
    $offset += $data.Length
}

foreach ($img in $images) {
    $data = $img[1]
    $ico.Write($data, 0, $data.Length)
}

$ico.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())

$b256 = Resize-Icon 256
$b256.Save($preview256, [System.Drawing.Imaging.ImageFormat]::Png)
$b256.Dispose()
$b48 = Resize-Icon 48
$b48.Save($preview48, [System.Drawing.Imaging.ImageFormat]::Png)
$b48.Dispose()

Write-Output ("ico=" + $icoPath)
Write-Output ("ico-size=" + (Get-Item $icoPath).Length)
Write-Output ("ico-header-count=" + [BitConverter]::ToInt16([System.IO.File]::ReadAllBytes($icoPath), 4))
$master.Dispose()
