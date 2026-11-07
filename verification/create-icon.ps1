$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '..\OsdPanel\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$frames = @()
foreach ($size in @(16, 24, 32, 48, 64)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 40, 140, 255))
    $margin = [single]($size / 16)
    $graphics.FillEllipse($brush, $margin, $margin, [single]($size - 2 * $margin), [single]($size - 2 * $margin))
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    if ($size -eq 64) { $bitmap.Save((Join-Path $PSScriptRoot 'tray-icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose()
    $brush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$iconStream = [IO.File]::Create((Join-Path $assetDirectory 'blue-circle.ico'))
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $writer.Write([byte]$frame.Size)
        $writer.Write([byte]$frame.Size)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
$icon = [Drawing.Icon]::new((Join-Path $assetDirectory 'blue-circle.ico'), 32, 32)
try {
    if ($icon.Width -ne 32 -or $icon.Height -ne 32) { throw 'Icon size verification failed.' }
    Write-Output 'Blue circle ICO generated and decoded: 16, 24, 32, 48, 64 px.'
} finally { $icon.Dispose() }
