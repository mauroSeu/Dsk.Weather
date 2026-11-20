using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PixelDesktopWidget.Models;

namespace PixelDesktopWidget.Services
{
    public sealed class WeatherService
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private readonly SettingsService? _settingsService;

        public WeatherService(SettingsService? settingsService = null)
        {
            _settingsService = settingsService;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PixelDesktopWidget/1.0");
            return client;
        }

        public static string DescribeWmoCodeItalian(int code)
        {
            return code switch
            {
                0 => "Sereno",
                1 => "Prevalentemente sereno",
                2 => "Poco nuvoloso",
                3 => "Nuvoloso",
                45 or 48 => "Nebbia",
                51 or 53 or 55 => "Pioviggine",
                56 or 57 => "Pioviggine gelata",
                61 => "Pioggia debole",
                63 => "Pioggia moderata",
                65 => "Pioggia forte",
                66 or 67 => "Pioggia gelata",
                71 or 73 or 75 or 77 => "Neve",
                80 or 81 or 82 => "Rovesci",
                85 or 86 => "Rovesci di neve",
                95 or 96 or 99 => "Temporale",
                _ => "Variabile"
            };
        }

        public static WeatherIconKind MapWmoToIcon(int code, bool isDay)
        {
            return code switch
            {
                0 or 1 => isDay ? WeatherIconKind.ClearDay : WeatherIconKind.ClearNight,
                2 => isDay ? WeatherIconKind.PartlyCloudyDay : WeatherIconKind.PartlyCloudyNight,
                3 => WeatherIconKind.Cloudy,
                45 or 48 => WeatherIconKind.Fog,
                51 or 53 or 55 or 56 or 57 => WeatherIconKind.Drizzle,
                61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherIconKind.Rain,
                71 or 73 or 75 or 77 or 85 or 86 => WeatherIconKind.Snow,
                95 or 96 or 99 => WeatherIconKind.Thunderstorm,
                _ => isDay ? WeatherIconKind.PartlyCloudyDay : WeatherIconKind.PartlyCloudyNight
            };
        }

        public static string FormatTemp(double tempC, TemperatureUnit unit)
        {
            double value = unit == TemperatureUnit.Fahrenheit
                ? (tempC * 9.0 / 5.0) + 32.0
                : tempC;
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + "°";
        }

        public static string GetOpenMeteoModelParam(WeatherModelPreference model)
        {
            return model switch
            {
                WeatherModelPreference.EcmwfIfs => "ecmwf_ifs025",
                WeatherModelPreference.DwdIcon => "icon_seamless",
                _ => "best_match"
            };
        }

        public static string DescribeWeatherModelItalian(WeatherModelPreference model)
        {
            return model switch
            {
                WeatherModelPreference.EcmwfIfs => "ECMWF",
                WeatherModelPreference.DwdIcon => "ICON",
                _ => "Auto"
            };
        }

        public static string DescribeUvLevelItalian(double uv)
        {
            if (uv < 3.0) return "Basso";
            if (uv < 6.0) return "Moderato";
            if (uv < 8.0) return "Alto";
            if (uv < 11.0) return "Molto alto";
            return "Estremo";
        }

        public static string DescribeWindDirectionCardinal(int degrees)
        {
            int norm = ((degrees % 360) + 360) % 360;
            string[] dirs = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
            int idx = (int)Math.Round(norm / 45.0, MidpointRounding.AwayFromZero) % 8;
            return dirs[idx];
        }

