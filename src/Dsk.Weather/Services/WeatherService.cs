using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dsk.Weather.Models;

namespace Dsk.Weather.Services
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Dsk.Weather/1.0");
            return client;
        }

        public static string DescribeWmoCodeItalian(int code, string lang = "it")
        {
            return LocalizationService.DescribeWmoCode(code, lang);
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

        public static string DescribeWeatherModelItalian(WeatherModelPreference model, string lang = "it")
        {
            return LocalizationService.DescribeWeatherModel(model, lang);
        }

        public static string DescribeUvLevelItalian(double uv, string lang = "it")
        {
            return LocalizationService.DescribeUvLevel(uv, lang);
        }

        public static string DescribeWindDirectionCardinal(int degrees, string lang = "it")
        {
            return LocalizationService.DescribeWindDirectionCardinal(degrees, lang);
        }

        public static string BuildAtAGlanceInsight(WeatherSnapshot w, DateTime localNow, string lang = "it")
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
                return LocalizationService.T(lang, "insight_storm_now");
            }

            HourlyForecastPoint? upcomingStorm = FindFirstMatching(horizon, 12, localNow, hp => hp.WeatherCode >= 95);
            if (upcomingStorm is not null)
            {
                return LocalizationService.Format(lang, "insight_storm_at", upcomingStorm.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture));
            }

            if (IsSnowCode(w.WeatherCode))
            {
                return LocalizationService.T(lang, "insight_snow_now");
            }

            HourlyForecastPoint? upcomingSnow = FindFirstMatching(horizon, 12, localNow, hp => IsSnowCode(hp.WeatherCode));
            if (upcomingSnow is not null)
            {
                return LocalizationService.Format(lang, "insight_snow_at", upcomingSnow.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture));
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
                    return LocalizationService.Format(lang, "insight_rain_clear_at", dryAgain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture));
                }

                return LocalizationService.T(lang, "insight_rain_hours");
            }

            HourlyForecastPoint? upcomingRain = FindFirstMatching(
                horizon,
                12,
                localNow,
                hp => IsRainOrDrizzleCode(hp.WeatherCode) || hp.PrecipProbability >= 50);

            if (upcomingRain is not null)
            {
                int prob = Math.Max(upcomingRain.PrecipProbability, 50);
                return LocalizationService.Format(lang, "insight_rain_at", upcomingRain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture), prob);
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
                return LocalizationService.Format(lang, "insight_wind_strong", Math.Round(peakWind).ToString("0", CultureInfo.InvariantCulture));
            }

            if (w.WeatherCode is 45 or 48 ||
                FindFirstMatching(horizon, 8, localNow, hp => hp.WeatherCode is 45 or 48) is not null)
            {
                return LocalizationService.T(lang, "insight_fog");
            }

            if (w.IsDay && w.CurrentUvIndex >= 6.0)
            {
                string level = DescribeUvLevelItalian(w.CurrentUvIndex, lang).ToLowerInvariant();
                string uvFormatted = w.CurrentUvIndex.ToString("0.#", CultureInfo.InvariantCulture);
                return LocalizationService.Format(lang, "insight_uv", level, uvFormatted);
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
                return LocalizationService.Format(lang, "insight_temp_drop", Math.Round(minNextTemp).ToString("0", CultureInfo.InvariantCulture));
            }

            if (w.SunsetLocal.HasValue)
            {
                double minsToSunset = (w.SunsetLocal.Value - localNow).TotalMinutes;
                if (minsToSunset >= 0.0 && minsToSunset <= 45.0)
                {
                    return LocalizationService.Format(lang, "insight_sunset", w.SunsetLocal.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
                }
            }

            if (w.SunriseLocal.HasValue)
            {
                double minsToSunrise = (w.SunriseLocal.Value - localNow).TotalMinutes;
                if (minsToSunrise >= 0.0 && minsToSunrise <= 45.0)
                {
                    return LocalizationService.Format(lang, "insight_sunrise", w.SunriseLocal.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
                }
            }

            if (w.WeatherCode is 0 or 1)
            {
                return LocalizationService.T(lang, w.IsDay ? "insight_clear_day" : "insight_clear_night");
            }

            return LocalizationService.Format(lang, "insight_variable", w.TodayPrecipProbMax);
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

        private static HourlyForecastPoint? FindConsecutiveMatching(
            List<HourlyForecastPoint> points,
            int maxHoursAhead,
            DateTime localNow,
            int requiredConsecutive,
            Func<HourlyForecastPoint, bool> predicate)
        {
            if (requiredConsecutive <= 1)
            {
                return FindFirstMatching(points, maxHoursAhead, localNow, predicate);
            }

            DateTime limit = localNow.AddHours(maxHoursAhead);
            HourlyForecastPoint? runStart = null;
            DateTime prevMatchTime = default;
            int streak = 0;

            foreach (HourlyForecastPoint hp in points)
            {
                if (hp.LocalTime > limit)
                {
                    break;
                }

                if (!predicate(hp))
                {
                    runStart = null;
                    streak = 0;
                    continue;
                }

                if (runStart is null || (hp.LocalTime - prevMatchTime).TotalHours > 1.51)
                {
                    runStart = hp;
                    streak = 1;
                }
                else
                {
                    streak++;
                }

                prevMatchTime = hp.LocalTime;
                if (streak >= requiredConsecutive)
                {
                    return runStart;
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

        private static bool IsFreezingRainCode(int code)
        {
            return code is 56 or 57 or 66 or 67;
        }

        private static bool IsIcyRiskCurrent(WeatherSnapshot w)
        {
            return w.TemperatureC <= -5.0 ||
                (w.TemperatureC <= 0.0 && (w.RelativeHumidity >= 80 ||
                    IsRainOrDrizzleCode(w.WeatherCode) ||
                    IsSnowCode(w.WeatherCode) ||
                    w.WeatherCode is 45 or 48 ||
                    w.PrecipitationMm >= 0.1));
        }

        private static bool IsIcyRiskHourly(HourlyForecastPoint hp)
        {
            return hp.TemperatureC <= -5.0 ||
                (hp.TemperatureC <= 0.0 && (hp.PrecipProbability >= 40 ||
                    hp.PrecipitationMm >= 0.1 ||
                    IsRainOrDrizzleCode(hp.WeatherCode) ||
                    IsSnowCode(hp.WeatherCode) ||
                    hp.WeatherCode is 45 or 48));
        }

        public static WeatherAlertInfo EvaluateWeatherAlert(WeatherSnapshot? w, TemperatureUnit unit, DateTime localNow, string lang = "it")
        {
            if (w is null)
            {
                return WeatherAlertInfo.None;
            }

            List<HourlyForecastPoint> horizon = w.Next12Hours is { Count: > 0 }
                ? w.Next12Hours
                : w.NextHours;

            string nowLabel = LocalizationService.T(lang, "alert_now");

            // --- ALLERTA ROSSA (Priorità massima) ---
            if (w.WeatherCode >= 95)
            {
                string title = LocalizationService.T(lang, w.WeatherCode >= 96 ? "alert_hail" : "alert_storm");
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    title,
                    nowLabel,
                    LocalizationService.Format(lang, "alert_red_storm_now", title));
            }

            HourlyForecastPoint? upcomingStorm = FindFirstMatching(horizon, 12, localNow, hp => hp.WeatherCode >= 95);
            if (upcomingStorm is not null)
            {
                string title = LocalizationService.T(lang, upcomingStorm.WeatherCode >= 96 ? "alert_hail" : "alert_storm");
                string hhmm = upcomingStorm.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    title,
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_red_storm_at", title, hhmm));
            }

            if (IsFreezingRainCode(w.WeatherCode))
            {
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_freezing_rain"),
                    nowLabel,
                    LocalizationService.T(lang, "alert_red_fr_now"));
            }

            HourlyForecastPoint? freezingRain = FindFirstMatching(horizon, 12, localNow, hp => IsFreezingRainCode(hp.WeatherCode));
            if (freezingRain is not null)
            {
                string hhmm = freezingRain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_freezing_rain"),
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_red_fr_at", hhmm));
            }

            HourlyForecastPoint? severeGust = FindFirstMatching(horizon, 12, localNow, hp => hp.WindGustsKmh >= 90.0);
            HourlyForecastPoint? persistentRedGust = FindConsecutiveMatching(horizon, 12, localNow, 2, hp => hp.WindGustsKmh >= 75.0);
            if (w.WindGustsKmh >= 75.0 || severeGust is not null || persistentRedGust is not null)
            {
                double redGustVal = w.WindGustsKmh >= 75.0
                    ? w.WindGustsKmh
                    : (severeGust?.WindGustsKmh ?? persistentRedGust!.WindGustsKmh);
                DateTime? redGustTime = w.WindGustsKmh >= 75.0
                    ? null
                    : (severeGust?.LocalTime ?? persistentRedGust!.LocalTime);
                string gustStr = $"{Math.Round(redGustVal).ToString("0", CultureInfo.InvariantCulture)} km/h";
                string when = redGustTime.HasValue
                    ? LocalizationService.Format(lang, "alert_when_at", redGustTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture))
                    : LocalizationService.T(lang, "alert_when_now");
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_strong_gusts"),
                    gustStr,
                    LocalizationService.Format(lang, "alert_red_gusts", gustStr, when));
            }

            if (w.WeatherCode is 65 or 82 || w.PrecipitationMm >= 15.0)
            {
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_cloudburst"),
                    nowLabel,
                    LocalizationService.T(lang, "alert_red_cloudburst_now"));
            }

            HourlyForecastPoint? cloudburst = FindFirstMatching(horizon, 12, localNow, hp => hp.WeatherCode is 65 or 82 || hp.PrecipitationMm >= 15.0)
                ?? FindConsecutiveMatching(horizon, 12, localNow, 2, hp => hp.PrecipitationMm >= 10.0);
            if (cloudburst is not null)
            {
                string hhmm = cloudburst.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_cloudburst"),
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_red_cloudburst_at", hhmm));
            }

            if (w.WeatherCode is 75 or 86)
            {
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_heavy_snow"),
                    nowLabel,
                    LocalizationService.T(lang, "alert_red_snow_now"));
            }

            HourlyForecastPoint? heavySnow = FindFirstMatching(horizon, 12, localNow, hp => hp.WeatherCode is 75 or 86);
            if (heavySnow is not null)
            {
                string hhmm = heavySnow.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_heavy_snow"),
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_red_snow_at", hhmm));
            }

            if (w.TemperatureC >= 38.0 || w.ApparentTemperatureC >= 40.0)
            {
                string tempStr = FormatTemp(w.TemperatureC, unit);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Red,
                    LocalizationService.T(lang, "alert_extreme_heat"),
                    LocalizationService.Format(lang, "alert_current_temp", tempStr),
                    LocalizationService.Format(lang, "alert_red_heat", tempStr));
            }

            // --- ALLERTA GIALLA (Attenzione: corrente o persistente >= 2h consecutive) ---
            HourlyForecastPoint? persistentYellowGust = FindConsecutiveMatching(horizon, 12, localNow, 2, hp => hp.WindGustsKmh >= 60.0);
            if (w.WindGustsKmh >= 60.0 || persistentYellowGust is not null)
            {
                double yellowGustVal = w.WindGustsKmh >= 60.0 ? w.WindGustsKmh : persistentYellowGust!.WindGustsKmh;
                DateTime? yellowGustTime = w.WindGustsKmh >= 60.0 ? null : persistentYellowGust!.LocalTime;
                string gustStr = $"{Math.Round(yellowGustVal).ToString("0", CultureInfo.InvariantCulture)} km/h";
                string when = yellowGustTime.HasValue
                    ? LocalizationService.Format(lang, "alert_when_at", yellowGustTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture))
                    : LocalizationService.T(lang, "alert_when_now");
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_wind"),
                    LocalizationService.Format(lang, "alert_gusts_val", gustStr),
                    LocalizationService.Format(lang, "alert_yellow_gusts", gustStr, when));
            }

            HourlyForecastPoint? persistentWind = FindConsecutiveMatching(horizon, 12, localNow, 2, hp => hp.WindSpeedKmh >= 45.0);
            if (w.WindSpeedKmh >= 45.0 || persistentWind is not null)
            {
                double windVal = w.WindSpeedKmh >= 45.0 ? w.WindSpeedKmh : persistentWind!.WindSpeedKmh;
                DateTime? windTime = w.WindSpeedKmh >= 45.0 ? null : persistentWind!.LocalTime;
                string windStr = $"{Math.Round(windVal).ToString("0", CultureInfo.InvariantCulture)} km/h";
                string when = windTime.HasValue
                    ? LocalizationService.Format(lang, "alert_when_at", windTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture))
                    : LocalizationService.T(lang, "alert_when_now");
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_wind"),
                    LocalizationService.Format(lang, "alert_wind_val", windStr),
                    LocalizationService.Format(lang, "alert_yellow_wind", windStr, when));
            }

            if (IsIcyRiskCurrent(w))
            {
                string frostStr = FormatTemp(w.TemperatureC, unit);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_frost"),
                    LocalizationService.Format(lang, "alert_current_temp", frostStr),
                    LocalizationService.Format(lang, "alert_yellow_frost_now", frostStr));
            }

            HourlyForecastPoint? frostEvent = FindConsecutiveMatching(horizon, 12, localNow, 2, IsIcyRiskHourly);
            if (frostEvent is null && w.TemperatureC >= 4.0)
            {
                frostEvent = FindFirstMatching(
                    horizon,
                    12,
                    localNow,
                    hp => hp.TemperatureC <= 0.0 && (w.TemperatureC - hp.TemperatureC) >= 6.0);
            }

            if (frostEvent is not null)
            {
                string frostStr = FormatTemp(frostEvent.TemperatureC, unit);
                string hhmm = frostEvent.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_frost"),
                    LocalizationService.Format(lang, "alert_frost_at", frostStr, hhmm),
                    LocalizationService.Format(lang, "alert_yellow_frost_at", frostStr, hhmm));
            }

            if (w.WeatherCode is 63 or 81 || w.PrecipitationMm >= 5.0)
            {
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_heavy_rain"),
                    nowLabel,
                    LocalizationService.T(lang, "alert_yellow_rain_now"));
            }

            HourlyForecastPoint? strongRain = FindConsecutiveMatching(horizon, 12, localNow, 2, hp => hp.WeatherCode is 63 or 81 || hp.PrecipitationMm >= 5.0);
            if (strongRain is not null)
            {
                string hhmm = strongRain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_heavy_rain"),
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_yellow_rain_at", hhmm));
            }

            if (IsSnowCode(w.WeatherCode))
            {
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_snow"),
                    nowLabel,
                    LocalizationService.T(lang, "alert_yellow_snow_now"));
            }

            HourlyForecastPoint? upcomingSnowAlert = FindConsecutiveMatching(horizon, 12, localNow, 2, hp => IsSnowCode(hp.WeatherCode));
            if (upcomingSnowAlert is not null)
            {
                string hhmm = upcomingSnowAlert.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_snow"),
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_yellow_snow_at", hhmm));
            }

            if (w.WeatherCode is 45 or 48)
            {
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_fog"),
                    LocalizationService.T(lang, "alert_reduced_vis"),
                    LocalizationService.T(lang, "alert_yellow_fog_now"));
            }

            HourlyForecastPoint? fogAlert = FindConsecutiveMatching(horizon, 8, localNow, 2, hp => hp.WeatherCode is 45 or 48);
            if (fogAlert is not null)
            {
                string hhmm = fogAlert.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_fog"),
                    LocalizationService.Format(lang, "alert_at", hhmm),
                    LocalizationService.Format(lang, "alert_yellow_fog_at", hhmm));
            }

            if (w.IsDay && w.CurrentUvIndex >= 8.0)
            {
                string uvFormatted = w.CurrentUvIndex.ToString("0.#", CultureInfo.InvariantCulture);
                string uvLvl = DescribeUvLevelItalian(w.CurrentUvIndex, lang);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_uv_high"),
                    LocalizationService.Format(lang, "alert_uv_idx", uvFormatted),
                    LocalizationService.Format(lang, "alert_yellow_uv", uvFormatted, uvLvl));
            }

            if (w.TemperatureC >= 34.0 || w.ApparentTemperatureC >= 36.0)
            {
                string heatStr = FormatTemp(w.TemperatureC, unit);
                return new WeatherAlertInfo(
                    WeatherAlertLevel.Yellow,
                    LocalizationService.T(lang, "alert_heat"),
                    LocalizationService.Format(lang, "alert_current_temp", heatStr),
                    LocalizationService.Format(lang, "alert_yellow_heat", heatStr));
            }

            return WeatherAlertInfo.None;
        }

        public static WeatherAlertInfo EvaluateCriticalNowcastOnly(
            WeatherSnapshot? w,
            TemperatureUnit unit,
            DateTime localNow,
            string lang = "it")
        {
            if (w is null)
            {
                return WeatherAlertInfo.None;
            }

            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            string nowLabel = LocalizationService.T(normalizedLang, "alert_now");
            string estimateSource = LocalizationService.T(normalizedLang, "alert_source_estimate");
            List<HourlyForecastPoint> horizon = w.Next12Hours is { Count: > 0 }
                ? w.Next12Hours
                : w.NextHours;

            if (w.WeatherCode >= 95)
            {
                string title = LocalizationService.T(normalizedLang, w.WeatherCode >= 96 ? "alert_hail" : "alert_storm");
                string msg = LocalizationService.Format(normalizedLang, "alert_red_storm_now", title);
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, nowLabel, msg, false, estimateSource, msg);
            }

            HourlyForecastPoint? nearStorm = FindFirstMatching(horizon, 2, localNow, hp => hp.WeatherCode >= 95);
            if (nearStorm is not null)
            {
                string title = LocalizationService.T(normalizedLang, nearStorm.WeatherCode >= 96 ? "alert_hail" : "alert_storm");
                string hhmm = nearStorm.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                string msg = LocalizationService.Format(normalizedLang, "alert_red_storm_at", title, hhmm);
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, LocalizationService.Format(normalizedLang, "alert_at", hhmm), msg, false, estimateSource, msg);
            }

            if (IsFreezingRainCode(w.WeatherCode) ||
                (w.TemperatureC <= 0.0 && (IsRainOrDrizzleCode(w.WeatherCode) || w.PrecipitationMm >= 0.5)))
            {
                string title = LocalizationService.T(normalizedLang, "alert_freezing_rain");
                string msg = LocalizationService.T(normalizedLang, "alert_red_fr_now");
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, nowLabel, msg, false, estimateSource, msg);
            }

            HourlyForecastPoint? nearFreezingRain = FindFirstMatching(horizon, 2, localNow, hp => IsFreezingRainCode(hp.WeatherCode));
            if (nearFreezingRain is not null)
            {
                string title = LocalizationService.T(normalizedLang, "alert_freezing_rain");
                string hhmm = nearFreezingRain.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                string msg = LocalizationService.Format(normalizedLang, "alert_red_fr_at", hhmm);
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, LocalizationService.Format(normalizedLang, "alert_at", hhmm), msg, false, estimateSource, msg);
            }

            HourlyForecastPoint? persistentCriticalGust = FindConsecutiveMatching(horizon, 3, localNow, 2, hp => hp.WindGustsKmh >= 75.0);
            bool currentSustainedGust = w.WindGustsKmh >= 90.0 ||
                (w.WindGustsKmh >= 75.0 && (horizon.Count == 0 || FindFirstMatching(horizon, 2, localNow, hp => hp.WindGustsKmh >= 65.0) is not null));
            if (currentSustainedGust || persistentCriticalGust is not null)
            {
                double gustVal = currentSustainedGust ? w.WindGustsKmh : persistentCriticalGust!.WindGustsKmh;
                string gustStr = $"{Math.Round(gustVal).ToString("0", CultureInfo.InvariantCulture)} km/h";
                string title = LocalizationService.T(normalizedLang, "alert_strong_gusts");
                string when = currentSustainedGust
                    ? LocalizationService.T(normalizedLang, "alert_when_now")
                    : LocalizationService.Format(normalizedLang, "alert_when_at", persistentCriticalGust!.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture));
                string msg = LocalizationService.Format(normalizedLang, "alert_red_gusts", gustStr, when);
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, gustStr, msg, false, estimateSource, msg);
            }

            if (w.WeatherCode is 65 or 82 || w.PrecipitationMm >= 15.0)
            {
                string title = LocalizationService.T(normalizedLang, "alert_cloudburst");
                string msg = LocalizationService.T(normalizedLang, "alert_red_cloudburst_now");
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, nowLabel, msg, false, estimateSource, msg);
            }

            HourlyForecastPoint? persistentCloudburst = FindConsecutiveMatching(horizon, 3, localNow, 2, hp => hp.PrecipitationMm >= 10.0);
            if (persistentCloudburst is not null)
            {
                string title = LocalizationService.T(normalizedLang, "alert_cloudburst");
                string hhmm = persistentCloudburst.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                string msg = LocalizationService.Format(normalizedLang, "alert_red_cloudburst_at", hhmm);
                return new WeatherAlertInfo(WeatherAlertLevel.Red, title, LocalizationService.Format(normalizedLang, "alert_at", hhmm), msg, false, estimateSource, msg);
            }

            _ = unit;
            return WeatherAlertInfo.None;
        }

        public static string BuildForecastUrl(double lat, double lon, WeatherModelPreference model = WeatherModelPreference.Auto)
        {
            string latStr = lat.ToString("0.####", CultureInfo.InvariantCulture);
            string lonStr = lon.ToString("0.####", CultureInfo.InvariantCulture);
            string modelParam = GetOpenMeteoModelParam(model);
            return $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,wind_direction_10m,relative_humidity_2m,uv_index,wind_gusts_10m,precipitation&hourly=temperature_2m,weather_code,precipitation_probability,wind_speed_10m,wind_direction_10m,is_day,wind_gusts_10m,precipitation,relative_humidity_2m&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,wind_speed_10m_max,wind_direction_10m_dominant,sunrise,sunset,uv_index_max,wind_gusts_10m_max&timezone=auto&forecast_days=7&models={modelParam}";
        }

        public static string BuildGeocodingUrl(string query, string lang = "it")
        {
            string escaped = Uri.EscapeDataString(query.Trim());
            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            return $"https://geocoding-api.open-meteo.com/v1/search?name={escaped}&count=6&language={normalizedLang}&format=json";
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

        public async Task<List<GeoLocationResult>> SearchCitiesAsync(string query, string lang = "it", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            {
                return new List<GeoLocationResult>();
            }

            string url = BuildGeocodingUrl(query, lang);
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
                snapshot.WindGustsKmh = TryGetDouble(current, "wind_gusts_10m", snapshot.WindSpeedKmh);
                snapshot.WindDirectionDeg = TryGetInt(current, "wind_direction_10m", 0);
                snapshot.RelativeHumidity = TryGetInt(current, "relative_humidity_2m", 0);
                snapshot.PrecipitationMm = TryGetFlexibleDouble(current, "precipitation", 0.0);
                snapshot.CurrentUvIndex = TryGetDouble(current, "uv_index", 0.0);
            }

            if (root.TryGetProperty("hourly", out JsonElement hourly))
            {
                var times = GetStringArray(hourly, "time");
                var temps = GetDoubleArray(hourly, "temperature_2m");
                var codes = GetIntArray(hourly, "weather_code");
                var probs = GetIntArray(hourly, "precipitation_probability");
                var precips = GetDoubleArray(hourly, "precipitation");
                var humidities = GetIntArray(hourly, "relative_humidity_2m");
                var winds = GetDoubleArray(hourly, "wind_speed_10m");
                var gusts = GetDoubleArray(hourly, "wind_gusts_10m");
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

                    double hrWind = i < winds.Count ? winds[i] : snapshot.WindSpeedKmh;
                    var point = new HourlyForecastPoint
                    {
                        LocalTime = parsedTime,
                        TemperatureC = i < temps.Count ? temps[i] : snapshot.TemperatureC,
                        WeatherCode = i < codes.Count ? codes[i] : snapshot.WeatherCode,
                        PrecipProbability = i < probs.Count ? probs[i] : 0,
                        PrecipitationMm = i < precips.Count ? precips[i] : 0.0,
                        RelativeHumidity = i < humidities.Count ? Math.Clamp(humidities[i], 0, 100) : snapshot.RelativeHumidity,
                        WindSpeedKmh = hrWind,
                        WindGustsKmh = i < gusts.Count ? gusts[i] : hrWind,
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
                var maxGusts = GetDoubleArray(daily, "wind_gusts_10m_max");
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

                    double dayWind = i < maxWinds.Count ? maxWinds[i] : snapshot.WindSpeedKmh;
                    DateTime? daySunrise = i < sunrises.Count &&
                        DateTime.TryParseExact(sunrises[i], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDaySunrise)
                        ? parsedDaySunrise
                        : null;
                    DateTime? daySunset = i < sunsets.Count &&
                        DateTime.TryParseExact(sunsets[i], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDaySunset)
                        ? parsedDaySunset
                        : null;

                    var dayPoint = new DailyForecastPoint
                    {
                        Date = parsedDate,
                        WeatherCode = i < codes.Count ? codes[i] : snapshot.WeatherCode,
                        MaxC = i < maxTemps.Count ? maxTemps[i] : snapshot.TemperatureC,
                        MinC = i < minTemps.Count ? minTemps[i] : snapshot.TemperatureC,
                        PrecipProbabilityMax = i < probs.Count ? probs[i] : 0,
                        WindSpeedMaxKmh = dayWind,
                        WindGustsMaxKmh = i < maxGusts.Count ? maxGusts[i] : dayWind,
                        WindDirectionDeg = i < windDirs.Count ? windDirs[i] : snapshot.WindDirectionDeg,
                        SunriseLocal = daySunrise,
                        SunsetLocal = daySunset
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
