# Genera Assets/app.ico: un circulo de "grabar" (rojo) sobre fondo transparente.
# Dibuja varios tamanos y los empaqueta en un .ico multi-resolucion.
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [int]($size * 0.12)
    $d = $size - 2 * $pad

    # Anillo exterior gris oscuro
    $penW = [Math]::Max(2, [int]($size * 0.06))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 60, 60, 66), $penW)
    $g.DrawEllipse($pen, $pad, $pad, $d, $d)

    # Circulo rojo interno (boton REC)
    $inPad = [int]($size * 0.30)
    $din = $size - 2 * $inPad
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 220, 45, 45))
    $g.FillEllipse($brush, $inPad, $inPad, $din, $din)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @{}
foreach ($s in $sizes) { $frames[$s] = New-Frame $s }

# Empaquetar en .ico manualmente (cada frame como PNG embebido).
$out = Join-Path $PSScriptRoot "app.ico"
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([UInt16]0)        # reserved
$bw.Write([UInt16]1)        # type = icon
$bw.Write([UInt16]$sizes.Count)

# Pre-render PNG bytes
$pngBytes = @{}
foreach ($s in $sizes) {
    $ms = New-Object System.IO.MemoryStream
    $frames[$s].Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes[$s] = $ms.ToArray()
    $ms.Dispose()
}

# ICONDIRENTRY (16 bytes cada una)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([Byte]0)   # colors
    $bw.Write([Byte]0)   # reserved
    $bw.Write([UInt16]1) # planes
    $bw.Write([UInt16]32)# bpp
    $bw.Write([UInt32]$pngBytes[$s].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngBytes[$s].Length
}
foreach ($s in $sizes) { $bw.Write($pngBytes[$s]) }

$bw.Flush(); $bw.Close(); $fs.Close()
foreach ($s in $sizes) { $frames[$s].Dispose() }
Write-Host "Icono creado: $out"
