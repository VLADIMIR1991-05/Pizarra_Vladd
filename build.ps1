# Compila Pizarra.exe con el compilador incluido en Windows (.NET Framework 4.8).
# Uso: powershell -ExecutionPolicy Bypass -File build.ps1
param([string]$Out = "$PSScriptRoot\Pizarra.exe", [string[]]$Extra = @(), [string[]]$Define = @())
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Icono: página con un trazo azul, guardado como PNG dentro de un .ico
$ico = Join-Path $env:TEMP 'pizarra-icon.ico'
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$hex = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 128, 8), (New-Object System.Drawing.PointF 232, 68), (New-Object System.Drawing.PointF 232, 188), (New-Object System.Drawing.PointF 128, 248), (New-Object System.Drawing.PointF 24, 188), (New-Object System.Drawing.PointF 24, 68))
$g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(10, 15, 30))), $hex)
$cyan = [System.Drawing.Color]::FromArgb(34, 211, 238); $violet = [System.Drawing.Color]::FromArgb(167, 139, 250)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 24, 8), (New-Object System.Drawing.Point 232, 248), $cyan, $violet)
$edge = New-Object System.Drawing.Pen $grad, 10
$edge.LineJoin = 'Round'
$g.DrawPolygon($edge, $hex)
$pen = New-Object System.Drawing.Pen $grad, 22
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'
$g.DrawBezier($pen, 68, 172, 104, 60, 134, 208, 190, 84)
$g.Dispose()
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$w = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($ico))
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
$w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$png.Length); $w.Write([uint32]22)
$w.Write($png)
$w.Close()

$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$args = @(
  '/nologo', '/target:winexe', '/optimize+', '/platform:x64', "/out:$Out", "/win32icon:$ico",
  "/r:$wpf\PresentationCore.dll", "/r:$wpf\PresentationFramework.dll", "/r:$wpf\WindowsBase.dll",
  "/r:$fw\System.Xaml.dll", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll',
  "/r:$fw\System.Runtime.dll", "/r:$fw\System.Threading.Tasks.dll", "/r:$fw\System.Runtime.WindowsRuntime.dll",
  "/r:$env:WINDIR\System32\WinMetadata\Windows.Foundation.winmd", "/r:$env:WINDIR\System32\WinMetadata\Windows.UI.winmd",
  '/codepage:65001'
)
foreach ($d in $Define) { $args += "/define:$d" }
$args += "$PSScriptRoot\Pizarra.cs"
$args += "$PSScriptRoot\Pro.cs"
$args += "$PSScriptRoot\Video.cs"
$args += $Extra
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw "Error de compilacion" }
Remove-Item $ico -ErrorAction SilentlyContinue
Write-Output "Compilado: $Out"
