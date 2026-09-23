# Rebuild the Windows icon from the supplied PNG without changing the artwork.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repositoryRoot 'assets/branding\logo.png'
$resources = Join-Path $repositoryRoot 'src\Netch\Resources'
$public = Join-Path $repositoryRoot 'src\Netch.WebUI\public'
New-Item -ItemType Directory -Path $public -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'assets/branding\logo.svg') -Destination (Join-Path $public 'logo.svg')
Copy-Item -LiteralPath $source -Destination (Join-Path $resources 'Netch.png')
$original = [System.Drawing.Image]::FromFile($source)
try {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128)
    $frames = foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($original, 0, 0, $size, $size)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            ,$stream.ToArray()
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [System.IO.File]::Create((Join-Path $resources 'Netch.ico'))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $writer.Write([byte]$sizes[$index]); $writer.Write([byte]$sizes[$index])
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $original.Dispose() }
Write-Output 'Updated application, tray, and WebUI artwork.'
