# Compila y ejecuta las pruebas con las ventanas invisibles.
$ErrorActionPreference = 'Stop'
$out = "$PSScriptRoot\salida"
New-Item -ItemType Directory -Force $out | Out-Null
& "$PSScriptRoot\..\build.ps1" -Out "$out\PizarraTest.exe" -Define SELFTEST -Extra "$PSScriptRoot\SelfTest.cs"
$p = Start-Process "$out\PizarraTest.exe" -ArgumentList "`"$out`"" -PassThru
if (-not $p.WaitForExit(120000)) { $p.Kill(); throw 'Las pruebas tardaron demasiado' }
Get-Content "$out\resultado.txt" -Encoding UTF8 | Select-String 'FALLO|EXCEP|Fallos'
exit $p.ExitCode