        public static string BuildAtAGlanceInsight(WeatherSnapshot w, DateTime localNow)
        {
            if (w is null)
            {
                return string.Empty;
            }

            List<HourlyForecastPoint> horizon = w.Next12Hours is { Count: > 0 }
                ? w.Next12Hours
                : w.NextHours;

            if (w.WeatherCode >= 95)
            {
                return "Temporale in corso · Prestare attenzione";
            }

            HourlyForecastPoint? upcomingStorm = FindFirstMatching(horizon, 12, localNow, hp => hp.WeatherCode >= 95);
            if (upcomingStorm is not null)
            {
                return $"Temporale previsto verso le {upcomingStorm.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)}";
            }

            if (IsSnowCode(w.WeatherCode))
            {
                return "Neve in corso";
            }

            HourlyForecastPoint? upcomingSnow = FindFirstMatching(horizon, 12, localNow, hp => IsSnowCode(hp.WeatherCode));
            if (upcomingSnow is not null)
            {
                return $"Neve prevista verso le {upcomingSnow.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)}";
            }

            if (IsRainOrDrizzleCode(w.WeatherCode))
            {
                HourlyForecastPoint? dryAgain = FindFirstMatching(
                    horizon,
                    10,
                    localNow,
                    hp => !IsRainOrDrizzleCode(hp.WeatherCode) && hp.WeatherCode < 95 && hp.PrecipProbability < 35);

                if (dryAgain is not null)
                {
                    return $"Pioggia in corso · Migliora verso le {dryAgain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)}";
                }

                return "Pioggia in corso nelle prossime ore";
            }

            HourlyForecastPoint? upcomingRain = FindFirstMatching(
                horizon,
                12,
                localNow,
                hp => IsRainOrDrizzleCode(hp.WeatherCode) || hp.PrecipProbability >= 50);

            if (upcomingRain is not null)
            {
                int prob = Math.Max(upcomingRain.PrecipProbability, 50);
                return $"Pioggia in arrivo verso le {upcomingRain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture)} ({prob}%)";
            }

            double peakWind = w.WindSpeedKmh;
            foreach (HourlyForecastPoint hp in horizon)
            {
                if (hp.LocalTime <= localNow.AddHours(12) && hp.WindSpeedKmh > peakWind)
                {
                    peakWind = hp.WindSpeedKmh;
                }
            }

            if (w.WindSpeedKmh >= 35.0 || peakWind >= 38.0)
            {
                return $"Vento sostenuto ({Math.Round(peakWind).ToString("0", CultureInfo.InvariantCulture)} km/h)";
            }

            if (w.WeatherCode is 45 or 48 ||
                FindFirstMatching(horizon, 8, localNow, hp => hp.WeatherCode is 45 or 48) is not null)
            {
                return "Nebbia presente o in formazione";
            }

            if (w.IsDay && w.CurrentUvIndex >= 6.0)
            {
                string level = DescribeUvLevelItalian(w.CurrentUvIndex).ToLowerInvariant();
                string uvFormatted = w.CurrentUvIndex.ToString("0.#", CultureInfo.InvariantCulture);
                return $"Indice UV {level} ({uvFormatted}) · Protezione consigliata";
            }

            double minNextTemp = double.MaxValue;
            foreach (HourlyForecastPoint hp in horizon)
            {
                if (hp.LocalTime <= localNow.AddHours(8) && hp.TemperatureC < minNextTemp)
                {
                    minNextTemp = hp.TemperatureC;
                }
            }

            if (minNextTemp < double.MaxValue && (w.TemperatureC - minNextTemp) >= 6.0)
            {
                return $"Calo termico in arrivo (fino a {Math.Round(minNextTemp).ToString("0", CultureInfo.InvariantCulture)}°)";
            }

            if (w.SunsetLocal.HasValue)
            {
                double minsToSunset = (w.SunsetLocal.Value - localNow).TotalMinutes;
                if (minsToSunset >= 0.0 && minsToSunset <= 45.0)
                {
                    return $"Tramonto alle {w.SunsetLocal.Value.ToString("HH:mm", CultureInfo.InvariantCulture)}";
                }
            }

            if (w.SunriseLocal.HasValue)
            {
                double minsToSunrise = (w.SunriseLocal.Value - localNow).TotalMinutes;
                if (minsToSunrise >= 0.0 && minsToSunrise <= 45.0)
                {
                    return $"Alba alle {w.SunriseLocal.Value.ToString("HH:mm", CultureInfo.InvariantCulture)}";
                }
            }

            if (w.WeatherCode is 0 or 1)
            {
                return w.IsDay ? "Giornata serena e stabile" : "Serata limpida e tranquilla";
            }

            return $"Nuvolosità variabile · Rischio pioggia {w.TodayPrecipProbMax}%";
        }

