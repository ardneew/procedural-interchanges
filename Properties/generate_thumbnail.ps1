Add-Type -AssemblyName System.Drawing

$width = 950
$height = 500
$bmp = [System.Drawing.Bitmap]::new($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)

$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

# Background Gradient
$rect = [System.Drawing.Rectangle]::new(0, 0, $width, $height)
$bgBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, [System.Drawing.Color]::FromArgb(255, 6, 11, 22), [System.Drawing.Color]::FromArgb(255, 15, 23, 42), 45.0)
$g.FillRectangle($bgBrush, $rect)

# Ambient Radial Glow on left behind the logo
$glowPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
$glowPath.AddEllipse(40, 60, 400, 380)
$pbg = [System.Drawing.Drawing2D.PathGradientBrush]::new($glowPath)
$pbg.CenterColor = [System.Drawing.Color]::FromArgb(45, 56, 189, 248)
$pbg.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 6, 11, 22))
$g.FillPath($pbg, $glowPath)

# Blueprint Cad Lines in background
$gridPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(18, 56, 189, 248), 1.5)
for ($x = 40; $x -lt $width; $x += 60) {
    $g.DrawLine($gridPen, $x, 0, $x, $height)
}
for ($y = 40; $y -lt $height; $y += 60) {
    $g.DrawLine($gridPen, 0, $y, $width, $y)
}
$g.DrawEllipse($gridPen, 80, 90, 320, 320)
$g.DrawEllipse($gridPen, 160, 170, 160, 160)

# --- Draw Turbine Vortex Logo on Left (Center: 240, 250) ---
$cx = 240.0
$cy = 250.0
$scale = 3.6

function Pt([double]$x48, [double]$y48) {
    $px = [float]($cx + ($x48 - 24.0) * $scale)
    $py = [float]($cy + ($y48 - 24.0) * $scale)
    return [System.Drawing.PointF]::new($px, $py)
}

$cyanPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 56, 189, 248), 14.0)
$cyanPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$cyanPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

$purplePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 129, 140, 248), 14.0)
$purplePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$purplePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

# Arm 1: North to East (Cyan)
$g.DrawBezier($cyanPen, (Pt 24 6), (Pt 24 16), (Pt 32 20), (Pt 42 20))

# Arm 2: East to South (Indigo)
$g.DrawBezier($purplePen, (Pt 42 24), (Pt 32 24), (Pt 28 32), (Pt 28 42))

# Arm 3: South to West (Cyan)
$g.DrawBezier($cyanPen, (Pt 24 42), (Pt 24 32), (Pt 16 28), (Pt 6 28))

# Arm 4: West to North (Indigo)
$g.DrawBezier($purplePen, (Pt 6 24), (Pt 16 24), (Pt 20 16), (Pt 20 6))

# Central Grade Hub
$hubBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
$hubPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 15, 23, 42), 4.0)
$hubR = 14.0
$g.FillEllipse($hubBrush, [float]($cx - $hubR), [float]($cy - $hubR), [float]($hubR * 2), [float]($hubR * 2))
$g.DrawEllipse($hubPen, [float]($cx - $hubR), [float]($cy - $hubR), [float]($hubR * 2), [float]($hubR * 2))

# --- Right Side Typography & Branding ---
# Category / Game Badge
$badgeFont = [System.Drawing.Font]::new("Segoe UI", [float]11.0, [System.Drawing.FontStyle]::Bold)
$badgeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 56, 189, 248))
$g.DrawString("CITIES: SKYLINES II  •  CODE MOD", $badgeFont, $badgeBrush, [float]456.0, [float]108.0)

# Main Title: PROCEDURAL
$titleFont1 = [System.Drawing.Font]::new("Segoe UI", [float]38.0, [System.Drawing.FontStyle]::Bold)
$whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 248, 250, 252))
$g.DrawString("PROCEDURAL", $titleFont1, $whiteBrush, [float]452.0, [float]136.0)

# Main Title: INTERCHANGES (with cyan tint)
$titleFont2 = [System.Drawing.Font]::new("Segoe UI", [float]38.0, [System.Drawing.FontStyle]::Bold)
$cyanTextBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 56, 189, 248))
$g.DrawString("INTERCHANGES", $titleFont2, $cyanTextBrush, [float]452.0, [float]196.0)

# Subtitle / Description
$subFont = [System.Drawing.Font]::new("Segoe UI", [float]14.0, [System.Drawing.FontStyle]::Regular)
$grayBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 148, 163, 184))
$g.DrawString("Parametric Highway Generator & CAD Studio", $subFont, $grayBrush, [float]456.0, [float]268.0)

# Feature Badges
$pillFont = [System.Drawing.Font]::new("Segoe UI", [float]10.0, [System.Drawing.FontStyle]::Bold)
$pillBorderPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(120, 56, 189, 248), 1.5)
$pillBgBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(180, 15, 23, 42))
$pillTextBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 226, 232, 240))

$pills = @("Live 3D Ghost", "Bézier Splines", "Zero Weaving", "CAD Overlay")
$px = 456.0
$py = 318.0

foreach ($pill in $pills) {
    $size = $g.MeasureString($pill, $pillFont)
    $pw = [float]($size.Width + 18)
    $ph = [float]28.0
    $pillRect = [System.Drawing.RectangleF]::new([float]$px, [float]$py, [float]$pw, [float]$ph)
    $g.FillRectangle($pillBgBrush, $pillRect)
    $g.DrawRectangle($pillBorderPen, $pillRect.X, $pillRect.Y, $pillRect.Width, $pillRect.Height)
    $g.DrawString($pill, $pillFont, $pillTextBrush, [float]($px + 9), [float]($py + 5))
    $px += ($pw + 10)
}

# Version tag in bottom right
$versionFont = [System.Drawing.Font]::new("Segoe UI", [float]10.0, [System.Drawing.FontStyle]::Regular)
$versionBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(180, 100, 116, 139))
$g.DrawString("v0.0.5  |  Paradox Mods & Local Toolchain", $versionFont, $versionBrush, [float]456.0, [float]370.0)

# Save to Properties/Thumbnail.png
$outputPath = Join-Path $PSScriptRoot "Thumbnail.png"
if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}
$bmp.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$bmp.Dispose()
Write-Host "Thumbnail generated successfully at: $outputPath"
