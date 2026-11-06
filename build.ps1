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
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 33, 36))), 16, 16, 224, 224)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(138, 180, 248)), 24
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'
$g.DrawBezier($pen, 56, 180, 100, 40, 130, 220, 200, 76)
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
$args += $Extra
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw "Error de compilacion" }
Remove-Item $ico -ErrorAction SilentlyContinue
Write-Output "Compilado: $Out"
