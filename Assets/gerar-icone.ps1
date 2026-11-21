# Gera Assets\ActionBar.ico (logo "AB" neon) desenhando cada tamanho separadamente,
# para que 16 e 24 px fiquem nítidos na bandeja e na barra de título.
# Uso: pwsh -File Assets\gerar-icone.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cyan = [System.Drawing.Color]::FromArgb(255, 0, 229, 255)
$magenta = [System.Drawing.Color]::FromArgb(255, 255, 43, 214)

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $small = $s -le 24
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $neon = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cyan, $magenta, 45)

    # Fundo escuro arredondado
    $m = [single]($s * 0.03)
    $bg = New-RoundedPath $m $m ($s - 2 * $m) ($s - 2 * $m) ([single]($s * 0.22))
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 9, 9, 22), [System.Drawing.Color]::FromArgb(255, 24, 16, 52), 60)
    $g.FillPath($bgBrush, $bg)

    # Moldura neon com brilho (camadas largas e transparentes por baixo do traço fino)
    $inset = [single]($s * 0.10)
    $frame = New-RoundedPath $inset $inset ($s - 2 * $inset) ($s - 2 * $inset) ([single]($s * 0.16))
    if (-not $small) {
        foreach ($layer in @(@(0.10, 35), @(0.065, 70))) {
            $pen = New-Object System.Drawing.Pen($neon, [single]($s * $layer[0])); $pen.LineJoin = 'Round'
            $glow = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb($layer[1], $cyan), [System.Drawing.Color]::FromArgb($layer[1], $magenta), 45)
            $pen.Brush = $glow; $g.DrawPath($pen, $frame); $pen.Dispose(); $glow.Dispose()
        }
    }
    $line = New-Object System.Drawing.Pen($neon, [single][Math]::Max(1, $s * 0.035)); $g.DrawPath($line, $frame)

    # Letras "AB"
    $text = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family = New-Object System.Drawing.FontFamily('Segoe UI')
    $style = [int][System.Drawing.FontStyle]::Bold
    $em = [single]($s * ($(if ($small) { 0.52 } else { 0.44 })))
    $format = New-Object System.Drawing.StringFormat; $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
    $text.AddString('AB', $family, $style, $em, $rect, $format)
    # Centraliza pelo contorno real das letras (a fonte tem espaço extra acima)
    $b = $text.GetBounds(); $mx = New-Object System.Drawing.Drawing2D.Matrix
    $mx.Translate([single]($s / 2 - ($b.X + $b.Width / 2)), [single]($s / 2 - ($b.Y + $b.Height / 2))); $text.Transform($mx)
    if ($s -ge 48) {
        # Letreiro de neon: halo largo, tubo colorido e núcleo quase branco.
        foreach ($layer in @(@(0.11, 30), @(0.075, 55), @(0.05, 90))) {
            $glow = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb($layer[1], $cyan), [System.Drawing.Color]::FromArgb($layer[1], $magenta), 0)
            $pen = New-Object System.Drawing.Pen($glow, [single]($s * $layer[0])); $pen.LineJoin = 'Round'
            $g.DrawPath($pen, $text); $pen.Dispose(); $glow.Dispose()
        }
        $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 14, 10, 34)); $g.FillPath($fill, $text); $fill.Dispose()
        $tubeBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cyan, $magenta, 0)
        $tube = New-Object System.Drawing.Pen($tubeBrush, [single]($s * 0.03)); $tube.LineJoin = 'Round'; $g.DrawPath($tube, $text); $tube.Dispose(); $tubeBrush.Dispose()
        $core = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 240, 250, 255), [single]($s * 0.011)); $core.LineJoin = 'Round'; $g.DrawPath($core, $text); $core.Dispose()
    }
    else {
        # Pequeno demais para contorno: letras cheias para continuarem legíveis.
        $g.FillPath($neon, $text)
    }
    $g.Dispose(); return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Frame $s; $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $here 'ActionBar-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose(); , $ms.ToArray()
}
# Formato ICO: cabeçalho + diretório + imagens PNG (aceito desde o Windows Vista)
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $here 'ActionBar.ico'), $out.ToArray())
"Ícone gerado: $(Join-Path $here 'ActionBar.ico') ($($out.Length) bytes)"
