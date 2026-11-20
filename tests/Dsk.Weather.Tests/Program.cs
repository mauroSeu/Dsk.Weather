using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Media;
using Dsk.Weather.Icons;
using Dsk.Weather.Models;
using Dsk.Weather.Services;
using Drawing = System.Drawing;

namespace Dsk.Weather.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var tests = new (string Name, Action Body)[]
            {
                ("WmoMappingItalianAndIcons", TestWmoMapping),
                ("ForecastJsonParsingAndInvariantUrl", TestForecastJsonAndInvariantCulture),
                ("GeocodingJsonParsing", TestGeocodingJson),
                ("MaterialColorExtractionAndPalettes", TestMaterialColorService),
                ("SettingsAtomicPersistenceAndClamp", TestSettingsService),
                ("TrayIconCreationAndDispose", TestTrayIconFactory),
                ("AtAGlanceInsightDeterministic", TestAtAGlanceInsight),
                ("OpenMeteoModelUrlAndExtraFields", TestOpenMeteoModelUrlAndExtraFields),
                ("RadarMercatorTileMath", TestRadarMercatorTileMath),
                ("DynamicAtmosphereBrushGeneration", TestDynamicAtmosphereBrush),
                ("AdvancedSettingsAndLegacyJsonPersistence", TestAdvancedSettingsAndLegacyJson),
                ("HourlyForecast24HoursParsing", TestHourlyForecast24Hours),
                ("WeatherAnimationsSettingPersistence", TestWeatherAnimationsSetting),
                ("WeatherAnimationsAllTenScenesStartCleanly", TestAllTenScenesAndIconsStartCleanly),
                ("SettingsWindowAndContextMenuInstantiation", TestSettingsWindowAndContextMenuInstantiation),
                ("FavoriteLocationCycleHelper", TestFavoriteLocationCycle),
                ("WeatherAlertsEvaluationAndSeverity", TestWeatherAlertsEvaluationAndSeverity),
                ("DefaultStyleCheckAndResetHelper", TestDefaultStyleCheckAndReset),
                ("HourlyStripAcrossAllLayouts", TestHourlyStripAcrossAllLayouts),
                ("ForecastDrawerDynamicAtmosphere", TestForecastDrawerDynamicAtmosphere),
                ("MultiLanguageLocalizationAndSettings", TestMultiLanguageLocalizationAndSettings),
                ("HourlyStripSunsetAndSunriseSlots", TestHourlyStripSunsetAndSunriseSlots),
                ("OfficialAlertsParsingAndMatching", TestOfficialAlertsParsingAndMatching)
            };

            int failures = 0;
            foreach ((string name, Action body) in tests)
            {
                try
                {
                    body();
                    Console.WriteLine($"[PASS] {name}");
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                }
            }

            Console.WriteLine($"Riepilogo: {tests.Length - failures}/{tests.Length} test superati.");
            return failures == 0 ? 0 : 1;
        }

        private static void TestWmoMapping()
        {
            AssertEqual("Sereno", WeatherService.DescribeWmoCodeItalian(0), "WMO 0");
            AssertEqual("Poco nuvoloso", WeatherService.DescribeWmoCodeItalian(2), "WMO 2");
            AssertEqual("Nuvoloso", WeatherService.DescribeWmoCodeItalian(3), "WMO 3");
            AssertEqual("Nebbia", WeatherService.DescribeWmoCodeItalian(45), "WMO 45");
            AssertEqual("Pioviggine", WeatherService.DescribeWmoCodeItalian(51), "WMO 51");
            AssertEqual("Pioggia moderata", WeatherService.DescribeWmoCodeItalian(63), "WMO 63");
            AssertEqual("Neve", WeatherService.DescribeWmoCodeItalian(73), "WMO 73");
            AssertEqual("Rovesci", WeatherService.DescribeWmoCodeItalian(80), "WMO 80");
            AssertEqual("Temporale", WeatherService.DescribeWmoCodeItalian(95), "WMO 95");
            AssertEqual("Variabile", WeatherService.DescribeWmoCodeItalian(999), "WMO sconosciuto");

            AssertEqual(WeatherIconKind.ClearDay, WeatherService.MapWmoToIcon(0, true), "Icona 0 giorno");
            AssertEqual(WeatherIconKind.ClearNight, WeatherService.MapWmoToIcon(0, false), "Icona 0 notte");
            AssertEqual(WeatherIconKind.PartlyCloudyDay, WeatherService.MapWmoToIcon(2, true), "Icona 2 giorno");
            AssertEqual(WeatherIconKind.PartlyCloudyNight, WeatherService.MapWmoToIcon(2, false), "Icona 2 notte");
            AssertEqual(WeatherIconKind.Cloudy, WeatherService.MapWmoToIcon(3, true), "Icona 3");
            AssertEqual(WeatherIconKind.Fog, WeatherService.MapWmoToIcon(45, true), "Icona 45");
            AssertEqual(WeatherIconKind.Drizzle, WeatherService.MapWmoToIcon(51, true), "Icona 51");
            AssertEqual(WeatherIconKind.Rain, WeatherService.MapWmoToIcon(63, false), "Icona 63");
            AssertEqual(WeatherIconKind.Snow, WeatherService.MapWmoToIcon(73, true), "Icona 73");
            AssertEqual(WeatherIconKind.Thunderstorm, WeatherService.MapWmoToIcon(95, false), "Icona 95");

            AssertEqual("20°", WeatherService.FormatTemp(20.2, TemperatureUnit.Celsius), "Temp Celsius");
            AssertEqual("68°", WeatherService.FormatTemp(20.0, TemperatureUnit.Fahrenheit), "Temp Fahrenheit");
        }

        private static void TestForecastJsonAndInvariantCulture()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");

                string url = WeatherService.BuildForecastUrl(45.4642, 9.1900);
                AssertTrue(url.Contains("latitude=45.4642", StringComparison.Ordinal), "Latitudine con punto sotto it-IT");
                AssertTrue(url.Contains("longitude=9.19", StringComparison.Ordinal), "Longitudine con punto sotto it-IT");
                AssertTrue(!url.Contains(",", StringComparison.Ordinal) || url.Contains("current=", StringComparison.Ordinal), "URL valida");
                AssertTrue(!url.Contains("45,4642", StringComparison.Ordinal), "Nessuna virgola decimale nella URL");

                const string sampleJson = """
                {
                  "current": {
                    "temperature_2m": 14.6,
                    "apparent_temperature": 13.1,
                    "weather_code": 2,
                    "is_day": 1,
                    "wind_speed_10m": 11.4,
                    "relative_humidity_2m": 64
                  },
                  "hourly": {
                    "time": [
                      "2026-11-19T21:00",
                      "2026-11-19T22:00",
                      "2026-11-19T23:00",
                      "2026-11-20T00:00",
                      "2026-11-20T01:00",
                      "2026-11-20T02:00",
                      "2026-11-20T03:00",
                      "2026-11-20T04:00"
                    ],
                    "temperature_2m": [15.0, 14.5, 13.8, 12.9, 12.1, 11.5, 11.0, 10.7],
                    "weather_code": [2, 2, 3, 61, 61, 3, 2, 1],
                    "precipitation_probability": [5, 10, 20, 65, 70, 30, 15, 5],
                    "is_day": [0, 0, 0, 0, 0, 0, 0, 0]
                  },
                  "daily": {
                    "time": [
                      "2026-11-19",
                      "2026-11-20",
                      "2026-11-21",
                      "2026-11-22",
                      "2026-11-23"
                    ],
                    "weather_code": [2, 61, 3, 0, 1],
                    "temperature_2m_max": [17.2, 14.0, 15.1, 16.4, 17.0],
                    "temperature_2m_min": [9.4, 8.2, 7.5, 6.8, 7.9],
                    "precipitation_probability_max": [20, 75, 30, 5, 10]
                  }
                }
                """;

                var refNow = new DateTime(2026, 11, 19, 22, 15, 0);
                WeatherSnapshot snap = WeatherService.ParseForecastJson(sampleJson, "Milano", refNow);

                AssertEqual("Milano", snap.CityName, "Nome città");
                AssertTrue(Math.Abs(snap.TemperatureC - 14.6) < 0.01, "Temperatura corrente");
                AssertEqual(2, snap.WeatherCode, "WeatherCode corrente");
                AssertEqual(6, snap.NextHours.Count, "Tutte le 6 previsioni orarie future parsate");
                AssertEqual(new DateTime(2026, 11, 19, 23, 0, 0), snap.NextHours[0].LocalTime, "Prima ora futura = 23:00");
                AssertEqual(new DateTime(2026, 11, 20, 4, 0, 0), snap.NextHours[5].LocalTime, "Sesta ora futura = 04:00");
                AssertEqual(5, snap.Daily.Count, "5 giorni daily");
                AssertTrue(Math.Abs(snap.TodayMaxC - 17.2) < 0.01, "TodayMaxC");
                AssertTrue(Math.Abs(snap.TodayMinC - 9.4) < 0.01, "TodayMinC");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        private static void TestGeocodingJson()
        {
            const string geoJson = """
            {
              "results": [
                {
                  "name": "Milano",
                  "admin1": "Lombardia",
                  "country": "Italia",
                  "latitude": 45.46427,
                  "longitude": 9.18951
                }
              ]
            }
            """;

            List<GeoLocationResult> list = WeatherService.ParseGeocodingJson(geoJson);
            AssertEqual(1, list.Count, "Conteggio risultati geocoding");
            AssertEqual("Milano, Lombardia, Italia", list[0].DisplayName, "DisplayName completo");
            AssertTrue(Math.Abs(list[0].Latitude - 45.46427) < 0.0001, "Latitudine Milano");

            List<GeoLocationResult> empty = WeatherService.ParseGeocodingJson("{}");
            AssertEqual(0, empty.Count, "Geocoding vuoto");
        }

        private static void TestMaterialColorService()
        {
            // Round-trip RGB <-> HSL
            Color input = Color.FromRgb(0x3B, 0x82, 0xF6);
            (double h, double s, double l) = MaterialColorService.RgbToHsl(input.R, input.G, input.B);
            Color roundTrip = MaterialColorService.HslToRgb(h, s, l);
            AssertTrue(Math.Abs(input.R - roundTrip.R) <= 2, "Round-trip R");
            AssertTrue(Math.Abs(input.G - roundTrip.G) <= 2, "Round-trip G");
            AssertTrue(Math.Abs(input.B - roundTrip.B) <= 2, "Round-trip B");

            // Buffer blu vibrante (BGRA)
            byte[] bluePixels = CreateSolidBgraBuffer(16, 0xF6, 0x82, 0x3B, 0xFF);
            Color? blueSeed = MaterialColorService.ExtractDominantSeedFromBgra32(bluePixels, 16);
            AssertTrue(blueSeed.HasValue, "Seed blu estratto");
            AssertTrue(blueSeed!.Value.B > blueSeed.Value.R, "Blu dominante su rosso");

            // Buffer rosso caldo (BGRA)
            byte[] redPixels = CreateSolidBgraBuffer(16, 0x3A, 0x45, 0xD9, 0xFF);
            Color? redSeed = MaterialColorService.ExtractDominantSeedFromBgra32(redPixels, 16);
            AssertTrue(redSeed.HasValue, "Seed rosso estratto");
            AssertTrue(redSeed!.Value.R > redSeed.Value.B, "Rosso dominante su blu");

            // Buffer grigio neutro -> null fallback
            byte[] grayPixels = CreateSolidBgraBuffer(16, 0x80, 0x80, 0x80, 0xFF);
            Color? graySeed = MaterialColorService.ExtractDominantSeedFromBgra32(grayPixels, 16);
            AssertTrue(!graySeed.HasValue, "Immagine grigia deve restituire null");

            // Generazione palette per tutti i 4 temi
            var service = new MaterialColorService();
            foreach (WidgetThemeMode mode in Enum.GetValues<WidgetThemeMode>())
            {
                MaterialThemePalette palette = service.BuildPalette(mode, Color.FromRgb(0x3B, 0x82, 0xF6));
                AssertTrue(palette.Surface != palette.OnSurface, $"Contrasto Surface/OnSurface per {mode}");
                AssertTrue(palette.Primary.A == 255, $"Primary opaco per {mode}");
            }
        }

        private static void TestSettingsService()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Dsk.WeatherTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SettingsService(tempDir);
                var s = new WidgetSettings
                {
                    Left = 120.5,
                    Top = 88.0,
                    LayoutStyle = WidgetLayoutStyle.MaterialCompact,
                    ThemeMode = WidgetThemeMode.GlassDark,
                    PinMode = WindowPinMode.BottomMost,
                    Scale = 2.5, // dovrà essere clampato a 1.50
                    BackgroundOpacity = -0.5, // dovrà essere clampato a 0.0
                    Use24HourFormat = false,
                    ShowSeconds = true,
                    ShowHourlyStrip = false,
                    LockPosition = true,
                    TempUnit = TemperatureUnit.Fahrenheit,
                    CityName = "Torino",
                    CountryName = "Italia",
                    Latitude = 45.0703,
                    Longitude = 7.6869,
                    LocationInitialized = true
                };

                svc.Save(s);
                WidgetSettings loaded = svc.Load();

                AssertEqual(WidgetLayoutStyle.MaterialCompact, loaded.LayoutStyle, "Layout persistito");
                AssertEqual(WidgetThemeMode.GlassDark, loaded.ThemeMode, "Tema persistito");
                AssertEqual(WindowPinMode.BottomMost, loaded.PinMode, "PinMode persistito");
                AssertTrue(Math.Abs(loaded.Scale - 1.50) < 0.001, "Clamp Scale massimo");
                AssertTrue(Math.Abs(loaded.BackgroundOpacity - 0.0) < 0.001, "Clamp Opacity minimo");
                AssertEqual("Torino", loaded.CityName, "Città persistita");
                AssertEqual(TemperatureUnit.Fahrenheit, loaded.TempUnit, "Unità persistita");

                (double clLeft, double clTop) = SettingsService.ClampPosition(-500, 2000, 360, 220, 0, 0, 1920, 1080);
                AssertEqual(0.0, clLeft, "Clamp sinistra");
                AssertEqual(860.0, clTop, "Clamp inferiore");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestTrayIconFactory()
        {
            using Drawing.Icon icon = TrayIconFactory.CreateTrayIcon();
            AssertEqual(32, icon.Width, "Larghezza tray icon");
            AssertEqual(32, icon.Height, "Altezza tray icon");
        }

        private static void TestAtAGlanceInsight()
        {
            var baseNow = new DateTime(2026, 11, 19, 14, 0, 0);

            var rainSoon = new WeatherSnapshot
            {
                WeatherCode = 2,
                IsDay = true,
                TemperatureC = 16.0,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new HourlyForecastPoint { LocalTime = baseNow.AddHours(3), WeatherCode = 61, PrecipProbability = 75, TemperatureC = 13.0 }
                }
            };
            string rainInsight = WeatherService.BuildAtAGlanceInsight(rainSoon, baseNow);
            AssertTrue(rainInsight.Contains("Pioggia in arrivo", StringComparison.Ordinal) && rainInsight.Contains("17:00", StringComparison.Ordinal), "Insight pioggia in arrivo");

            var storm = new WeatherSnapshot { WeatherCode = 95, IsDay = true };
            AssertEqual("Temporale in corso · Prestare attenzione", WeatherService.BuildAtAGlanceInsight(storm, baseNow), "Insight temporale");

            var windy = new WeatherSnapshot { WeatherCode = 1, IsDay = true, WindSpeedKmh = 42.0 };
            AssertTrue(WeatherService.BuildAtAGlanceInsight(windy, baseNow).Contains("Vento sostenuto", StringComparison.Ordinal), "Insight vento");

            var clearNight = new WeatherSnapshot { WeatherCode = 0, IsDay = false };
            AssertEqual("Serata limpida e tranquilla", WeatherService.BuildAtAGlanceInsight(clearNight, baseNow), "Insight serata serena");
        }

        private static void TestOpenMeteoModelUrlAndExtraFields()
        {
            string urlAuto = WeatherService.BuildForecastUrl(45.4642, 9.19, WeatherModelPreference.Auto);
            string urlEcmwf = WeatherService.BuildForecastUrl(45.4642, 9.19, WeatherModelPreference.EcmwfIfs);
            string urlIcon = WeatherService.BuildForecastUrl(45.4642, 9.19, WeatherModelPreference.DwdIcon);

            AssertTrue(urlAuto.Contains("models=best_match", StringComparison.Ordinal), "Model Auto");
            AssertTrue(urlEcmwf.Contains("models=ecmwf_ifs025", StringComparison.Ordinal), "Model ECMWF");
            AssertTrue(urlIcon.Contains("models=icon_seamless", StringComparison.Ordinal), "Model ICON");
            AssertTrue(urlAuto.Contains("wind_speed_10m_max,wind_direction_10m_dominant,sunrise,sunset,uv_index_max", StringComparison.Ordinal), "Campi extra daily");
            AssertEqual("NE", WeatherService.DescribeWindDirectionCardinal(45), "Direzione cardinale NE");
            AssertEqual("SO", WeatherService.DescribeWindDirectionCardinal(225), "Direzione cardinale SO");

            const string json = """
            {
              "current": { "temperature_2m": 21.0, "apparent_temperature": 21.5, "weather_code": 0, "is_day": 1, "wind_speed_10m": 8.0, "wind_direction_10m": 135, "relative_humidity_2m": 45, "uv_index": 6.4 },
              "hourly": {
                "time": ["2026-11-19T15:00", "2026-11-19T16:00"],
                "temperature_2m": [21.5, 20.0],
                "weather_code": [0, 1],
                "precipitation_probability": [0, 5],
                "wind_speed_10m": [10.0, 12.0],
                "wind_direction_10m": [140, 150],
                "is_day": [1, 1]
              },
              "daily": {
                "time": ["2026-11-19"],
                "weather_code": [0],
                "temperature_2m_max": [22.0],
                "temperature_2m_min": [11.0],
                "precipitation_probability_max": [5],
                "wind_speed_10m_max": [18.5],
                "wind_direction_10m_dominant": [315],
                "sunrise": ["2026-11-19T07:18"],
                "sunset": ["2026-11-19T16:52"],
                "uv_index_max": [6.4]
              }
            }
            """;

            WeatherSnapshot snap = WeatherService.ParseForecastJson(json, "Milano", new DateTime(2026, 11, 19, 14, 10, 0));
            AssertTrue(Math.Abs(snap.CurrentUvIndex - 6.4) < 0.01, "UV index corrente");
            AssertEqual(135, snap.WindDirectionDeg, "Direzione vento corrente estratta");
            AssertTrue(Math.Abs(snap.Daily[0].WindSpeedMaxKmh - 18.5) < 0.01, "Vento max daily estratto");
            AssertEqual(315, snap.Daily[0].WindDirectionDeg, "Direzione vento daily estratta");
            AssertTrue(snap.SunriseLocal.HasValue && snap.SunriseLocal.Value.Hour == 7 && snap.SunriseLocal.Value.Minute == 18, "Alba estratta");
            AssertTrue(snap.SunsetLocal.HasValue && snap.SunsetLocal.Value.Hour == 16 && snap.SunsetLocal.Value.Minute == 52, "Tramonto estratto");
            AssertEqual(2, snap.Next12Hours.Count, "Next12Hours popolato");
        }

        private static void TestRadarMercatorTileMath()
        {
            (double tx, double ty) = RadarService.LatLonToTileExact(45.4642, 9.1900, 7);
            AssertTrue(tx > 66.0 && tx < 68.0, "Tile X Milano zoom 7");
            AssertTrue(ty > 45.0 && ty < 47.0, "Tile Y Milano zoom 7");

            RadarViewportPlan plan = RadarService.ComputeTileGrid(45.4642, 9.1900, 7, 340, 210);
            AssertEqual(7, plan.Zoom, "Zoom radar plan");
            AssertEqual(170.0, plan.MarkerX, "MarkerX centrato");
            AssertEqual(105.0, plan.MarkerY, "MarkerY centrato");
            AssertTrue(plan.Tiles.Count >= 4, "Griglia tile sufficiente a coprire 340x210");
            int radiusKm = RadarService.ComputeRadiusKm(45.4642, 7, 56.0);
            AssertTrue(radiusKm >= 30 && radiusKm <= 60, "Raggio km radar coerente a zoom 7");
        }

        private static void TestDynamicAtmosphereBrush()
        {
            var svc = new MaterialColorService();
            MaterialThemePalette palette = svc.BuildPalette(WidgetThemeMode.PixelDark);
            var weather = new WeatherSnapshot { WeatherCode = 63, IsDay = true };

            Brush brush = MaterialColorService.BuildAtmosphereBrush(weather, palette, WidgetThemeMode.PixelDark, 0.85, DateTime.Now);
            AssertTrue(brush is LinearGradientBrush, "Atmosfera genera LinearGradientBrush");
            AssertTrue(brush.IsFrozen, "Atmosfera brush deve essere frozen");
        }

        private static void TestAdvancedSettingsAndLegacyJson()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "Dsk.WeatherTests_v11_" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SettingsService(tempDir);
                var s = new WidgetSettings
                {
                    EnableDynamicAtmosphere = false,
                    RadarZoom = 99 // clampato a 8
                };
                s.AddOrSelectLocation("Lemmer", "The Netherlands", 52.8491, 5.699);
                s.AddOrSelectLocation("Serramanna", "Italia", 39.4225, 8.9219);

                svc.Save(s);
                WidgetSettings loaded = svc.Load();

                AssertEqual(false, loaded.EnableDynamicAtmosphere, "Flag atmosfera persistito");
                AssertEqual(8, loaded.RadarZoom, "RadarZoom clampato a 8");
                AssertEqual(2, loaded.FavoriteLocations.Count, "Posizioni preferite persistite");
                AssertEqual("Serramanna", loaded.CityName, "Ultima città selezionata attiva");

                // Verifica tolleranza a settings.json legacy contenente campi rimossi (WeatherModel / QuickNote)
                string legacyJson = """
                {
                  "WeatherModel": "EcmwfIfs",
                  "QuickNote": "Vecchia nota",
                  "CityName": "Roma",
                  "EnableDynamicAtmosphere": true,
                  "RadarZoom": 7
                }
                """;
                File.WriteAllText(Path.Combine(tempDir, "settings.json"), legacyJson);
                WidgetSettings legacyLoaded = svc.Load();
                AssertEqual("Roma", legacyLoaded.CityName, "Settings legacy JSON caricato senza errori");
                AssertEqual(true, legacyLoaded.EnableDynamicAtmosphere, "Flag atmosfera da legacy JSON");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestHourlyForecast24Hours()
        {
            var baseHour = new DateTime(2026, 11, 19, 23, 0, 0);
            var times = new List<string>(30);
            var temps = new List<string>(30);
            var codes = new List<string>(30);
            var probs = new List<string>(30);
            var winds = new List<string>(30);
            var isDays = new List<string>(30);

            for (int i = 0; i < 30; i++)
            {
                DateTime dt = baseHour.AddHours(i);
                times.Add($"\"{dt.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}\"");
                temps.Add((10.0 + (i % 8)).ToString("0.0", CultureInfo.InvariantCulture));
                codes.Add("2");
                probs.Add(((i * 5) % 100).ToString(CultureInfo.InvariantCulture));
                winds.Add("12.0");
                isDays.Add(dt.Hour >= 7 && dt.Hour <= 17 ? "1" : "0");
            }

            string json = $$"""
            {
              "current": { "temperature_2m": 11.0, "apparent_temperature": 9.5, "weather_code": 2, "is_day": 0, "wind_speed_10m": 10.0, "relative_humidity_2m": 70, "uv_index": 0.0 },
              "hourly": {
                "time": [{{string.Join(",", times)}}],
                "temperature_2m": [{{string.Join(",", temps)}}],
                "weather_code": [{{string.Join(",", codes)}}],
                "precipitation_probability": [{{string.Join(",", probs)}}],
                "wind_speed_10m": [{{string.Join(",", winds)}}],
                "is_day": [{{string.Join(",", isDays)}}]
              },
              "daily": {
                "time": ["2026-11-19"],
                "weather_code": [2],
                "temperature_2m_max": [16.0],
                "temperature_2m_min": [8.0],
                "precipitation_probability_max": [20]
              }
            }
            """;

            var refNow = new DateTime(2026, 11, 19, 23, 35, 0);
            WeatherSnapshot snap = WeatherService.ParseForecastJson(json, "Milano", refNow);

            AssertEqual(24, snap.NextHours.Count, "Previsioni orarie esattamente 24 ore");
            AssertEqual(12, snap.Next12Hours.Count, "Next12Hours limitato a 12 ore");
            AssertEqual(new DateTime(2026, 11, 20, 0, 0, 0), snap.NextHours[0].LocalTime, "Prima ora futura = 00:00");
            AssertEqual(new DateTime(2026, 11, 20, 23, 0, 0), snap.NextHours[23].LocalTime, "24esima ora futura = 23:00 giorno successivo");
        }

        private static void TestWeatherAnimationsSetting()
        {
            var defaults = new WidgetSettings();
            AssertEqual(true, defaults.EnableWeatherAnimations, "Default EnableWeatherAnimations = true");

            WidgetSettings cloned = defaults.Clone();
            AssertEqual(true, cloned.EnableWeatherAnimations, "Clone EnableWeatherAnimations");

            string tempDir = Path.Combine(Path.GetTempPath(), "Dsk.WeatherTests_Anim_" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SettingsService(tempDir);
                defaults.EnableWeatherAnimations = false;
                svc.Save(defaults);
                WidgetSettings loaded = svc.Load();
                AssertEqual(false, loaded.EnableWeatherAnimations, "Persistenza EnableWeatherAnimations = false");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestAllTenScenesAndIconsStartCleanly()
        {
            var scene = new Dsk.Weather.Controls.WeatherSceneCanvas();
            var icon = new Dsk.Weather.Controls.WeatherIconView();

            foreach (WeatherIconKind kind in Enum.GetValues<WeatherIconKind>())
            {
                scene.Configure(kind, enabled: true, isLightTheme: false);
                AssertTrue(scene.ActiveAnimationCount > 0, $"Scene {kind} (dark) deve avere animazioni attive");

                scene.Configure(kind, enabled: true, isLightTheme: true);
                AssertTrue(scene.ActiveAnimationCount > 0, $"Scene {kind} (light) deve avere animazioni attive");

                icon.IconKind = kind;
                icon.Animate = true;
                AssertTrue(icon.ActiveAnimationCount > 0, $"Icona {kind} deve avere animazioni attive");
            }

            scene.Stop();
            AssertEqual(0, scene.ActiveAnimationCount, "Scene.Stop() azzera animazioni");

            icon.Animate = false;
            AssertEqual(0, icon.ActiveAnimationCount, "Icon.Animate=false azzera animazioni");
        }

        private static void TestSettingsWindowAndContextMenuInstantiation()
        {
            if (System.Windows.Application.Current is null)
            {
                var app = new Dsk.Weather.App();
                app.InitializeComponent();
            }

            var settings = new WidgetSettings();
            settings.AddOrSelectLocation("Lemmer", "The Netherlands", 52.8491, 5.699);
            settings.AddOrSelectLocation("Serramanna", "Italia", 39.4225, 8.9219);

            var weatherSvc = new WeatherService();
            var autostartSvc = new AutostartService();
            var window = new Dsk.Weather.Windows.SettingsWindow(
                settings,
                weatherSvc,
                autostartSvc,
                _ => { },
                () => { });

            try
            {
                AssertTrue(
                    window.ActiveLocationText.Text.Contains("Serramanna", StringComparison.Ordinal),
                    "ActiveLocationText deve contenere Serramanna");

                var colorSvc = new MaterialColorService();
                window.ApplyPalette(colorSvc.BuildPalette(WidgetThemeMode.MaterialLight), isLightTheme: true);
                window.ApplyTemplate();
                window.Measure(new System.Windows.Size(512, 706));

                window.ApplyPalette(colorSvc.BuildPalette(WidgetThemeMode.PixelDark), isLightTheme: false);
                window.Measure(new System.Windows.Size(512, 706));
            }
            finally
            {
                window.Close();
            }

            var contextMenu = new System.Windows.Controls.ContextMenu();
            if (System.Windows.Application.Current?.TryFindResource(typeof(System.Windows.Controls.ContextMenu)) is System.Windows.Style cmStyle)
            {
                contextMenu.Style = cmStyle;
            }
            contextMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Aggiorna meteo" });
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            contextMenu.ApplyTemplate();
            contextMenu.Measure(new System.Windows.Size(200, 200));
            AssertEqual(2, contextMenu.Items.Count, "ContextMenu con MenuItem e Separator istanziato senza errori");
        }

        private static void TestFavoriteLocationCycle()
        {
            var settings = new WidgetSettings();
            settings.AddOrSelectLocation("Lemmer", "The Netherlands", 52.8491, 5.699);
            AssertTrue(settings.PeekNextFavorite() is null, "Con una sola città PeekNextFavorite deve essere null");
            AssertTrue(settings.CycleNextFavorite() is null, "Con una sola città CycleNextFavorite deve essere null");

            settings.AddOrSelectLocation("Serramanna", "Italia", 39.42335, 8.92243);
            AssertEqual("Serramanna", settings.CityName, "Città attiva iniziale Serramanna");

            SavedLocation? peeked = settings.PeekNextFavorite();
            AssertTrue(peeked is not null && peeked.Name == "Lemmer", "Prossimo preferito atteso: Lemmer");

            SavedLocation? switched1 = settings.CycleNextFavorite();
            AssertTrue(switched1 is not null && switched1.Name == "Lemmer" && settings.CityName == "Lemmer", "Primo ciclo deve passare a Lemmer");

            SavedLocation? switched2 = settings.CycleNextFavorite();
            AssertTrue(switched2 is not null && switched2.Name == "Serramanna" && settings.CityName == "Serramanna", "Secondo ciclo deve tornare a Serramanna");
        }

        private static void TestWeatherAlertsEvaluationAndSeverity()
        {
            var baseNow = new DateTime(2026, 11, 20, 12, 0, 0);

            // 1. Tempo neutro -> None
            var mild = new WeatherSnapshot
            {
                TemperatureC = 18.0,
                ApparentTemperatureC = 18.0,
                WeatherCode = 1,
                IsDay = true,
                WindSpeedKmh = 10.0,
                WindGustsKmh = 18.0,
                CurrentUvIndex = 3.0
            };
            AssertEqual(WeatherAlertLevel.None, WeatherService.EvaluateWeatherAlert(mild, TemperatureUnit.Celsius, baseNow).Level, "Tempo mite deve restituire allerta None");

            // 2. Gelo notturno entro 12h -> Yellow
            var frostAhead = new WeatherSnapshot
            {
                TemperatureC = 5.0,
                WeatherCode = 2,
                IsDay = true,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new HourlyForecastPoint { LocalTime = baseNow.AddHours(6), TemperatureC = -2.0, WeatherCode = 0 }
                }
            };
            WeatherAlertInfo frostAlert = WeatherService.EvaluateWeatherAlert(frostAhead, TemperatureUnit.Celsius, baseNow);
            AssertEqual(WeatherAlertLevel.Yellow, frostAlert.Level, "Gelo -2° deve attivare Allerta Gialla");
            AssertEqual("RISCHIO GELO", frostAlert.Title, "Titolo allerta gelo");

            // 3. Raffiche 62 km/h -> Yellow (ALLERTA VENTO)
            var windy = new WeatherSnapshot
            {
                TemperatureC = 14.0,
                WeatherCode = 3,
                IsDay = true,
                WindSpeedKmh = 35.0,
                WindGustsKmh = 62.0
            };
            WeatherAlertInfo windAlert = WeatherService.EvaluateWeatherAlert(windy, TemperatureUnit.Celsius, baseNow);
            AssertEqual(WeatherAlertLevel.Yellow, windAlert.Level, "Raffiche 62 km/h -> Allerta Gialla");
            AssertEqual("ALLERTA VENTO", windAlert.Title, "Titolo allerta vento");

            // 4. Raffiche estreme 82 km/h -> Red (RAFFICHE FORTI)
            var extremeWind = new WeatherSnapshot
            {
                TemperatureC = 14.0,
                WeatherCode = 3,
                IsDay = true,
                WindSpeedKmh = 52.0,
                WindGustsKmh = 82.0
            };
            WeatherAlertInfo extremeWindAlert = WeatherService.EvaluateWeatherAlert(extremeWind, TemperatureUnit.Celsius, baseNow);
            AssertEqual(WeatherAlertLevel.Red, extremeWindAlert.Level, "Raffiche 82 km/h -> Allerta Rossa");
            AssertEqual("RAFFICHE FORTI", extremeWindAlert.Title, "Titolo raffiche forti");

            // 5. Temporale WMO 95 -> Red (TEMPORALE)
            var storm = new WeatherSnapshot { TemperatureC = 16.0, WeatherCode = 95, IsDay = true };
            WeatherAlertInfo stormAlert = WeatherService.EvaluateWeatherAlert(storm, TemperatureUnit.Celsius, baseNow);
            AssertEqual(WeatherAlertLevel.Red, stormAlert.Level, "WMO 95 -> Allerta Rossa");
            AssertEqual("TEMPORALE", stormAlert.Title, "Titolo temporale");

            // 6. Parsing wind_gusts_10m + persistenza EnableWeatherAlerts
            string url = WeatherService.BuildForecastUrl(45.4642, 9.19);
            AssertTrue(url.Contains("wind_gusts_10m", StringComparison.Ordinal), "URL Open-Meteo deve includere wind_gusts_10m");

            const string gustJson = """
            {
              "current": { "temperature_2m": 12.0, "apparent_temperature": 10.0, "weather_code": 3, "is_day": 1, "wind_speed_10m": 32.0, "wind_gusts_10m": 64.5, "relative_humidity_2m": 70, "uv_index": 2.0 },
              "hourly": {
                "time": ["2026-11-20T13:00"],
                "temperature_2m": [12.0],
                "weather_code": [3],
                "precipitation_probability": [20],
                "wind_speed_10m": [34.0],
                "wind_gusts_10m": [68.2],
                "is_day": [1]
              },
              "daily": {
                "time": ["2026-11-20"],
                "weather_code": [3],
                "temperature_2m_max": [14.0],
                "temperature_2m_min": [6.0],
                "precipitation_probability_max": [20],
                "wind_speed_10m_max": [34.0],
                "wind_gusts_10m_max": [70.0]
              }
            }
            """;
            WeatherSnapshot parsed = WeatherService.ParseForecastJson(gustJson, "Milano", baseNow);
            AssertTrue(Math.Abs(parsed.WindGustsKmh - 64.5) < 0.01, "Raffiche correnti estratte");
            AssertTrue(Math.Abs(parsed.NextHours[0].WindGustsKmh - 68.2) < 0.01, "Raffiche orarie estratte");
            AssertTrue(Math.Abs(parsed.Daily[0].WindGustsMaxKmh - 70.0) < 0.01, "Raffiche max daily estratte");

            string tempDir = Path.Combine(Path.GetTempPath(), "DskWeatherTests_Alert_" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SettingsService(tempDir);
                var cfg = new WidgetSettings { EnableWeatherAlerts = false };
                svc.Save(cfg);
                WidgetSettings reloaded = svc.Load();
                AssertEqual(false, reloaded.EnableWeatherAlerts, "Persistenza EnableWeatherAlerts = false");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestDefaultStyleCheckAndReset()
        {
            var cfg = new WidgetSettings();
            cfg.AddOrSelectLocation("Serramanna", "Italia", 39.42335, 8.92243);
            cfg.EnableWeatherAlerts = false;
            AssertEqual(true, cfg.IsDefaultStyle(), "Configurazione iniziale deve risultare in stile predefinito");

            cfg.LayoutStyle = WidgetLayoutStyle.MaterialCompact;
            cfg.ThemeMode = WidgetThemeMode.MaterialLight;
            cfg.BackgroundOpacity = 0.45;
            cfg.Scale = 1.25;
            cfg.EnableWeatherAnimations = false;
            AssertEqual(false, cfg.IsDefaultStyle(), "Dopo modifica stile IsDefaultStyle deve essere false");

            cfg.ResetStyleToDefaults();
            AssertEqual(true, cfg.IsDefaultStyle(), "Dopo ResetStyleToDefaults deve tornare in stile predefinito");
            AssertEqual("Serramanna", cfg.CityName, "ResetStyleToDefaults non deve toccare la città");
            AssertEqual(false, cfg.EnableWeatherAlerts, "ResetStyleToDefaults non deve toccare EnableWeatherAlerts");
        }

        private static void TestHourlyStripAcrossAllLayouts()
        {
            if (System.Windows.Application.Current is null)
            {
                var app = new Dsk.Weather.App();
                app.InitializeComponent();
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "DskWeatherTests_HourlyLayouts_" + Guid.NewGuid().ToString("N"));
            Dsk.Weather.Windows.WidgetWindow? window = null;
            try
            {
                var settingsService = new SettingsService(tempDir);
                var settings = new WidgetSettings
                {
                    ShowHourlyStrip = true,
                    PinMode = WindowPinMode.BottomMost,
                    LayoutStyle = WidgetLayoutStyle.PixelCard
                };
                settings.AddOrSelectLocation("Serramanna", "Italia", 39.42335, 8.92243);

                window = new Dsk.Weather.Windows.WidgetWindow(
                    settings,
                    settingsService,
                    new WeatherService(),
                    new MaterialColorService(),
                    new DesktopPinService(),
                    new AutostartService(),
                    () => { },
                    () => { });

                AssertTrue(
                    !ReferenceEquals(window.CardHourlyContainer.Parent, window.LayoutPixelCard),
                    "CardHourlyContainer non deve essere figlio esclusivo di LayoutPixelCard");
                AssertTrue(
                    ReferenceEquals(window.CardHourlyContainer.Parent, window.CardLayoutsHost),
                    "CardHourlyContainer deve appartenere al contenitore condiviso CardLayoutsHost");

                WidgetSettings next = settings.Clone();
                next.LayoutStyle = WidgetLayoutStyle.PixelCard;
                next.ShowHourlyStrip = true;
                window.ApplySettings(next, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Visible, window.LayoutPixelCard.Visibility, "LayoutPixelCard visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.CardHourlyContainer.Visibility, "Striscia 24h visibile su Card Completa");
                AssertEqual(0.0, window.HourlyScrollIndicator.Opacity, "Indicatore scorrimento invisibile a riposo");
                AssertTrue(window.CardHourlyContainer.Effect is null, "Nessuna ombra testo su Card Completa");
                AssertEqual(10.0, window.CardHourlyContainer.Margin.Top, "Margine superiore striscia su Card Completa");

                next = settings.Clone();
                next.LayoutStyle = WidgetLayoutStyle.PixelMinimalTransparent;
                next.ShowHourlyStrip = true;
                window.ApplySettings(next, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Visible, window.LayoutPixelMinimal.Visibility, "LayoutPixelMinimal visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.CardHourlyContainer.Visibility, "Striscia 24h visibile su Minimal Trasparente");
                AssertTrue(window.CardHourlyContainer.Effect is not null, "MinimalTextShadow applicato alla striscia 24h in Minimal Trasparente");

                next = settings.Clone();
                next.LayoutStyle = WidgetLayoutStyle.MaterialCompact;
                next.ShowHourlyStrip = true;
                window.ApplySettings(next, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Visible, window.LayoutMaterialCompact.Visibility, "LayoutMaterialCompact visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.CardHourlyContainer.Visibility, "Striscia 24h visibile su Material Compatto");
                AssertTrue(window.CardHourlyContainer.Effect is null, "Nessuna ombra testo minimal su Material Compatto");
                AssertEqual(8.0, window.CardHourlyContainer.Margin.Top, "Margine superiore raccolto striscia su Material Compatto");
                AssertEqual(6.0, window.CardHourlyContainer.Padding.Top, "Padding superiore raccolto striscia su Material Compatto");

                next = settings.Clone();
                next.LayoutStyle = WidgetLayoutStyle.PixelMinimalTransparent;
                next.ShowHourlyStrip = false;
                window.ApplySettings(next, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Collapsed, window.CardHourlyContainer.Visibility, "Striscia 24h nascosta su Minimal se ShowHourlyStrip = false");

                next = settings.Clone();
                next.LayoutStyle = WidgetLayoutStyle.MaterialCompact;
                next.ShowHourlyStrip = false;
                window.ApplySettings(next, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Collapsed, window.CardHourlyContainer.Visibility, "Striscia 24h nascosta su Material Compatto se ShowHourlyStrip = false");
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestForecastDrawerDynamicAtmosphere()
        {
            if (System.Windows.Application.Current is null)
            {
                var app = new Dsk.Weather.App();
                app.InitializeComponent();
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "DskWeatherTests_DrawerAtmosphere_" + Guid.NewGuid().ToString("N"));
            Dsk.Weather.Windows.WidgetWindow? window = null;
            try
            {
                var settingsService = new SettingsService(tempDir);
                var settings = new WidgetSettings
                {
                    EnableDynamicAtmosphere = true,
                    PinMode = WindowPinMode.BottomMost,
                    LayoutStyle = WidgetLayoutStyle.PixelCard
                };
                settings.AddOrSelectLocation("Serramanna", "Italia", 39.42335, 8.92243);

                window = new Dsk.Weather.Windows.WidgetWindow(
                    settings,
                    settingsService,
                    new WeatherService(),
                    new MaterialColorService(),
                    new DesktopPinService(),
                    new AutostartService(),
                    () => { },
                    () => { });

                window.ApplySettings(settings, refreshWeather: false);
                AssertTrue(
                    window.ForecastDrawer.Background is LinearGradientBrush,
                    "ForecastDrawer deve usare LinearGradientBrush quando EnableDynamicAtmosphere = true su Card Completa");

                WidgetSettings minimal = settings.Clone();
                minimal.LayoutStyle = WidgetLayoutStyle.PixelMinimalTransparent;
                window.ApplySettings(minimal, refreshWeather: false);
                AssertTrue(
                    window.ForecastDrawer.Background is LinearGradientBrush,
                    "ForecastDrawer deve usare LinearGradientBrush anche con layout Minimal Trasparente");

                WidgetSettings disabled = minimal.Clone();
                disabled.EnableDynamicAtmosphere = false;
                window.ApplySettings(disabled, refreshWeather: false);
                AssertTrue(
                    window.ForecastDrawer.Background is SolidColorBrush,
                    "ForecastDrawer deve tornare a SolidColorBrush quando EnableDynamicAtmosphere = false");
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestMultiLanguageLocalizationAndSettings()
        {
            AssertEqual("it", LocalizationService.NormalizeLanguage("invalid"), "Fallback lingua sconosciuta su it");
            AssertEqual("nl", LocalizationService.NormalizeLanguage("nl-NL"), "Normalizzazione nl-NL su nl");

            foreach (string lang in LocalizationService.SupportedLanguages)
            {
                string wmo = WeatherService.DescribeWmoCodeItalian(0, lang);
                AssertTrue(!string.IsNullOrWhiteSpace(wmo), $"WMO 0 non vuoto per {lang}");
            }

            AssertEqual("Clear", WeatherService.DescribeWmoCodeItalian(0, "en"), "WMO 0 EN");
            AssertEqual("Klar", WeatherService.DescribeWmoCodeItalian(0, "de"), "WMO 0 DE");
            AssertEqual("Helder", WeatherService.DescribeWmoCodeItalian(0, "nl"), "WMO 0 NL");
            AssertEqual("Despejado", WeatherService.DescribeWmoCodeItalian(0, "es"), "WMO 0 ES");
            AssertEqual("Limpo", WeatherService.DescribeWmoCodeItalian(0, "pt"), "WMO 0 PT");

            AssertEqual("SW", WeatherService.DescribeWindDirectionCardinal(225, "en"), "Cardinale SW in EN");
            AssertEqual("SO", WeatherService.DescribeWindDirectionCardinal(225, "es"), "Cardinale SO in ES");

            string tempDir = Path.Combine(Path.GetTempPath(), "DskWeatherTests_Lang_" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SettingsService(tempDir);
                var cfg = new WidgetSettings { Language = "de" };
                svc.Save(cfg);
                WidgetSettings loaded = svc.Load();
                AssertEqual("de", loaded.Language, "Lingua persistita");

                loaded.ResetStyleToDefaults();
                AssertEqual("de", loaded.Language, "ResetStyleToDefaults non deve resettare la lingua");

                if (System.Windows.Application.Current is null)
                {
                    var app = new Dsk.Weather.App();
                    app.InitializeComponent();
                }

                var win = new Dsk.Weather.Windows.SettingsWindow(
                    loaded,
                    new WeatherService(),
                    new AutostartService(),
                    _ => { },
                    () => { });
                try
                {
                    AssertEqual("Einstellungen", win.HeaderTitleText.Text, "Titolo SettingsWindow in tedesco");
                    win.ApplyLanguage("en");
                    AssertEqual("Settings", win.HeaderTitleText.Text, "Titolo SettingsWindow in inglese");
                }
                finally
                {
                    win.Close();
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void TestHourlyStripSunsetAndSunriseSlots()
        {
            var baseDay = new DateTime(2026, 11, 20, 15, 0, 0);
            var snapshot = new WeatherSnapshot
            {
                SunriseLocal = new DateTime(2026, 11, 20, 8, 8, 0),
                SunsetLocal = new DateTime(2026, 11, 20, 16, 30, 0),
                NextHours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = new DateTime(2026, 11, 20, 15, 0, 0), TemperatureC = 10.0 },
                    new() { LocalTime = new DateTime(2026, 11, 20, 16, 0, 0), TemperatureC = 9.0 },
                    new() { LocalTime = new DateTime(2026, 11, 20, 17, 0, 0), TemperatureC = 7.0 },
                    new() { LocalTime = new DateTime(2026, 11, 21, 8, 0, 0), TemperatureC = 4.0 },
                    new() { LocalTime = new DateTime(2026, 11, 21, 9, 0, 0), TemperatureC = 6.0 }
                },
                Daily = new List<DailyForecastPoint>
                {
                    new()
                    {
                        Date = new DateTime(2026, 11, 20),
                        SunriseLocal = new DateTime(2026, 11, 20, 8, 8, 0),
                        SunsetLocal = new DateTime(2026, 11, 20, 16, 30, 0)
                    },
                    new()
                    {
                        Date = new DateTime(2026, 11, 21),
                        SunriseLocal = new DateTime(2026, 11, 21, 8, 10, 0),
                        SunsetLocal = new DateTime(2026, 11, 21, 16, 29, 0)
                    }
                }
            };

            List<Dsk.Weather.Windows.WidgetWindow.HourlyStripSlot> slots =
                Dsk.Weather.Windows.WidgetWindow.BuildHourlyStripSlots(snapshot);

            AssertEqual(7, slots.Count, "5 ore + Tramonto oggi + Alba domani");
            AssertEqual(Dsk.Weather.Windows.WidgetWindow.HourlySlotKind.Forecast, slots[1].Kind, "Slot 1 deve essere 16:00");
            AssertEqual(Dsk.Weather.Windows.WidgetWindow.HourlySlotKind.Sunset, slots[2].Kind, "Slot 2 deve essere Tramonto 16:30");
            AssertEqual(16, slots[2].LocalTime.Hour, "Ora Tramonto");
            AssertEqual(30, slots[2].LocalTime.Minute, "Minuto Tramonto");
            AssertTrue(Math.Abs(slots[2].TemperatureC - 8.0) < 0.01, "Temperatura interpolata Tramonto 16:30 deve essere 8.0°C");

            AssertEqual(Dsk.Weather.Windows.WidgetWindow.HourlySlotKind.Sunrise, slots[5].Kind, "Slot 5 deve essere Alba 08:10");
            AssertEqual(8, slots[5].LocalTime.Hour, "Ora Alba");
            AssertEqual(10, slots[5].LocalTime.Minute, "Minuto Alba");
            AssertTrue(baseDay < slots[2].LocalTime, "Tramonto successivo alle 15:00");
        }

        private static void TestOfficialAlertsParsingAndMatching()
        {
            var baseNow = new DateTime(2026, 11, 20, 12, 0, 0);
            DateTimeOffset refLocal = new DateTimeOffset(baseNow, TimeZoneInfo.Local.GetUtcOffset(baseNow));
            string onsetActive = refLocal.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
            string expiresFuture = refLocal.AddHours(6).ToString("O", CultureInfo.InvariantCulture);
            string expiresPast = refLocal.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);

            string sampleAtom = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:cap="urn:oasis:names:tc:emergency:cap:1.2">
              <entry>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT001</value></cap:geocode>
                <cap:areaDesc>Calabria</cap:areaDesc>
                <cap:event>Red Rain Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:areaDesc>Sardegna</cap:areaDesc>
                <cap:event>Yellow Wind Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Moderate</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:areaDesc>Sardegna</cap:areaDesc>
                <cap:event>Orange Coastal Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Severe</cap:severity>
                <cap:status>Actual</cap:status>
                <link type="application/cap+xml" href="https://feeds.meteoalarm.org/api/v1/warnings/sample-cap" />
              </entry>
              <entry>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:areaDesc>Sardegna</cap:areaDesc>
                <cap:event>Red Thunderstorm Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresPast}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
            </feed>
            """;

            var sardegna = new OfficialAlertService.GeoRegionInfo(
                CountryCode: "it",
                IsoLvl4: "IT-88",
                IsoLvl6: "IT-VS",
                State: "Sardigna/Sardegna",
                County: "Medio Campidano",
                Province: string.Empty,
                Municipality: string.Empty,
                City: "Serramanna");

            OfficialAlertService.MeteoAlarmMatch? match =
                OfficialAlertService.ParseMeteoAlarmAtom(sampleAtom, sardegna, baseNow);
            AssertTrue(match is not null, "Match Sardegna IT019 deve essere trovato");
            AssertEqual(WeatherAlertLevel.Orange, match!.Level, "Allerta arancione non scaduta deve vincere su gialla");
            AssertEqual("IT019", match.GeocodeId, "Geocode IT019");

            string sampleCap = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
              <info>
                <language>en-GB</language>
                <event>Orange Wind Warning</event>
                <onset>{onsetActive}</onset>
                <expires>{expiresFuture}</expires>
                <senderName>Italian Air Force National Meteorological Service</senderName>
                <description>Strong winds on coasts (DISCLAIMER: test disclaimer omitted)</description>
                <parameter><valueName>awareness_type</valueName><value>1; Wind</value></parameter>
                <area><areaDesc>Sardegna</areaDesc></area>
              </info>
              <info>
                <language>it-IT</language>
                <event>Allerta Arancione Vento</event>
                <onset>{onsetActive}</onset>
                <expires>{expiresFuture}</expires>
                <senderName>Servizio Meteorologico dell'Aeronautica Militare</senderName>
                <description>Venti forti di maestrale sulla Sardegna. (DISCLAIMER: testo omesso)</description>
                <parameter><valueName>awareness_type</valueName><value>1; Wind</value></parameter>
                <area><areaDesc>Sardegna</areaDesc></area>
              </info>
            </alert>
            """;

            WeatherAlertInfo capIt = OfficialAlertService.BuildAlertFromCap(sampleCap, match, baseNow, "it");
            AssertEqual(true, capIt.IsOfficial, "Flag IsOfficial true");
            AssertEqual("ALLERTA VENTO", capIt.Title, "Titolo CAP vento IT");
            AssertEqual(WeatherAlertLevel.Orange, capIt.Level, "Livello arancione CAP");
            AssertTrue(capIt.SourceName.Contains("Aeronautica Militare", StringComparison.Ordinal), "Sender ufficiale IT estratto");
            AssertTrue(!capIt.Description.Contains("DISCLAIMER", StringComparison.OrdinalIgnoreCase), "Disclaimer rimosso");

            WeatherAlertInfo capEn = OfficialAlertService.BuildAlertFromCap(sampleCap, match, baseNow, "en");
            AssertEqual("WIND ALERT", capEn.Title, "Titolo CAP vento EN");

            string brightSkyJson = $$"""
            {
              "alerts": [
                {
                  "status": "actual",
                  "onset": "{{onsetActive}}",
                  "expires": "{{expiresFuture}}",
                  "severity": "severe",
                  "event_en": "wind gusts",
                  "event_de": "STURMBÖEN",
                  "description_en": "Severe wind gusts up to 90 km/h.",
                  "description_de": "Es treten schwere Sturmböen bis 90 km/h auf."
                }
              ]
            }
            """;
            WeatherAlertInfo? dwdAlert = OfficialAlertService.ParseBrightSkyJson(brightSkyJson, baseNow, "de");
            AssertTrue(dwdAlert is not null && dwdAlert.IsOfficial, "BrightSky DWD ufficiale");
            AssertEqual(WeatherAlertLevel.Orange, dwdAlert!.Level, "DWD severe -> Orange");
            AssertEqual("WINDWARNUNG", dwdAlert.Title, "DWD titolo DE");
            AssertTrue(dwdAlert.Description.Contains("Sturmböen", StringComparison.Ordinal), "DWD descrizione DE");

            string nwsJson = $$"""
            {
              "features": [
                {
                  "properties": {
                    "status": "Actual",
                    "severity": "Extreme",
                    "event": "Tornado Warning",
                    "headline": "Tornado Warning issued for test area",
                    "onset": "{{onsetActive}}",
                    "expires": "{{expiresFuture}}"
                  }
                }
              ]
            }
            """;
            WeatherAlertInfo? nwsAlert = OfficialAlertService.ParseNwsJson(nwsJson, baseNow, "en");
            AssertTrue(nwsAlert is not null && nwsAlert.IsOfficial, "NOAA NWS ufficiale");
            AssertEqual(WeatherAlertLevel.Red, nwsAlert!.Level, "NWS Extreme -> Red");

            // Verifica rete di sicurezza critica: vento moderato 50 km/h NON genera falsi allarmi se feed ufficiale dice 0 allerte
            var moderateWind = new WeatherSnapshot { WeatherCode = 3, WindSpeedKmh = 30.0, WindGustsKmh = 50.0 };
            AssertEqual(
                WeatherAlertLevel.None,
                WeatherService.EvaluateCriticalNowcastOnly(moderateWind, TemperatureUnit.Celsius, baseNow, "it").Level,
                "Nowcast critico deve ignorare vento moderato 50 km/h");

            var liveStorm = new WeatherSnapshot { WeatherCode = 95 };
            AssertEqual(
                WeatherAlertLevel.Red,
                WeatherService.EvaluateCriticalNowcastOnly(liveStorm, TemperatureUnit.Celsius, baseNow, "it").Level,
                "Nowcast critico segnala temporale WMO 95 in corso");
        }

        private static byte[] CreateSolidBgraBuffer(int pixelCount, byte b, byte g, byte r, byte a)
        {
            byte[] buf = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                int o = i * 4;
                buf[o] = b;
                buf[o + 1] = g;
                buf[o + 2] = r;
                buf[o + 3] = a;
            }
            return buf;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException($"{message}: atteso '{expected}', ottenuto '{actual}'");
            }
        }
    }
}
