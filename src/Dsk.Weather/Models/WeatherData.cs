using System;
using System.Collections.Generic;

namespace Dsk.Weather.Models
{
    public enum WeatherIconKind
    {
        ClearDay,
        ClearNight,
        PartlyCloudyDay,
        PartlyCloudyNight,
        Cloudy,
        Fog,
        Drizzle,
        Rain,
        Snow,
        Thunderstorm
    }

    public enum WeatherAlertLevel
    {
        None,
        Yellow,
        Orange,
        Red
    }

    public sealed record WeatherAlertInfo(
        WeatherAlertLevel Level,
        string Title,
        string Detail,
        string Tooltip,
        bool IsOfficial = false,
        string SourceName = "",
        string Description = "",
        string Certainty = "")
    {
        public static WeatherAlertInfo None { get; } = new WeatherAlertInfo(WeatherAlertLevel.None, string.Empty, string.Empty, string.Empty);
    }

    public sealed class WeatherSnapshot
    {
        public DateTime FetchedAtUtc { get; set; }
        public string CityName { get; set; } = string.Empty;
        public double TemperatureC { get; set; }
        public double ApparentTemperatureC { get; set; }
        public int WeatherCode { get; set; }
        public bool IsDay { get; set; } = true;
        public double WindSpeedKmh { get; set; }
        public double WindGustsKmh { get; set; }
        public int WindDirectionDeg { get; set; }
        public int RelativeHumidity { get; set; }
        public double PrecipitationMm { get; set; }
        public double TodayMaxC { get; set; }
        public double TodayMinC { get; set; }
        public int TodayPrecipProbMax { get; set; }
        public double CurrentUvIndex { get; set; }
        public double TodayUvIndexMax { get; set; }
        public DateTime? SunriseLocal { get; set; }
        public DateTime? SunsetLocal { get; set; }
        public string ModelUsed { get; set; } = "best_match";
        public List<HourlyForecastPoint> NextHours { get; set; } = new List<HourlyForecastPoint>();
        public List<HourlyForecastPoint> Next12Hours { get; set; } = new List<HourlyForecastPoint>();
        public List<DailyForecastPoint> Daily { get; set; } = new List<DailyForecastPoint>();
        public bool IsFromCache { get; set; }
    }

    public sealed class HourlyForecastPoint
    {
        public DateTime LocalTime { get; set; }
        public double TemperatureC { get; set; }
        public int WeatherCode { get; set; }
        public int PrecipProbability { get; set; }
        public double PrecipitationMm { get; set; }
        public double WindSpeedKmh { get; set; }
        public double WindGustsKmh { get; set; }
        public int WindDirectionDeg { get; set; }
        public bool IsDay { get; set; } = true;
    }

    public sealed class DailyForecastPoint
    {
        public DateTime Date { get; set; }
        public int WeatherCode { get; set; }
        public double MaxC { get; set; }
        public double MinC { get; set; }
        public int PrecipProbabilityMax { get; set; }
        public double WindSpeedMaxKmh { get; set; }
        public double WindGustsMaxKmh { get; set; }
        public int WindDirectionDeg { get; set; }
        public DateTime? SunriseLocal { get; set; }
        public DateTime? SunsetLocal { get; set; }
    }

    public sealed class GeoLocationResult
    {
        public string Name { get; set; } = string.Empty;
        public string Admin1 { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public string DisplayName
        {
            get
            {
                var parts = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    parts.Add(Name.Trim());
                }

                if (!string.IsNullOrWhiteSpace(Admin1) && !string.Equals(Admin1.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(Admin1.Trim());
                }

                if (!string.IsNullOrWhiteSpace(Country))
                {
                    parts.Add(Country.Trim());
                }

                return string.Join(", ", parts);
            }
        }
    }
}
