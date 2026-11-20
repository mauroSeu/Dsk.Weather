<#
.SYNOPSIS
Avvia Pixel Desktop Widget dalla cartella dist (build standard non-packed antivirus-friendly).
#>
$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $PSScriptRoot
$VbsPath = Join-Path $RootDir "dist\Avvia-PixelWidget.vbs"
$DllPath = Join-Path $RootDir "dist\PixelDesktopWidget.dll"

if (-not (Test-Path $DllPath)) {
    Write-Host "Build non trovata in '$DllPath'." -ForegroundColor Yellow
    Write-Host "Esegui prima:" -ForegroundColor Cyan
    Write-Host "  dotnet publish `"$RootDir\src\PixelDesktopWidget\PixelDesktopWidget.csproj`" -c Release -r win-x64 --no-self-contained -o `"$RootDir\dist`""
    exit 1
}

Start-Process -FilePath "wscript.exe" -ArgumentList "//B //Nologo `"$VbsPath`"" -WorkingDirectory (Split-Path -Parent $DllPath)
Write-Host "PixelDesktopWidget avviato tramite host ufficiale Microsoft .NET (zero popup Antivirus)." -ForegroundColor Green
