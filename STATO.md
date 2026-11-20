# STATO PROGETTO — PixelDesktopWidget

- **Stato:** Chiuso come **v1.2 Final** (prodotto completo, verificato 16/16 test verdi, zero errori in runtime).
- **Percorso:** `./` (`PixelDesktopWidget`)
- **Stack:** C# .NET 8 WPF + WinForms (`NotifyIcon` isolato) + Win32 P/Invoke 64-bit safe, zero pacchetti NuGet, 0% GPU LLM.

## Moduli Principali
- `src\PixelDesktopWidget\Models\WidgetSettings.cs` (`SavedLocation`, `FavoriteLocations`, `PeekNextFavorite`/`CycleNextFavorite`, `RadarZoom` 5..8), `WeatherData.cs` (`WindSpeedMaxKmh`, `WindDirectionDeg`), `MaterialThemePalette.cs`
- `src\PixelDesktopWidget\Services\SettingsService.cs` (retry atomico 3x + `LogError` con stack trace), `WeatherService.cs` (7 giorni `forecast_days=7`, vento e direzione `0-360°`), `RadarService.cs` (tile OSM ufficiali + filtro Dark Tactical + `ComputeRadiusKm` + nowcast/past 8 frame), `MaterialColorService.cs`, `DesktopPinService.cs` (`Win + D` protection), `AutostartService.cs` (via `Avvia-PixelWidget.vbs` host Microsoft `dotnet.exe`)
- `src\PixelDesktopWidget\Controls\WeatherIconView.xaml(.cs)`, `WeatherSceneCanvas.xaml(.cs)` (`Freeze()` completo), `WeatherRadarView.xaml(.cs)` (`308x238`, Z5-Z8 mouse wheel, giorno/notte, HUD portata km + sonar)
- `src\PixelDesktopWidget\Icons\TrayIconFactory.cs`
- `src\PixelDesktopWidget\Windows\WidgetWindow.xaml(.cs)` (geometria fissa `338 px`, striscia 24h con Sparkline Bézier + `💧 %`, tabella 7 giorni con `Lun 23`, `💧 %`, triangolino vento direzionale e `Min°/Max°`, cambio rapido città 1-click sulla riga località e menu contestuale, Radar auto alba/tramonto)
- `src\PixelDesktopWidget\Windows\SettingsWindow.xaml(.cs)` (UI floating Material You arrotondata sincronizzata live con `CurrentPalette`, toggle switch a pillola, lista Posizioni preferite + `+ Aggiungi posizione`, pulsante Ricarica meteo)
- `src\PixelDesktopWidget\App.xaml(.cs)` (`ContextMenu` implicito sicuro con `Separator`, debounce 300 ms, `DispatcherUnhandledException`)
- `tests\PixelDesktopWidget.Tests\Program.cs` (`16/16 [PASS]` incluse istanziazione `SettingsWindow`/`ContextMenu` e ciclo preferiti)
- `src\PixelDesktopWidget\Avvia-PixelWidget.vbs`, `scripts\Launch-PixelWidget.ps1` e `scripts\Toggle-Autostart.ps1` (avvio automatico attivo su `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`)
