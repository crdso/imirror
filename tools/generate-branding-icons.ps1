$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/iMirror.App/Assets/Branding'
foreach ($name in @('iMirror','iPhone')) {
    $source = [Windows.Media.Imaging.BitmapImage]::new()
    $source.BeginInit(); $source.CacheOption = 'OnLoad'
    $source.UriSource = [Uri](Join-Path $assets "$name.png"); $source.EndInit(); $source.Freeze()
    $frames = @()
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        $visual = [Windows.Media.DrawingVisual]::new()
        [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual,'HighQuality')
        $drawing = $visual.RenderOpen()
        $drawing.DrawImage($source,[Windows.Rect]::new(0,0,$size,$size)); $drawing.Close()
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($visual)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $buffer = [IO.MemoryStream]::new(); $encoder.Save($buffer)
        $frames += @{Size=$size;Bytes=$buffer.ToArray()}; $buffer.Dispose()
    }
    $output = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets "$name.ico")))
    try {
        $output.Write([uint16]0); $output.Write([uint16]1); $output.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $output.Write([byte]$dimension); $output.Write([byte]$dimension)
            $output.Write([byte]0); $output.Write([byte]0); $output.Write([uint16]1); $output.Write([uint16]32)
            $output.Write([uint32]$frame.Bytes.Length); $output.Write([uint32]$offset); $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $output.Write([byte[]]$frame.Bytes) }
    } finally { $output.Dispose() }
    Write-Host "$name.ico: nine PNG/32-bit frames, 16 through 256 px; original transparency preserved."
}
