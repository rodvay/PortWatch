$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Add-Type -AssemblyName System.Drawing

function New-PortWatchIcon {
    param([string]$OutPath)
    $sizes = @(16, 32, 48, 256)
    $pngList = New-Object System.Collections.Generic.List[byte[]]

    foreach ($sz in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad = [float]($sz * 0.06)
        $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($sz - $pad * 2), ($sz - $pad * 2))
        $rad = [float]($sz * 0.24)
        $d = $rad * 2.0

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect, [System.Drawing.Color]::FromArgb(255, 16, 28, 44), [System.Drawing.Color]::FromArgb(255, 9, 13, 21), 45.0)
        $g.FillPath($bgBrush, $path)

        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 52, 211, 153), [Math]::Max(1.2, $sz * 0.045))
        $g.DrawPath($borderPen, $path)

        # Network radar rings + connected nodes
        $r1 = $sz * 0.30
        $r2 = $sz * 0.16
        $cx = $sz * 0.50; $cy = $sz * 0.50
        $ringPen1 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, 56, 189, 248), [Math]::Max(1.2, $sz * 0.045))
        $ringPen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 52, 211, 153), [Math]::Max(1.3, $sz * 0.055))
        $g.DrawEllipse($ringPen1, [float]($cx - $r1), [float]($cy - $r1), [float]($r1 * 2), [float]($r1 * 2))
        $g.DrawEllipse($ringPen2, [float]($cx - $r2), [float]($cy - $r2), [float]($r2 * 2), [float]($r2 * 2))

        $nodeBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 56, 189, 248))
        $dot = [Math]::Max(2.2, $sz * 0.12)
        $g.FillEllipse($nodeBrush, [float]($cx - $dot * 0.5), [float]($cy - $dot * 0.5), [float]$dot, [float]$dot)

        $dot2 = [Math]::Max(2.0, $sz * 0.095)
        $emBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 52, 211, 153))
        $g.FillEllipse($emBrush, [float]($cx + $r1 * 0.72 - $dot2 * 0.5), [float]($cy - $r1 * 0.68 - $dot2 * 0.5), [float]$dot2, [float]$dot2)

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngList.Add($ms.ToArray())

        $ms.Dispose(); $emBrush.Dispose(); $nodeBrush.Dispose(); $ringPen2.Dispose(); $ringPen1.Dispose()
        $borderPen.Dispose(); $bgBrush.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($OutPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]; $bytes = $pngList[$i]
            $dim = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
            $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
            $offset += $bytes.Length
        }
        foreach ($bytes in $pngList) { $bw.Write($bytes) }
    } finally { $bw.Dispose(); $fs.Dispose() }
}

Write-Host "Generating app.ico..."
New-PortWatchIcon -OutPath (Join-Path $scriptDir "app.ico")

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "csc.exe not found under $env:WINDIR\Microsoft.NET" }

Write-Host "Compiling PortWatch.exe..."
& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    /codepage:65001 `
    /utf8output `
    /win32icon:"$scriptDir\app.ico" `
    /win32manifest:"$scriptDir\app.manifest" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /out:"$scriptDir\PortWatch.exe" `
    "$scriptDir\PortNative.cs" `
    "$scriptDir\NetworkEngine.cs" `
    "$scriptDir\NetHudForm.cs" `
    "$scriptDir\Program.cs"

if ($LASTEXITCODE -ne 0) { throw "Compilation failed (ExitCode=$LASTEXITCODE)" }

$exeInfo = Get-Item (Join-Path $scriptDir "PortWatch.exe")
Write-Host ("Successfully built: {0} ({1:N1} KB)" -f $exeInfo.FullName, ($exeInfo.Length / 1024.0))
