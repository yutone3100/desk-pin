param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\DeskPin\Assets\DeskPin.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new(
        $size,
        $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $scale = $size / 64.0
            $background = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(45, 108, 223))
            try {
                $graphics.FillEllipse($background, 3 * $scale, 3 * $scale, 58 * $scale, 58 * $scale)
            }
            finally {
                $background.Dispose()
            }

            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [Math]::Max(1.0, 5 * $scale))
            try {
                $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
                $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
                $graphics.DrawLine($pen, 22 * $scale, 18 * $scale, 45 * $scale, 41 * $scale)
                $graphics.DrawLine($pen, 39 * $scale, 15 * $scale, 19 * $scale, 35 * $scale)
                $graphics.DrawLine($pen, 18 * $scale, 46 * $scale, 31 * $scale, 33 * $scale)
            }
            finally {
                $pen.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }

        $png = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
            [pscustomobject]@{
                Size = $size
                Bytes = $png.ToArray()
            }
        }
        finally {
            $png.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }

    $writer.Flush()
    $directory = Split-Path -Parent $OutputPath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    [System.IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Host "DeskPin icon: $OutputPath"
