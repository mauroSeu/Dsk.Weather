using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
                ("OfficialAlertsParsingAndMatching", TestOfficialAlertsParsingAndMatching),
                ("OfficialAlertsGeometryPolygonAndCircle", TestOfficialAlertsGeometryPolygonAndCircle),
                ("OfficialAlertsUpdateCancelReferencesAndCertainty", TestOfficialAlertsUpdateCancelReferencesAndCertainty),
                ("OfficialAlertsSourceStatusAndStaleDegradation", TestOfficialAlertsSourceStatusAndStaleDegradation),
                ("FallbackPersistenceAndNowcastThresholds", TestFallbackPersistenceAndNowcastThresholds),
                ("DpcItalyOfficialAlertsByMunicipality", TestDpcItalyOfficialAlertsByMunicipality),
                ("IpmaPortugalOfficialAlertsParsing", TestIpmaPortugalOfficialAlertsParsing),
                ("MetNorwayOfficialAlertsParsing", TestMetNorwayOfficialAlertsParsing),
                ("HourlyStripSeparateCurvesAndHumidity", TestHourlyStripSeparateCurvesAndHumidity),
                ("LocationAutoDetectionAndCleanSettingsUi", TestLocationAutoDetectionAndCleanSettingsUi),
                ("SettingsWindowExitWidgetAction", TestSettingsWindowExitWidgetAction)
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

        private static void TestOfficialAlertsGeometryPolygonAndCircle()
        {
            List<(double Lat, double Lon)> poly = OfficialAlertService.ParseCapPolygon(
                "39.15,9.00 39.15,9.25 39.32,9.25 39.32,9.00 39.15,9.00");
            AssertEqual(5, poly.Count, "Parsing poligono CAP 5 vertici");
            AssertEqual(true, OfficialAlertService.IsPointInPolygon(39.2238, 9.1217, poly), "Cagliari dentro poligono");
            AssertEqual(true, OfficialAlertService.IsPointInPolygon(39.15, 9.10, poly), "Punto sul bordo dentro poligono");
            AssertEqual(false, OfficialAlertService.IsPointInPolygon(40.7259, 8.5556, poly), "Sassari fuori poligono");

            (double Lat, double Lon, double RadiusKm)? circle = OfficialAlertService.ParseCapCircle("39.2238,9.1217 15");
            AssertTrue(circle.HasValue, "Parsing cerchio CAP valido");
            AssertEqual(true, OfficialAlertService.IsPointInCircle(39.24, 9.15, circle!.Value.Lat, circle.Value.Lon, circle.Value.RadiusKm), "Punto vicino dentro cerchio");
            AssertEqual(false, OfficialAlertService.IsPointInCircle(40.7259, 8.5556, circle.Value.Lat, circle.Value.Lon, circle.Value.RadiusKm), "Sassari fuori cerchio");

            var baseNow = new DateTime(2026, 11, 20, 12, 0, 0);
            DateTimeOffset refLocal = new DateTimeOffset(baseNow, TimeZoneInfo.Local.GetUtcOffset(baseNow));
            string onsetActive = refLocal.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
            string expiresFuture = refLocal.AddHours(6).ToString("O", CultureInfo.InvariantCulture);

            string polyAtom = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:cap="urn:oasis:names:tc:emergency:cap:1.2">
              <entry>
                <cap:identifier>IT-POLY-1</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:areaDesc>Sardegna Meridionale</cap:areaDesc>
                <cap:event>Orange Rain Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Severe</cap:severity>
                <cap:status>Actual</cap:status>
                <cap:polygon>39.15,9.00 39.15,9.25 39.32,9.25 39.32,9.00 39.15,9.00</cap:polygon>
              </entry>
            </feed>
            """;

            var sardegna = new OfficialAlertService.GeoRegionInfo(
                "it", "IT-88", string.Empty, "Sardegna", string.Empty, string.Empty, string.Empty, string.Empty);

            OfficialAlertService.MeteoAlarmMatch? cagliariMatch =
                OfficialAlertService.ParseMeteoAlarmAtom(polyAtom, sardegna, baseNow, 39.2238, 9.1217);
            AssertTrue(cagliariMatch is not null && cagliariMatch.Level == WeatherAlertLevel.Orange, "Atom con poligono deve accettare Cagliari");

            OfficialAlertService.MeteoAlarmMatch? sassariMatch =
                OfficialAlertService.ParseMeteoAlarmAtom(polyAtom, sardegna, baseNow, 40.7259, 8.5556);
            AssertTrue(sassariMatch is null, "Atom con poligono deve escludere Sassari anche se in IT019");

            var genericMatch = new OfficialAlertService.MeteoAlarmMatch(
                WeatherAlertLevel.Orange,
                "Orange Rain Warning",
                "Sardegna",
                "IT019",
                null,
                refLocal.AddHours(-1),
                refLocal.AddHours(6));

            string capWithPolygon = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <alert xmlns="urn:oasis:names:tc:emergency:cap:1.2">
              <status>Actual</status>
              <msgType>Alert</msgType>
              <info>
                <language>it-IT</language>
                <event>Pioggia intensa</event>
                <onset>{onsetActive}</onset>
                <expires>{expiresFuture}</expires>
                <certainty>Observed</certainty>
                <senderName>Aeronautica Militare</senderName>
                <area>
                  <areaDesc>Cagliari</areaDesc>
                  <polygon>39.15,9.00 39.15,9.25 39.32,9.25 39.32,9.00 39.15,9.00</polygon>
                </area>
              </info>
            </alert>
            """;

            WeatherAlertInfo? capInside = OfficialAlertService.TryBuildAlertFromCap(capWithPolygon, genericMatch, baseNow, "it", 39.2238, 9.1217);
            WeatherAlertInfo? capOutside = OfficialAlertService.TryBuildAlertFromCap(capWithPolygon, genericMatch, baseNow, "it", 40.7259, 8.5556);
            AssertTrue(capInside is not null && capInside.Certainty == "Observed", "CAP con poligono deve accettare il punto interno");
            AssertTrue(capOutside is null, "CAP con poligono deve escludere il punto esterno");

            string capWithoutGeometry = capWithPolygon.Replace("<polygon>39.15,9.00 39.15,9.25 39.32,9.25 39.32,9.00 39.15,9.00</polygon>", string.Empty);
            WeatherAlertInfo? capAdminFallback = OfficialAlertService.TryBuildAlertFromCap(capWithoutGeometry, genericMatch, baseNow, "it", 40.7259, 8.5556);
            AssertTrue(capAdminFallback is not null, "CAP senza geometria mantiene fallback amministrativo");
        }

        private static void TestOfficialAlertsUpdateCancelReferencesAndCertainty()
        {
            var baseNow = new DateTime(2026, 11, 20, 12, 0, 0);
            DateTimeOffset refLocal = new DateTimeOffset(baseNow, TimeZoneInfo.Local.GetUtcOffset(baseNow));
            string onsetActive = refLocal.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
            string expiresFuture = refLocal.AddHours(6).ToString("O", CultureInfo.InvariantCulture);
            string expiresPast = refLocal.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
            string onsetFarFuture = refLocal.AddHours(36).ToString("O", CultureInfo.InvariantCulture);

            string lifecycleAtom = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:cap="urn:oasis:names:tc:emergency:cap:1.2">
              <entry>
                <cap:identifier>IT-001</cap:identifier>
                <cap:message_type>Alert</cap:message_type>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Red Thunderstorm Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-001-CANCEL</cap:identifier>
                <cap:message_type>Cancel</cap:message_type>
                <cap:references>servizio@it,IT-001,2026-11-20T10:00:00Z</cap:references>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Cancel Warning</cap:event>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-002</cap:identifier>
                <cap:message_type>Alert</cap:message_type>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Orange Wind Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Severe</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-003</cap:identifier>
                <cap:message_type>Update</cap:message_type>
                <cap:references>servizio@it,IT-002,2026-11-20T10:30:00Z</cap:references>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Yellow Wind Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Moderate</cap:severity>
                <cap:certainty>Observed</cap:certainty>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-TEST</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Red Exercise</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Test</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-FUTURE</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Red Future</cap:event>
                <cap:onset>{onsetFarFuture}</cap:onset>
                <cap:expires>{refLocal.AddHours(42):O}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-EXPIRED</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Red Expired</cap:event>
                <cap:onset>{refLocal.AddHours(-6):O}</cap:onset>
                <cap:expires>{expiresPast}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-UNLIKELY</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Red Unlikely</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Extreme</cap:severity>
                <cap:certainty>Unlikely</cap:certainty>
                <cap:status>Actual</cap:status>
              </entry>
            </feed>
            """;

            var sardegna = new OfficialAlertService.GeoRegionInfo(
                "it", "IT-88", string.Empty, "Sardegna", string.Empty, string.Empty, string.Empty, string.Empty);

            List<OfficialAlertService.MeteoAlarmMatch> candidates =
                OfficialAlertService.ParseMeteoAlarmCandidates(lifecycleAtom, sardegna, baseNow);
            AssertEqual(1, candidates.Count, "Solo IT-003 (Update) deve restare attivo dopo Cancel/Update/Test/Expires/Future/Unlikely");
            AssertEqual("IT-003", candidates[0].Identifier, "Identificatore superstite IT-003");
            AssertEqual(WeatherAlertLevel.Yellow, candidates[0].Level, "Livello aggiornato a Yellow da Update");
            AssertEqual("Observed", candidates[0].Certainty, "Certainty Observed preservata");

            string certaintyTieAtom = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:cap="urn:oasis:names:tc:emergency:cap:1.2">
              <entry>
                <cap:identifier>IT-POS</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Yellow Rain Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Moderate</cap:severity>
                <cap:certainty>Possible</cap:certainty>
                <cap:status>Actual</cap:status>
              </entry>
              <entry>
                <cap:identifier>IT-OBS</cap:identifier>
                <cap:geocode><valueName>EMMA_ID</valueName><value>IT019</value></cap:geocode>
                <cap:event>Yellow Wind Warning</cap:event>
                <cap:onset>{onsetActive}</cap:onset>
                <cap:expires>{expiresFuture}</cap:expires>
                <cap:severity>Moderate</cap:severity>
                <cap:certainty>Observed</cap:certainty>
                <cap:status>Actual</cap:status>
              </entry>
            </feed>
            """;
            OfficialAlertService.MeteoAlarmMatch? tieWinner =
                OfficialAlertService.ParseMeteoAlarmAtom(certaintyTieAtom, sardegna, baseNow);
            AssertTrue(tieWinner is not null && tieWinner.Identifier == "IT-OBS", "A parità di severità Observed deve vincere su Possible");
        }

        private static void TestOfficialAlertsSourceStatusAndStaleDegradation()
        {
            OfficialAlertService.ClearCachesForTests();
            try
            {
                DateTimeOffset t0 = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
                int fetchCount = 0;

                OfficialAlertService.PayloadFetchOutcome live = OfficialAlertService.FetchPayloadWithCacheAsync(
                    "test:feed",
                    "https://example.invalid/feed",
                    t0,
                    (_, _) =>
                    {
                        fetchCount++;
                        return Task.FromResult<string?>("FEED_BODY_V1");
                    },
                    CancellationToken.None).GetAwaiter().GetResult();

                AssertEqual(AlertSourceStatus.Live, live.Status, "Primo fetch riuscito -> Live");
                AssertEqual("FEED_BODY_V1", live.Body, "Body V1");
                AssertEqual((DateTimeOffset?)t0, live.LastSuccessfulFetchUtc, "LastSuccessfulFetchUtc = t0");
                AssertEqual(1, fetchCount, "1 chiamata effettuata");

                OfficialAlertService.PayloadFetchOutcome cached = OfficialAlertService.FetchPayloadWithCacheAsync(
                    "test:feed",
                    "https://example.invalid/feed",
                    t0.AddMinutes(5),
                    (_, _) => throw new InvalidOperationException("Non deve interrogare la rete entro TTL"),
                    CancellationToken.None).GetAwaiter().GetResult();

                AssertEqual(AlertSourceStatus.Live, cached.Status, "Cache fresca -> Live");
                AssertEqual("FEED_BODY_V1", cached.Body, "Body da cache fresca");

                OfficialAlertService.PayloadFetchOutcome stale = OfficialAlertService.FetchPayloadWithCacheAsync(
                    "test:feed",
                    "https://example.invalid/feed",
                    t0.AddMinutes(20),
                    (_, _) => Task.FromResult<string?>(null),
                    CancellationToken.None).GetAwaiter().GetResult();

                AssertEqual(AlertSourceStatus.StaleCache, stale.Status, "Errore rete entro 60 min -> StaleCache");
                AssertEqual("FEED_BODY_V1", stale.Body, "Body preservato in StaleCache");
                AssertEqual((DateTimeOffset?)t0, stale.LastSuccessfulFetchUtc, "Timestamp ultimo fetch preservato in StaleCache");

                OfficialAlertService.PayloadFetchOutcome degraded = OfficialAlertService.FetchPayloadWithCacheAsync(
                    "test:feed",
                    "https://example.invalid/feed",
                    t0.AddMinutes(75),
                    (_, _) => Task.FromResult<string?>(null),
                    CancellationToken.None).GetAwaiter().GetResult();

                AssertEqual(AlertSourceStatus.Fallback, degraded.Status, "Errore rete oltre 60 min -> Fallback esplicito (non Live)");
                AssertTrue(degraded.Body is null, "Nessun body oltre la grace period");
                AssertEqual((DateTimeOffset?)t0, degraded.LastSuccessfulFetchUtc, "LastSuccessfulFetchUtc tracciato anche su Fallback");
            }
            finally
            {
                OfficialAlertService.ClearCachesForTests();
            }
        }

        private static void TestFallbackPersistenceAndNowcastThresholds()
        {
            var baseNow = new DateTime(2026, 11, 20, 12, 0, 0);

            // 1. Singolo picco orario isolato -> NON deve generare allerta gialla
            var singleGustSpike = new WeatherSnapshot
            {
                TemperatureC = 14.0,
                WeatherCode = 3,
                WindSpeedKmh = 20.0,
                WindGustsKmh = 30.0,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(1), TemperatureC = 14.0, WeatherCode = 3, WindGustsKmh = 32.0 },
                    new() { LocalTime = baseNow.AddHours(2), TemperatureC = 13.0, WeatherCode = 3, WindGustsKmh = 62.0 },
                    new() { LocalTime = baseNow.AddHours(3), TemperatureC = 13.0, WeatherCode = 3, WindGustsKmh = 28.0 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.None,
                WeatherService.EvaluateWeatherAlert(singleGustSpike, TemperatureUnit.Celsius, baseNow).Level,
                "Picco isolato di 1h a 62 km/h deve essere ignorato");

            var drySingleFreeze = new WeatherSnapshot
            {
                TemperatureC = 2.0,
                WeatherCode = 0,
                RelativeHumidity = 45,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(2), TemperatureC = -1.0, WeatherCode = 0, PrecipProbability = 0 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.None,
                WeatherService.EvaluateWeatherAlert(drySingleFreeze, TemperatureUnit.Celsius, baseNow).Level,
                "1h isolata secca a -1°C senza umidità/calo brusco non attiva falsi allarmi");

            // 2. Persistenza >= 2 ore consecutive -> Allerta Gialla
            var persistentGust = new WeatherSnapshot
            {
                TemperatureC = 14.0,
                WeatherCode = 3,
                WindSpeedKmh = 20.0,
                WindGustsKmh = 30.0,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(1), TemperatureC = 14.0, WeatherCode = 3, WindGustsKmh = 62.0 },
                    new() { LocalTime = baseNow.AddHours(2), TemperatureC = 13.0, WeatherCode = 3, WindGustsKmh = 64.0 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.Yellow,
                WeatherService.EvaluateWeatherAlert(persistentGust, TemperatureUnit.Celsius, baseNow).Level,
                "Raffiche >= 60 km/h per 2h consecutive -> Yellow");

            var persistentRain = new WeatherSnapshot
            {
                TemperatureC = 12.0,
                WeatherCode = 3,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(1), TemperatureC = 11.0, WeatherCode = 61, PrecipitationMm = 6.2 },
                    new() { LocalTime = baseNow.AddHours(2), TemperatureC = 11.0, WeatherCode = 61, PrecipitationMm = 5.8 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.Yellow,
                WeatherService.EvaluateWeatherAlert(persistentRain, TemperatureUnit.Celsius, baseNow).Level,
                "Pioggia >= 5 mm/h per 2h consecutive -> Yellow");

            var persistentWetFrost = new WeatherSnapshot
            {
                TemperatureC = 2.0,
                WeatherCode = 3,
                RelativeHumidity = 85,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(1), TemperatureC = -1.0, WeatherCode = 3, PrecipProbability = 60 },
                    new() { LocalTime = baseNow.AddHours(2), TemperatureC = -1.5, WeatherCode = 3, PrecipProbability = 55 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.Yellow,
                WeatherService.EvaluateWeatherAlert(persistentWetFrost, TemperatureUnit.Celsius, baseNow).Level,
                "Gelo umido per 2h consecutive -> Yellow");

            // 3. EvaluateCriticalNowcastOnly: intensità + persistenza
            var singleFutureCriticalGust = new WeatherSnapshot
            {
                WeatherCode = 3,
                WindGustsKmh = 25.0,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(1), WindGustsKmh = 76.0 },
                    new() { LocalTime = baseNow.AddHours(2), WindGustsKmh = 40.0 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.None,
                WeatherService.EvaluateCriticalNowcastOnly(singleFutureCriticalGust, TemperatureUnit.Celsius, baseNow).Level,
                "Nowcast critico ignora singola ora futura isolata a 76 km/h");

            var twoHoursCriticalGust = new WeatherSnapshot
            {
                WeatherCode = 3,
                WindGustsKmh = 40.0,
                Next12Hours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = baseNow.AddHours(1), WindGustsKmh = 78.0 },
                    new() { LocalTime = baseNow.AddHours(2), WindGustsKmh = 80.0 }
                }
            };
            AssertEqual(
                WeatherAlertLevel.Red,
                WeatherService.EvaluateCriticalNowcastOnly(twoHoursCriticalGust, TemperatureUnit.Celsius, baseNow).Level,
                "Nowcast critico segnala Red per raffiche >= 75 km/h su 2h consecutive imminenti");

            var severeNowGust = new WeatherSnapshot { WeatherCode = 3, WindGustsKmh = 92.0 };
            AssertEqual(
                WeatherAlertLevel.Red,
                WeatherService.EvaluateCriticalNowcastOnly(severeNowGust, TemperatureUnit.Celsius, baseNow).Level,
                "Burrasca severa corrente 92 km/h -> Red immediata");

            var rainOnFreeze = new WeatherSnapshot { WeatherCode = 61, TemperatureC = -1.0, PrecipitationMm = 2.0 };
            AssertEqual(
                WeatherAlertLevel.Red,
                WeatherService.EvaluateCriticalNowcastOnly(rainOnFreeze, TemperatureUnit.Celsius, baseNow).Level,
                "Pioggia su temperatura <= 0°C -> Red");

            var extremeRainNow = new WeatherSnapshot { WeatherCode = 63, TemperatureC = 14.0, PrecipitationMm = 16.0 };
            AssertEqual(
                WeatherAlertLevel.Red,
                WeatherService.EvaluateCriticalNowcastOnly(extremeRainNow, TemperatureUnit.Celsius, baseNow).Level,
                "Pioggia >= 15 mm/h -> Red");

            // 4. URL + parsing precipitation mm/h
            string url = WeatherService.BuildForecastUrl(45.4642, 9.19);
            AssertTrue(url.Contains(",precipitation", StringComparison.Ordinal), "URL Open-Meteo deve richiedere precipitation");
            const string precipJson = """
            {
              "current": { "temperature_2m": 10.0, "apparent_temperature": 9.0, "weather_code": 63, "is_day": 1, "wind_speed_10m": 20.0, "wind_gusts_10m": 35.0, "relative_humidity_2m": 88, "uv_index": 1.0, "precipitation": 6.4 },
              "hourly": {
                "time": ["2026-11-20T13:00"],
                "temperature_2m": [10.0],
                "weather_code": [63],
                "precipitation_probability": [90],
                "precipitation": [7.8],
                "wind_speed_10m": [22.0],
                "wind_gusts_10m": [38.0],
                "is_day": [1]
              },
              "daily": { "time": ["2026-11-20"], "weather_code": [63], "temperature_2m_max": [12.0], "temperature_2m_min": [7.0], "precipitation_probability_max": [90], "wind_speed_10m_max": [22.0] }
            }
            """;
            WeatherSnapshot parsed = WeatherService.ParseForecastJson(precipJson, "Milano", baseNow);
            AssertTrue(Math.Abs(parsed.PrecipitationMm - 6.4) < 0.01, "PrecipitationMm corrente estratto");
            AssertTrue(Math.Abs(parsed.NextHours[0].PrecipitationMm - 7.8) < 0.01, "PrecipitationMm orario estratto");
        }

        private static void TestDpcItalyOfficialAlertsByMunicipality()
        {
            AssertEqual(false, OfficialAlertService.UsesMeteoAlarm("it"), "Italia non deve usare MeteoAlarm");

            const string pageDataSnippet = """
            {"result":{"data":{"url":"https://raw.githubusercontent.com/pcm-dpc/DPC-Bollettini-Criticita-Idrogeologica-Idraulica/master/files/all/20261120_1342_all.zip"}}}
            """;
            AssertEqual("20261120_1342", OfficialAlertService.ExtractDpcBulletinId(pageDataSnippet), "Estrazione ID bollettino DPC");

            const string dpcTopoJson = """
            {
              "type": "Topology",
              "objects": {
                "zone_allerta": {
                  "type": "GeometryCollection",
                  "geometries": [
                    {
                      "type": "Polygon",
                      "properties": {
                        "Nome zona": "Campidano",
                        "Rappresentata nella mappa": "Assenza di fenomeni significativi prevedibili / NESSUNA ALLERTA",
                        "Per rischio idraulico": "Assenza di fenomeni significativi prevedibili / NESSUNA ALLERTA",
                        "Per rischio temporali": "Assenza di fenomeni significativi prevedibili / NESSUNA ALLERTA",
                        "Per rischio idrogeologico": "Assenza di fenomeni significativi prevedibili / NESSUNA ALLERTA",
                        "Comuni": ["Cagliari", "Quartu Sant'Elena", "Serramanna"]
                      }
                    },
                    {
                      "type": "Polygon",
                      "properties": {
                        "Nome zona": "Montagna bolognese",
                        "Rappresentata nella mappa": "Ordinaria per rischio idrogeologico / ALLERTA GIALLA",
                        "Per rischio idraulico": "Assenza di fenomeni significativi prevedibili / NESSUNA ALLERTA",
                        "Per rischio temporali": "Assenza di fenomeni significativi prevedibili / NESSUNA ALLERTA",
                        "Per rischio idrogeologico": "Ordinaria / ALLERTA GIALLA",
                        "Comuni": ["Castiglione dei Pepoli", "Vergato"]
                      }
                    },
                    {
                      "type": "Polygon",
                      "properties": {
                        "Nome zona": "Bacino Ligure",
                        "Rappresentata nella mappa": "Moderata per temporali / ALLERTA ARANCIONE",
                        "Per rischio idraulico": "Ordinaria / ALLERTA GIALLA",
                        "Per rischio temporali": "Moderata / ALLERTA ARANCIONE",
                        "Per rischio idrogeologico": "Ordinaria / ALLERTA GIALLA",
                        "Comuni": ["Genova"]
                      }
                    }
                  ]
                }
              }
            }
            """;

            var baseNow = new DateTime(2026, 11, 20, 17, 10, 0);

            var serramanna = new OfficialAlertService.GeoRegionInfo(
                "it", "IT-88", "IT-VS", "Sardegna", "Medio Campidano", string.Empty, string.Empty, "Serramanna");
            (bool sFound, WeatherAlertInfo? sAlert) = OfficialAlertService.ParseDpcTopoJson(dpcTopoJson, serramanna, baseNow, "it");
            AssertEqual(true, sFound, "Serramanna trovata nel bollettino DPC");
            AssertTrue(sAlert is null, "Serramanna in zona Campidano senza allerte deve restituire null");

            var quartu = new OfficialAlertService.GeoRegionInfo(
                "it", "IT-88", "IT-CA", "Sardegna", "Cagliari", string.Empty, string.Empty, "Quartu Sant Elena");
            (bool qFound, WeatherAlertInfo? qAlert) = OfficialAlertService.ParseDpcTopoJson(dpcTopoJson, quartu, baseNow, "it");
            AssertEqual(true, qFound, "Quartu Sant'Elena matchata anche senza apostrofo");
            AssertTrue(qAlert is null, "Quartu senza allerta");

            var vergato = new OfficialAlertService.GeoRegionInfo(
                "it", "IT-45", "IT-BO", "Emilia-Romagna", "Bologna", string.Empty, string.Empty, "Vergato");
            (bool vFound, WeatherAlertInfo? vAlert) = OfficialAlertService.ParseDpcTopoJson(dpcTopoJson, vergato, baseNow, "it");
            AssertEqual(true, vFound, "Vergato trovata");
            AssertTrue(vAlert is not null && vAlert.IsOfficial, "Vergato allerta ufficiale attiva");
            AssertEqual(WeatherAlertLevel.Yellow, vAlert!.Level, "Vergato Allerta Gialla");
            AssertEqual("IDROGEOLOGICO", vAlert.Title, "Titolo DPC idrogeologico IT");
            AssertEqual("Protezione Civile Nazionale", vAlert.SourceName, "Fonte DPC");

            (_, WeatherAlertInfo? vEn) = OfficialAlertService.ParseDpcTopoJson(dpcTopoJson, vergato, baseNow, "en");
            AssertTrue(vEn is not null && vEn.Title == "HYDROGEOLOGICAL", "Titolo DPC idrogeologico EN");

            var genova = new OfficialAlertService.GeoRegionInfo(
                "it", "IT-42", "IT-GE", "Liguria", "Genova", string.Empty, string.Empty, "Genova");
            (bool gFound, WeatherAlertInfo? gAlert) = OfficialAlertService.ParseDpcTopoJson(dpcTopoJson, genova, baseNow, "it");
            AssertEqual(true, gFound, "Genova trovata");
            AssertTrue(gAlert is not null && gAlert.Level == WeatherAlertLevel.Orange, "Genova Allerta Arancione temporali");
            AssertEqual("TEMPORALE", gAlert!.Title, "Titolo DPC temporali");
        }

        private static void TestIpmaPortugalOfficialAlertsParsing()
        {
            AssertEqual(false, OfficialAlertService.UsesMeteoAlarm("pt"), "Portogallo usa IPMA e non MeteoAlarm");

            var baseNow = new DateTime(2026, 11, 20, 17, 10, 0);
            string startActive = baseNow.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            string endFuture = baseNow.AddHours(6).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

            string ipmaJson = $$"""
            [
              {
                "text": "Tempo sem avisos.",
                "awarenessTypeName": "Precipitação",
                "idAreaAviso": "VIS",
                "startTime": "{{startActive}}",
                "awarenessLevelID": "green",
                "endTime": "{{endFuture}}"
              },
              {
                "text": "Chuva persistente, por vezes forte.",
                "awarenessTypeName": "Precipitação",
                "idAreaAviso": "VIS",
                "startTime": "{{startActive}}",
                "awarenessLevelID": "orange",
                "endTime": "{{endFuture}}"
              },
              {
                "text": "Vento forte no litoral.",
                "awarenessTypeName": "Vento",
                "idAreaAviso": "LSB",
                "startTime": "{{startActive}}",
                "awarenessLevelID": "yellow",
                "endTime": "{{endFuture}}"
              }
            ]
            """;

            var viseu = new OfficialAlertService.GeoRegionInfo(
                "pt", "PT-18", string.Empty, "Viseu", "Viseu", string.Empty, string.Empty, "Viseu");
            WeatherAlertInfo? visIt = OfficialAlertService.ParseIpmaJson(ipmaJson, viseu, baseNow, "it");
            AssertTrue(visIt is not null && visIt.IsOfficial, "IPMA Viseu allerta ufficiale");
            AssertEqual(WeatherAlertLevel.Orange, visIt!.Level, "IPMA Viseu livello orange");
            AssertEqual("PIOGGIA", visIt.Title, "IPMA titolo pioggia IT");
            AssertEqual("IPMA", visIt.SourceName, "Fonte IPMA");

            WeatherAlertInfo? visPt = OfficialAlertService.ParseIpmaJson(ipmaJson, viseu, baseNow, "pt");
            AssertTrue(visPt is not null && visPt.Title == "PRECIPITAÇÃO", "IPMA titolo pioggia PT");

            var faro = new OfficialAlertService.GeoRegionInfo(
                "pt", "PT-08", string.Empty, "Faro", "Faro", string.Empty, string.Empty, "Faro");
            AssertTrue(OfficialAlertService.ParseIpmaJson(ipmaJson, faro, baseNow, "pt") is null, "Faro senza avvisi in array -> null");
        }

        private static void TestMetNorwayOfficialAlertsParsing()
        {
            AssertEqual(false, OfficialAlertService.UsesMeteoAlarm("no"), "Norvegia usa MET Norway e non MeteoAlarm");

            var baseNow = new DateTime(2026, 11, 20, 17, 10, 0);
            DateTimeOffset refLocal = new DateTimeOffset(baseNow, TimeZoneInfo.Local.GetUtcOffset(baseNow));
            string onset = refLocal.AddHours(-1).ToString("O", CultureInfo.InvariantCulture);
            string expires = refLocal.AddHours(5).ToString("O", CultureInfo.InvariantCulture);

            const string emptyNoJson = """{"features":[],"lang":"no","lastChange":"2026-11-20T16:12:38+00:00","type":"FeatureCollection"}""";
            AssertTrue(OfficialAlertService.ParseMetNorwayJson(emptyNoJson, baseNow, "it") is null, "MET Norway features vuoto -> null");

            string activeNoJson = $$"""
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "when": {
                    "interval": ["{{onset}}", "{{expires}}"]
                  },
                  "properties": {
                    "status": "Actual",
                    "severity": "Severe",
                    "awareness_level": "3; orange; Severe",
                    "certainty": "Observed",
                    "event": "gale",
                    "eventAwarenessName": "Stiv kuling",
                    "description": "Strong wind gusts along coastal Oslo fjord."
                  }
                }
              ]
            }
            """;

            WeatherAlertInfo? noAlert = OfficialAlertService.ParseMetNorwayJson(activeNoJson, baseNow, "it");
            AssertTrue(noAlert is not null && noAlert.IsOfficial, "MET Norway allerta ufficiale");
            AssertEqual(WeatherAlertLevel.Orange, noAlert!.Level, "MET Norway Severe -> Orange");
            AssertEqual("ALLERTA VENTO", noAlert.Title, "MET Norway titolo vento IT");
            AssertEqual("MET Norway", noAlert.SourceName, "Fonte MET Norway");
            AssertEqual("Observed", noAlert.Certainty, "Certainty Observed preservata");
        }

        private static void TestHourlyStripSeparateCurvesAndHumidity()
        {
            string url = WeatherService.BuildForecastUrl(39.4234, 8.9224);
            AssertTrue(url.Contains("relative_humidity_2m", StringComparison.Ordinal), "URL Open-Meteo deve richiedere relative_humidity_2m");
            int hourlyIdx = url.IndexOf("hourly=", StringComparison.Ordinal);
            AssertTrue(hourlyIdx > 0 && url.Substring(hourlyIdx).Contains("relative_humidity_2m", StringComparison.Ordinal), "Parametro hourly deve includere relative_humidity_2m");

            var baseNow = new DateTime(2026, 11, 20, 12, 0, 0);
            const string json = """
            {
              "current": { "temperature_2m": 15.0, "apparent_temperature": 14.0, "weather_code": 2, "is_day": 1, "wind_speed_10m": 12.0, "wind_gusts_10m": 20.0, "relative_humidity_2m": 68, "uv_index": 2.0 },
              "hourly": {
                "time": ["2026-11-20T13:00", "2026-11-20T14:00"],
                "temperature_2m": [16.0, 18.0],
                "weather_code": [2, 3],
                "precipitation_probability": [20, 60],
                "relative_humidity_2m": [60, 80],
                "wind_speed_10m": [12.0, 14.0],
                "is_day": [1, 1]
              },
              "daily": { "time": ["2026-11-20"], "weather_code": [2], "temperature_2m_max": [18.0], "temperature_2m_min": [10.0], "precipitation_probability_max": [60], "wind_speed_10m_max": [14.0] }
            }
            """;
            WeatherSnapshot parsed = WeatherService.ParseForecastJson(json, "Serramanna", baseNow);
            AssertEqual(60, parsed.NextHours[0].RelativeHumidity, "RelativeHumidity ora 1");
            AssertEqual(80, parsed.NextHours[1].RelativeHumidity, "RelativeHumidity ora 2");

            var solarSnap = new WeatherSnapshot
            {
                SunsetLocal = new DateTime(2026, 11, 20, 13, 30, 0),
                NextHours = new List<HourlyForecastPoint>
                {
                    new() { LocalTime = new DateTime(2026, 11, 20, 13, 0, 0), TemperatureC = 16.0, PrecipProbability = 20, RelativeHumidity = 60 },
                    new() { LocalTime = new DateTime(2026, 11, 20, 14, 0, 0), TemperatureC = 18.0, PrecipProbability = 60, RelativeHumidity = 80 }
                }
            };
            List<Dsk.Weather.Windows.WidgetWindow.HourlyStripSlot> slots =
                Dsk.Weather.Windows.WidgetWindow.BuildHourlyStripSlots(solarSnap);
            AssertEqual(3, slots.Count, "2 ore + 1 tramonto");
            AssertEqual(Dsk.Weather.Windows.WidgetWindow.HourlySlotKind.Sunset, slots[1].Kind, "Slot centrale è Tramonto");
            AssertEqual(40, slots[1].PrecipProbability, "Precipitazioni interpolate al tramonto (40%)");
            AssertEqual(70, slots[1].RelativeHumidity, "Umidità interpolata al tramonto (70%)");

            string tempDir = Path.Combine(Path.GetTempPath(), "DskWeatherTests_Curves_" + Guid.NewGuid().ToString("N"));
            Dsk.Weather.Windows.WidgetWindow? window = null;
            try
            {
                var settingsService = new SettingsService(tempDir);
                var cfg = new WidgetSettings
                {
                    ShowHourlyStrip = true,
                    ShowHourlyTempCurve = false,
                    ShowHourlyPrecipCurve = true,
                    ShowHourlyHumidityCurve = true,
                    PinMode = WindowPinMode.BottomMost,
                    LayoutStyle = WidgetLayoutStyle.PixelCard
                };
                cfg.AddOrSelectLocation("Serramanna", "Italia", 39.42335, 8.92243);
                settingsService.Save(cfg);
                WidgetSettings reloaded = settingsService.Load();
                AssertEqual(false, reloaded.ShowHourlyTempCurve, "Persistenza ShowHourlyTempCurve = false");
                AssertEqual(true, reloaded.ShowHourlyPrecipCurve, "Persistenza ShowHourlyPrecipCurve = true");
                AssertEqual(true, reloaded.ShowHourlyHumidityCurve, "Persistenza ShowHourlyHumidityCurve = true");

                reloaded.ResetStyleToDefaults();
                AssertEqual(false, reloaded.ShowHourlyTempCurve, "ResetStyleToDefaults non deve alterare ShowHourlyTempCurve");
                AssertEqual(true, reloaded.ShowHourlyPrecipCurve, "ResetStyleToDefaults non deve alterare ShowHourlyPrecipCurve");
                AssertEqual(true, reloaded.ShowHourlyHumidityCurve, "ResetStyleToDefaults non deve alterare ShowHourlyHumidityCurve");

                if (System.Windows.Application.Current is null)
                {
                    var app = new Dsk.Weather.App();
                    app.InitializeComponent();
                }

                window = new Dsk.Weather.Windows.WidgetWindow(
                    cfg,
                    settingsService,
                    new WeatherService(),
                    new MaterialColorService(),
                    new DesktopPinService(),
                    new AutostartService(),
                    () => { },
                    () => { });

                // 1. Solo curva termica attiva
                WidgetSettings onlyTemp = cfg.Clone();
                onlyTemp.ShowHourlyTempCurve = true;
                onlyTemp.ShowHourlyPrecipCurve = false;
                onlyTemp.ShowHourlyHumidityCurve = false;
                window.ApplySettings(onlyTemp, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Visible, window.HourlySparklineCanvas.Visibility, "Curva termica visibile");
                AssertEqual(System.Windows.Visibility.Collapsed, window.HourlyPrecipCanvas.Visibility, "Curva precipitazioni nascosta");
                AssertEqual(System.Windows.Visibility.Collapsed, window.HourlyHumidityCanvas.Visibility, "Curva umidità nascosta");
                AssertEqual(System.Windows.Visibility.Collapsed, window.HourlyCurvesLegend.Visibility, "Legenda nascosta con sola curva termica");

                // 2. Termica spenta + Precipitazioni & Umidità accese
                WidgetSettings precipAndHum = cfg.Clone();
                precipAndHum.ShowHourlyTempCurve = false;
                precipAndHum.ShowHourlyPrecipCurve = true;
                precipAndHum.ShowHourlyHumidityCurve = true;
                window.ApplySettings(precipAndHum, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Collapsed, window.HourlySparklineCanvas.Visibility, "Curva termica nascosta");
                AssertEqual(System.Windows.Visibility.Visible, window.HourlyPrecipCanvas.Visibility, "Curva precipitazioni visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.HourlyHumidityCanvas.Visibility, "Curva umidità visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.HourlyCurvesLegend.Visibility, "Legenda visibile con precipitazioni/umidità");
                AssertEqual(System.Windows.Visibility.Collapsed, window.LegendTempItem.Visibility, "Voce legenda Temp nascosta");
                AssertEqual(System.Windows.Visibility.Visible, window.LegendPrecipItem.Visibility, "Voce legenda Pioggia visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.LegendHumidityItem.Visibility, "Voce legenda Umidità visibile");

                // 3. Tutte e 3 attive
                WidgetSettings allThree = cfg.Clone();
                allThree.ShowHourlyTempCurve = true;
                allThree.ShowHourlyPrecipCurve = true;
                allThree.ShowHourlyHumidityCurve = true;
                window.ApplySettings(allThree, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Visible, window.HourlySparklineCanvas.Visibility, "Tutte e 3: Termica visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.HourlyPrecipCanvas.Visibility, "Tutte e 3: Precipitazioni visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.HourlyHumidityCanvas.Visibility, "Tutte e 3: Umidità visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.HourlyCurvesLegend.Visibility, "Tutte e 3: Legenda visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.LegendTempItem.Visibility, "Tutte e 3: Voce Temp visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.LegendPrecipItem.Visibility, "Tutte e 3: Voce Pioggia visibile");
                AssertEqual(System.Windows.Visibility.Visible, window.LegendHumidityItem.Visibility, "Tutte e 3: Voce Umidità visibile");

                // 4. Striscia 24h disattivata indipendentemente dalle curve
                WidgetSettings stripOff = allThree.Clone();
                stripOff.ShowHourlyStrip = false;
                window.ApplySettings(stripOff, refreshWeather: false);
                AssertEqual(System.Windows.Visibility.Collapsed, window.CardHourlyContainer.Visibility, "CardHourlyContainer nascosta se ShowHourlyStrip = false");
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

        private static void TestLocationAutoDetectionAndCleanSettingsUi()
        {
            const string geoJsSample = """{"city":"Lemmer","region":"Friesland","country":"The Netherlands","latitude":"52.8491","longitude":"5.699"}""";
            GeoLocationResult? geoParsed = WeatherService.ParseGeoJsJson(geoJsSample);
            AssertTrue(geoParsed is not null, "GeoJS JSON valido parsato");
            AssertEqual("Lemmer", geoParsed!.Name, "GeoJS città");
            AssertEqual("The Netherlands", geoParsed.Country, "GeoJS paese");
            AssertTrue(Math.Abs(geoParsed.Latitude - 52.8491) < 0.001, "GeoJS latitudine");

            const string ipWhoValid = """{"success":true,"city":"De Fryske Marren","region":"Friesland","country":"Netherlands","latitude":53.1667,"longitude":5.8333}""";
            GeoLocationResult? ipWhoParsed = WeatherService.ParseIpWhoIsJson(ipWhoValid);
            AssertTrue(ipWhoParsed is not null && ipWhoParsed.Name == "De Fryske Marren", "ipwho.is fallback valido");

            const string ipWhoFail = """{"success":false,"message":"invalid IP"}""";
            AssertTrue(WeatherService.ParseIpWhoIsJson(ipWhoFail) is null, "ipwho.is success=false deve restituire null");

            string tempDir = Path.Combine(Path.GetTempPath(), "DskWeatherTests_AutoLoc_" + Guid.NewGuid().ToString("N"));
            try
            {
                var svc = new SettingsService(tempDir);
                var cfg = new WidgetSettings
                {
                    AutoDetectLocation = true,
                    Language = "it"
                };
                cfg.AddOrSelectLocation("Lemmer", "Paesi Bassi", 52.8491, 5.699);
                svc.Save(cfg);

                WidgetSettings reloaded = svc.Load();
                AssertEqual(true, reloaded.AutoDetectLocation, "Persistenza AutoDetectLocation = true");
                WidgetSettings cloned = reloaded.Clone();
                AssertEqual(true, cloned.AutoDetectLocation, "Clone preserva AutoDetectLocation");
                cloned.ResetStyleToDefaults();
                AssertEqual(true, cloned.AutoDetectLocation, "ResetStyleToDefaults non deve alterare AutoDetectLocation");

                if (System.Windows.Application.Current is null)
                {
                    var app = new Dsk.Weather.App();
                    app.InitializeComponent();
                }

                WidgetSettings manualCfg = reloaded.Clone();
                manualCfg.AutoDetectLocation = false;
                var winManual = new Dsk.Weather.Windows.SettingsWindow(
                    manualCfg,
                    new WeatherService(),
                    new AutostartService(),
                    _ => { },
                    () => { });
                try
                {
                    AssertEqual("Lemmer, Paesi Bassi", winManual.ActiveLocationText.Text, "ActiveLocationText senza coordinate");
                    AssertTrue(!winManual.ActiveLocationText.Text.Contains('('), "Nessuna parentesi/coordinata in ActiveLocationText");
                    AssertEqual("Manuale", winManual.LocationModeText.Text, "Badge modalità Manuale");
                    winManual.ApplyLanguage("en");
                    AssertEqual("Automatic location (network / IP)", (string)winManual.AutoLocationCheck.Content, "Switch Posizione automatica in inglese");
                    AssertEqual("Manual", winManual.LocationModeText.Text, "Badge modalità Manual in inglese");
                }
                finally
                {
                    winManual.Close();
                }

                var winAuto = new Dsk.Weather.Windows.SettingsWindow(
                    reloaded,
                    new WeatherService(),
                    new AutostartService(),
                    _ => { },
                    () => { });
                try
                {
                    AssertEqual("Auto", winAuto.LocationModeText.Text, "Badge modalità Auto con AutoDetectLocation = true");
                }
                finally
                {
                    winAuto.Close();
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

        private static void TestSettingsWindowExitWidgetAction()
        {
            Type settingsType = typeof(Dsk.Weather.Windows.SettingsWindow);
            System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public;
            AssertTrue(settingsType.GetField("BtnResetPositionBottom", flags) is null, "BtnResetPositionBottom deve essere stato rimosso");
            AssertTrue(settingsType.GetField("BtnRefreshWeatherBottom", flags) is null, "BtnRefreshWeatherBottom deve essere stato rimosso");
            AssertTrue(settingsType.GetField("BtnCloseBottom", flags) is null, "BtnCloseBottom deve essere stato rimosso");

            if (System.Windows.Application.Current is null)
            {
                var app = new Dsk.Weather.App();
                app.InitializeComponent();
            }

            bool exitCalled = false;
            var cfg = new WidgetSettings
            {
                Language = "it"
            };
            var win = new Dsk.Weather.Windows.SettingsWindow(
                cfg,
                new WeatherService(),
                new AutostartService(),
                _ => { },
                () => { },
                onExitApp: () => exitCalled = true);
            try
            {
                AssertEqual("Chiudi completamente il widget", win.BtnExitWidgetText.Text, "Testo BtnExitWidget in italiano");
                AssertEqual(
                    "Termina Dsk.Weather e rimuove il widget dal desktop e dalla barra di sistema",
                    win.BtnExitWidget.ToolTip as string,
                    "Tooltip BtnExitWidget in italiano");

                win.ApplyLanguage("en");
                AssertEqual("Exit widget completely", win.BtnExitWidgetText.Text, "Testo BtnExitWidget in inglese");
                AssertEqual(
                    "Close Dsk.Weather and remove the widget from the desktop and system tray",
                    win.BtnExitWidget.ToolTip as string,
                    "Tooltip BtnExitWidget in inglese");

                win.BtnExitWidget.RaiseEvent(
                    new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                AssertTrue(exitCalled, "Il click su BtnExitWidget deve invocare onExitApp");
            }
            finally
            {
                win.Close();
            }
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
