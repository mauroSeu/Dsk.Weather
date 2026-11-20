# STATO PROGETTO — Dsk.Weather

- **Stato:** Chiuso come **v1.3.7** (prodotto completo, verificato 22/22 test verdi, zero errori in runtime).
- **Percorso:** `./` (`Dsk.Weather`)
- **Stack:** C# .NET 8 WPF + WinForms (`NotifyIcon` isolato) + Win32 P/Invoke 64-bit safe, zero pacchetti NuGet, 0% GPU LLM.

## Moduli Principali
- `src\Dsk.Weather\Models\WidgetSettings.cs` (`SavedLocation`, `FavoriteLocations`, `PeekNextFavorite`/`CycleNextFavorite`, `RadarZoom` 5..8), `WeatherData.cs` (`WindSpeedMaxKmh`, `WindDirectionDeg`), `MaterialThemePalette.cs`
- `src\Dsk.Weather\Services\SettingsService.cs` (retry atomico 3x + `LogError` con stack trace), `LocalizationService.cs` (localizzazione completa in 6 lingue: `it`, `en`, `de`, `nl`, `es`, `pt`), `WeatherService.cs` (motore fisso ad alta precisione Open-Meteo `best_match` con modelli locali + ECMWF IFS e UV/raffiche completi, 7 giorni `forecast_days=7`, vento e direzione `0-360°`, testi localizzati), `RadarService.cs` (tile OSM ufficiali + filtro Dark Tactical + `ComputeRadiusKm` + nowcast/past 8 frame), `MaterialColorService.cs`, `DesktopPinService.cs` (`Win + D` protection), `AutostartService.cs` (via `Avvia-DskWeather.vbs` host Microsoft `dotnet.exe`)
- `src\Dsk.Weather\Controls\WeatherIconView.xaml(.cs)`, `WeatherSceneCanvas.xaml(.cs)` (`Freeze()` completo), `WeatherRadarView.xaml(.cs)` (`308x238`, Z5-Z8 mouse wheel, giorno/notte, HUD portata km + sonar)
- `src\Dsk.Weather\Icons\TrayIconFactory.cs`
- `src\Dsk.Weather\Windows\WidgetWindow.xaml(.cs)` (geometria fissa `338 px`, atmosfera dinamica condivisa tra Card e pannello Previsioni/Radar su tutti i layout, badge allerte meteo Giallo/Rosso accanto all'orologio, striscia 24h condivisa con Sparkline Bézier + `💧 %` + slot Tramonto/Alba all'orario esatto adattata allo stile di tutti e 3 i layout con scorrimento fluido via rotellina/drag e barra visibile solo su hover, tabella 7 giorni con `Lun 23`, `💧 %`, triangolino vento direzionale e `Min°/Max°`, cambio rapido città 1-click sulla riga località e menu contestuale, Radar auto alba/tramonto)
- `src\Dsk.Weather\Windows\SettingsWindow.xaml(.cs)` (UI floating Material You arrotondata sincronizzata live con `CurrentPalette`, toggle switch a pillola incluse allerte meteo, spunta stile predefinito, selettore lingua 6 lingue al posto dell'anteprima scena, rimossi selettore modello meteo e promemoria rapido, lista Posizioni preferite + `+ Aggiungi posizione`, pulsante Ricarica meteo)
- `src\Dsk.Weather\App.xaml(.cs)` (`ContextMenu` implicito sicuro con `Separator`, debounce 300 ms, `DispatcherUnhandledException`)
- `tests\Dsk.Weather.Tests\Program.cs` (`22/22 [PASS]` incluse istanziazione `SettingsWindow`/`ContextMenu`, ciclo preferiti, valutazione allerte meteo, reset stile predefinito, striscia 24h su tutti i layout, atmosfera dinamica sul pannello Previsioni, multilingua 6 lingue e slot Tramonto/Alba nella striscia 24h)
- `src\Dsk.Weather\Avvia-DskWeather.vbs`, `scripts\Launch-DskWeather.ps1` e `scripts\Toggle-Autostart.ps1` (avvio automatico attivo su `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`)
