<#
.SYNOPSIS
Attiva, disattiva o controlla l'avvio automatico di Pixel Desktop Widget con Windows.
#>
param(
    [switch]$Enable,
    [switch]$Disable,
    [switch]$Status
)

$ErrorActionPreference = "Stop"

$RunPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$ValueName = "PixelDesktopWidget"
$RootDir = Split-Path -Parent $PSScriptRoot
$VbsPath = Join-Path $RootDir "dist\Avvia-PixelWidget.vbs"
$ExePath = Join-Path $RootDir "dist\PixelDesktopWidget.exe"

if ($Enable -and $Disable) {
    Write-Error "Specifica solo una tra -Enable e -Disable."
    exit 1
}

if ($Enable) {
    if (Test-Path $VbsPath) {
        $Cmd = "wscript.exe //B //Nologo `"$VbsPath`""
        Set-ItemProperty -Path $RunPath -Name $ValueName -Value $Cmd -Type String
        Write-Host "Avvio automatico ATTIVATO (via host Microsoft .NET): $Cmd" -ForegroundColor Green
        exit 0
    }
    if (-not (Test-Path $ExePath)) {
        Write-Error "Eseguibile non trovato in '$ExePath'. Esegui prima dotnet publish verso dist\."
        exit 1
    }

    Set-ItemProperty -Path $RunPath -Name $ValueName -Value "`"$ExePath`"" -Type String
    Write-Host "Avvio automatico ATTIVATO per: $ExePath" -ForegroundColor Green
    exit 0
}

if ($Disable) {
    Remove-ItemProperty -Path $RunPath -Name $ValueName -ErrorAction SilentlyContinue
    Write-Host "Avvio automatico DISATTIVATO." -ForegroundColor Yellow
    exit 0
}

$Current = Get-ItemProperty -Path $RunPath -Name $ValueName -ErrorAction SilentlyContinue | Select-Object -ExpandProperty $ValueName -ErrorAction SilentlyContinue
if ($Current) {
    Write-Host "Stato avvio automatico: ATTIVO -> $Current" -ForegroundColor Green
} else {
    Write-Host "Stato avvio automatico: DISATTIVO" -ForegroundColor Gray
}
