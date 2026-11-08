$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 32, 48, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream
    $outer = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#B7CDFF'), [single]($size * 0.085))
    $inner = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#E8835F'), [single]($size * 0.085))
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawEllipse($outer, [single]($size * 0.09), [single]($size * 0.09), [single]($size * 0.82), [single]($size * 0.82))
        $graphics.DrawEllipse($inner, [single]($size * 0.28), [single]($size * 0.28), [single]($size * 0.44), [single]($size * 0.44))
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += ,@{ Size = $size; Bytes = $stream.ToArray() }
    }
    finally { $outer.Dispose(); $inner.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
}
$file = [System.IO.File]::Create((Join-Path $PSScriptRoot 'app.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
}
finally { $writer.Dispose(); $file.Dispose() }