        private static HourlyForecastPoint? FindFirstMatching(
            List<HourlyForecastPoint> points,
            int maxHoursAhead,
            DateTime localNow,
            Func<HourlyForecastPoint, bool> predicate)
        {
            DateTime limit = localNow.AddHours(maxHoursAhead);
            foreach (HourlyForecastPoint hp in points)
            {
                if (hp.LocalTime <= limit && predicate(hp))
                {
                    return hp;
                }
            }
            return null;
        }

        private static bool IsRainOrDrizzleCode(int code)
        {
            return (code >= 51 && code <= 67) || (code >= 80 && code <= 82);
        }

        private static bool IsSnowCode(int code)
        {
            return (code >= 71 && code <= 77) || code is 85 or 86;
        }

        public static string BuildForecastUrl(double lat, double lon, WeatherModelPreference model = WeatherModelPreference.Auto)
        {
            string latStr = lat.ToString("0.####", CultureInfo.InvariantCulture);
            string lonStr = lon.ToString("0.####", CultureInfo.InvariantCulture);
            string modelParam = GetOpenMeteoModelParam(model);
            return $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,wind_direction_10m,relative_humidity_2m,uv_index&hourly=temperature_2m,weather_code,precipitation_probability,wind_speed_10m,wind_direction_10m,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,wind_speed_10m_max,wind_direction_10m_dominant,sunrise,sunset,uv_index_max&timezone=auto&forecast_days=7&models={modelParam}";
        }

        public static string BuildGeocodingUrl(string query)
        {
            string escaped = Uri.EscapeDataString(query.Trim());
            return $"https://geocoding-api.open-meteo.com/v1/search?name={escaped}&count=6&language=it&format=json";
        }

        public async Task<WeatherSnapshot?> FetchForecastAsync(
            double lat,
            double lon,
            string cityName,
            WeatherModelPreference model = WeatherModelPreference.Auto,
            CancellationToken ct = default)
        {
            string url = BuildForecastUrl(lat, lon, model);
            try
            {
                using var response = await SharedHttpClient.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                DateTime referenceNow = DateTime.Now;
                WeatherSnapshot snapshot = ParseForecastJson(json, cityName, referenceNow);
                snapshot.ModelUsed = GetOpenMeteoModelParam(model);
                snapshot.IsFromCache = false;
                _settingsService?.SaveWeatherCache(snapshot);
                return snapshot;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                WeatherSnapshot? cached = _settingsService?.LoadWeatherCache();
                if (cached is not null)
                {
                    cached.IsFromCache = true;
                    if (!string.IsNullOrWhiteSpace(cityName))
                    {
                        cached.CityName = cityName;
                    }
                    return cached;
                }

                return null;
            }
        }

