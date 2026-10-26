<#
.SYNOPSIS
    Gera o ícone (.ico) e o logo (.png) do BackupGuard a partir das formas de assets\logo.svg.
    Usa só o WPF do próprio Windows; rode de novo se mudar o desenho.
#>
Add-Type -AssemblyName PresentationCore, WindowsBase

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot "src\BackupGuard.App\Assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Mesmas formas do logo.svg (espaço de 256 x 256).
$dark = [Windows.Media.SolidColorBrush][Windows.Media.Color]::FromRgb(0x11, 0x11, 0x11)
$light = [Windows.Media.Brushes]::White
$background = New-Object Windows.Media.RectangleGeometry (New-Object Windows.Rect 0, 0, 256, 256), 56, 56
$shield = [Windows.Media.Geometry]::Parse("M128 34 L206 62 V122 C206 172 173 207 128 224 C83 207 50 172 50 122 V62 Z")
$arrow = [Windows.Media.Geometry]::Parse("M128 78 L172 126 H143 V176 H113 V126 H84 Z")

function Render([int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $ctx = $visual.RenderOpen()
    $ctx.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 256), ($size / 256)))
    $ctx.DrawGeometry($dark, $null, $background)
    $ctx.DrawGeometry($light, $null, $shield)
    $ctx.DrawGeometry($dark, $null, $arrow)
    $ctx.Pop()
    $ctx.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

# Logo grande para a interface e documentação.
[IO.File]::WriteAllBytes((Join-Path $outDir "logo.png"), (Render 256))

# ICO com várias resoluções (entradas PNG, aceitas desde o Windows Vista).
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (Render $_) }
$ico = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $ico
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $writer.Write([byte]($s % 256)); $writer.Write([byte]($s % 256))  # 256 é gravado como 0
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$data.Length); $writer.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $writer.Write($data) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $outDir "app.ico"), $ico.ToArray())
Write-Host "Gerados: $outDir\app.ico e logo.png"