        public async Task<List<GeoLocationResult>> SearchCitiesAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return new List<GeoLocationResult>();
            }

            string url = BuildGeocodingUrl(query);
            try
            {
                using var response = await SharedHttpClient.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseGeocodingJson(json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new List<GeoLocationResult>();
            }
        }

        public async Task<GeoLocationResult> DetectLocationByIpAsync(CancellationToken ct = default)
        {
            try
            {
                using var response = await SharedHttpClient.GetAsync("https://get.geojs.io/v1/ip/geo.json", ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                string city = TryGetString(root, "city");
                string country = TryGetString(root, "country");
                string region = TryGetString(root, "region");

                double lat = TryGetFlexibleDouble(root, "latitude", 41.9028);
                double lon = TryGetFlexibleDouble(root, "longitude", 12.4964);

                if (string.IsNullOrWhiteSpace(city))
                {
                    city = "Roma";
                }

                if (string.IsNullOrWhiteSpace(country))
                {
                    country = "Italia";
                }

                return new GeoLocationResult
                {
                    Name = city,
                    Admin1 = region,
                    Country = country,
                    Latitude = lat,
                    Longitude = lon
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new GeoLocationResult
                {
                    Name = "Roma",
                    Admin1 = "Lazio",
                    Country = "Italia",
                    Latitude = 41.9028,
                    Longitude = 12.4964
                };
            }
        }

        internal static WeatherSnapshot ParseForecastJson(string json, string cityName, DateTime referenceLocalNow)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            var snapshot = new WeatherSnapshot
            {
                FetchedAtUtc = DateTime.UtcNow,
                CityName = string.IsNullOrWhiteSpace(cityName) ? "Posizione locale" : cityName.Trim(),
                IsFromCache = false
            };

            if (root.TryGetProperty("current", out JsonElement current))
            {
                snapshot.TemperatureC = TryGetDouble(current, "temperature_2m", 0.0);
                snapshot.ApparentTemperatureC = TryGetDouble(current, "apparent_temperature", snapshot.TemperatureC);
                snapshot.WeatherCode = TryGetInt(current, "weather_code", 0);
                snapshot.IsDay = TryGetInt(current, "is_day", 1) == 1;
                snapshot.WindSpeedKmh = TryGetDouble(current, "wind_speed_10m", 0.0);
                snapshot.WindDirectionDeg = TryGetInt(current, "wind_direction_10m", 0);
                snapshot.RelativeHumidity = TryGetInt(current, "relative_humidity_2m", 0);
                snapshot.CurrentUvIndex = TryGetDouble(current, "uv_index", 0.0);
            }

            if (root.TryGetProperty("hourly", out JsonElement hourly))
            {
                var times = GetStringArray(hourly, "time");
                var temps = GetDoubleArray(hourly, "temperature_2m");
                var codes = GetIntArray(hourly, "weather_code");
                var probs = GetIntArray(hourly, "precipitation_probability");
                var winds = GetDoubleArray(hourly, "wind_speed_10m");
                var windDirs = GetIntArray(hourly, "wind_direction_10m");
                var isDays = GetIntArray(hourly, "is_day");

                DateTime minThreshold = new DateTime(
                    referenceLocalNow.Year,
                    referenceLocalNow.Month,
                    referenceLocalNow.Day,
                    referenceLocalNow.Hour,
                    0,
                    0).AddHours(1);

                int count = times.Count;
                for (int i = 0; i < count && snapshot.NextHours.Count < 24; i++)
                {
                    if (!DateTime.TryParseExact(
                            times[i],
                            "yyyy-MM-dd'T'HH:mm",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out DateTime parsedTime))
                    {
                        if (!DateTime.TryParse(times[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
                        {
                            continue;
                        }
                    }

                    if (parsedTime < minThreshold)
                    {
                        continue;
                    }

                    var point = new HourlyForecastPoint
                    {
                        LocalTime = parsedTime,
                        TemperatureC = i < temps.Count ? temps[i] : snapshot.TemperatureC,
                        WeatherCode = i < codes.Count ? codes[i] : snapshot.WeatherCode,
                        PrecipProbability = i < probs.Count ? probs[i] : 0,
                        WindSpeedKmh = i < winds.Count ? winds[i] : snapshot.WindSpeedKmh,
                        WindDirectionDeg = i < windDirs.Count ? windDirs[i] : snapshot.WindDirectionDeg,
                        IsDay = i < isDays.Count ? isDays[i] == 1 : snapshot.IsDay
                    };

                    snapshot.NextHours.Add(point);
                    if (snapshot.Next12Hours.Count < 12)
                    {
                        snapshot.Next12Hours.Add(point);
                    }
                }
            }

            if (root.TryGetProperty("daily", out JsonElement daily))
            {
                var dates = GetStringArray(daily, "time");
                var codes = GetIntArray(daily, "weather_code");
                var maxTemps = GetDoubleArray(daily, "temperature_2m_max");
                var minTemps = GetDoubleArray(daily, "temperature_2m_min");
                var probs = GetIntArray(daily, "precipitation_probability_max");
                var maxWinds = GetDoubleArray(daily, "wind_speed_10m_max");
                var windDirs = GetIntArray(daily, "wind_direction_10m_dominant");
                var sunrises = GetStringArray(daily, "sunrise");
                var sunsets = GetStringArray(daily, "sunset");
                var uvMaxList = GetDoubleArray(daily, "uv_index_max");

                int count = Math.Min(dates.Count, 7);
                for (int i = 0; i < count; i++)
                {
                    if (!DateTime.TryParseExact(
                            dates[i],
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out DateTime parsedDate))
                    {
                        if (!DateTime.TryParse(dates[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
                        {
                            continue;
                        }
                    }

                    var dayPoint = new DailyForecastPoint
                    {
                        Date = parsedDate,
                        WeatherCode = i < codes.Count ? codes[i] : snapshot.WeatherCode,
                        MaxC = i < maxTemps.Count ? maxTemps[i] : snapshot.TemperatureC,
                        MinC = i < minTemps.Count ? minTemps[i] : snapshot.TemperatureC,
                        PrecipProbabilityMax = i < probs.Count ? probs[i] : 0,
                        WindSpeedMaxKmh = i < maxWinds.Count ? maxWinds[i] : snapshot.WindSpeedKmh,
                        WindDirectionDeg = i < windDirs.Count ? windDirs[i] : snapshot.WindDirectionDeg
                    };
                    snapshot.Daily.Add(dayPoint);
                }

                if (sunrises.Count > 0 &&
                    DateTime.TryParseExact(sunrises[0], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedSunrise))
                {
                    snapshot.SunriseLocal = parsedSunrise;
                }

                if (sunsets.Count > 0 &&
                    DateTime.TryParseExact(sunsets[0], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedSunset))
                {
                    snapshot.SunsetLocal = parsedSunset;
                }

                if (uvMaxList.Count > 0)
                {
                    snapshot.TodayUvIndexMax = uvMaxList[0];
                }

                if (snapshot.Daily.Count > 0)
                {
                    snapshot.TodayMaxC = snapshot.Daily[0].MaxC;
                    snapshot.TodayMinC = snapshot.Daily[0].MinC;
                    snapshot.TodayPrecipProbMax = snapshot.Daily[0].PrecipProbabilityMax;
                }
                else
                {
                    snapshot.TodayMaxC = snapshot.TemperatureC;
                    snapshot.TodayMinC = snapshot.TemperatureC;
                }
            }

            return snapshot;
        }

        internal static List<GeoLocationResult> ParseGeocodingJson(string json)
        {
            var results = new List<GeoLocationResult>();
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (JsonElement item in array.EnumerateArray())
            {
                string name = TryGetString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                results.Add(new GeoLocationResult
                {
                    Name = name,
                    Admin1 = TryGetString(item, "admin1"),
                    Country = TryGetString(item, "country"),
                    Latitude = TryGetDouble(item, "latitude", 0.0),
                    Longitude = TryGetDouble(item, "longitude", 0.0)
                });
            }

            return results;
        }

        private static string TryGetString(JsonElement el, string propName)
        {
            if (el.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static double TryGetDouble(JsonElement el, string propName, double fallback)
        {
            if (el.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d))
            {
                return d;
            }
            return fallback;
        }

        private static double TryGetFlexibleDouble(JsonElement el, string propName, double fallback)
        {
            if (!el.TryGetProperty(propName, out JsonElement val))
            {
                return fallback;
            }

            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out double d))
            {
                return d;
            }

            if (val.ValueKind == JsonValueKind.String &&
                double.TryParse(val.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static int TryGetInt(JsonElement el, string propName, int fallback)
        {
            if (el.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.Number)
            {
                if (val.TryGetInt32(out int i))
                {
                    return i;
                }
                if (val.TryGetDouble(out double d))
                {
                    return (int)Math.Round(d);
                }
            }
            return fallback;
        }

        private static List<string> GetStringArray(JsonElement el, string propName)
        {
            var list = new List<string>();
            if (el.TryGetProperty(propName, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in arr.EnumerateArray())
                {
                    list.Add(item.ValueKind == JsonValueKind.String ? (item.GetString() ?? string.Empty) : string.Empty);
                }
            }
            return list;
        }

        private static List<double> GetDoubleArray(JsonElement el, string propName)
        {
            var list = new List<double>();
            if (el.TryGetProperty(propName, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in arr.EnumerateArray())
                {
                    list.Add(item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double d) ? d : 0.0);
                }
            }
            return list;
        }

        private static List<int> GetIntArray(JsonElement el, string propName)
        {
            var list = new List<int>();
            if (el.TryGetProperty(propName, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number)
                    {
                        if (item.TryGetInt32(out int i))
                        {
                            list.Add(i);
                        }
                        else if (item.TryGetDouble(out double d))
                        {
                            list.Add((int)Math.Round(d));
                        }
                        else
                        {
                            list.Add(0);
                        }
                    }
                    else
                    {
                        list.Add(0);
                    }
                }
            }
            return list;
        }
    }
}
