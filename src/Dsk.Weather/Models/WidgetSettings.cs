using System;
using System.Collections.Generic;

namespace Dsk.Weather.Models
{
    public enum WidgetLayoutStyle
    {
        PixelCard,
        PixelMinimalTransparent,
        MaterialCompact
    }

    public enum WidgetThemeMode
    {
        DynamicWallpaper,
        PixelDark,
        GlassDark,
        MaterialLight
    }

    public enum WindowPinMode
    {
        DesktopPinned,
        BottomMost,
        AlwaysOnTop
    }

    public enum TemperatureUnit
    {
        Celsius,
        Fahrenheit
    }

    public enum WeatherModelPreference
    {
        Auto,
        EcmwfIfs,
        DwdIcon
    }

    public sealed class SavedLocation
    {
        public string Name { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public string DisplayLabel => string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}";

        public bool Matches(string name, double lat, double lon)
        {
            bool sameCoords = Math.Abs(Latitude - lat) < 0.02 && Math.Abs(Longitude - lon) < 0.02;
            bool sameName = string.Equals(Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase);
            return sameCoords || sameName;
        }

        public SavedLocation Clone()
        {
            return new SavedLocation
            {
                Name = Name,
                Country = Country,
                Latitude = Latitude,
                Longitude = Longitude
            };
        }
    }

    public sealed class WidgetSettings
    {
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public WidgetLayoutStyle LayoutStyle { get; set; } = WidgetLayoutStyle.PixelCard;
        public WidgetThemeMode ThemeMode { get; set; } = WidgetThemeMode.DynamicWallpaper;
        public WindowPinMode PinMode { get; set; } = WindowPinMode.DesktopPinned;
        public double Scale { get; set; } = 1.0;
        public double BackgroundOpacity { get; set; } = 0.80;
        public bool Use24HourFormat { get; set; } = true;
        public bool ShowSeconds { get; set; } = false;
        public bool ShowHourlyStrip { get; set; } = true;
        public bool LockPosition { get; set; } = false;
        public TemperatureUnit TempUnit { get; set; } = TemperatureUnit.Celsius;
        public bool EnableDynamicAtmosphere { get; set; } = true;
        public bool EnableWeatherAnimations { get; set; } = true;
        public bool EnableWeatherAlerts { get; set; } = true;
        public string Language { get; set; } = "it";
        public int RadarZoom { get; set; } = 7;
        public string CityName { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public double Latitude { get; set; } = 41.9028;
        public double Longitude { get; set; } = 12.4964;
        public bool LocationInitialized { get; set; } = false;
        public List<SavedLocation> FavoriteLocations { get; set; } = new List<SavedLocation>();

        public void Normalize()
        {
            Scale = Math.Clamp(double.IsNaN(Scale) || double.IsInfinity(Scale) ? 1.0 : Scale, 0.75, 1.50);
            BackgroundOpacity = Math.Clamp(double.IsNaN(BackgroundOpacity) || double.IsInfinity(BackgroundOpacity) ? 0.80 : BackgroundOpacity, 0.0, 1.0);
            RadarZoom = Math.Clamp(RadarZoom == 0 ? 7 : RadarZoom, 5, 8);

            if (double.IsNaN(Latitude) || double.IsInfinity(Latitude))
            {
                Latitude = 41.9028;
            }
            else
            {
                Latitude = Math.Clamp(Latitude, -90.0, 90.0);
            }

            if (double.IsNaN(Longitude) || double.IsInfinity(Longitude))
            {
                Longitude = 12.4964;
            }
            else
            {
                Longitude = Math.Clamp(Longitude, -180.0, 180.0);
            }

            Language = Dsk.Weather.Services.LocalizationService.NormalizeLanguage(Language);
            CityName ??= string.Empty;
            CountryName ??= string.Empty;

            FavoriteLocations ??= new List<SavedLocation>();
            var cleaned = new List<SavedLocation>(FavoriteLocations.Count);
            foreach (SavedLocation loc in FavoriteLocations)
            {
                if (loc is null || string.IsNullOrWhiteSpace(loc.Name))
                {
                    continue;
                }

                double lat = double.IsNaN(loc.Latitude) || double.IsInfinity(loc.Latitude) ? 41.9028 : Math.Clamp(loc.Latitude, -90.0, 90.0);
                double lon = double.IsNaN(loc.Longitude) || double.IsInfinity(loc.Longitude) ? 12.4964 : Math.Clamp(loc.Longitude, -180.0, 180.0);
                string name = loc.Name.Trim();
                string country = (loc.Country ?? string.Empty).Trim();

                bool exists = false;
                foreach (SavedLocation existing in cleaned)
                {
                    if (existing.Matches(name, lat, lon))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    cleaned.Add(new SavedLocation
                    {
                        Name = name,
                        Country = country,
                        Latitude = lat,
                        Longitude = lon
                    });
                }
            }

            if (cleaned.Count == 0 && !string.IsNullOrWhiteSpace(CityName))
            {
                cleaned.Add(new SavedLocation
                {
                    Name = CityName.Trim(),
                    Country = CountryName.Trim(),
                    Latitude = Latitude,
                    Longitude = Longitude
                });
            }

            FavoriteLocations = cleaned;
        }

        public void AddOrSelectLocation(string name, string country, double lat, double lon)
        {
            string cleanName = (name ?? string.Empty).Trim();
            string cleanCountry = (country ?? string.Empty).Trim();
            double cleanLat = Math.Clamp(lat, -90.0, 90.0);
            double cleanLon = Math.Clamp(lon, -180.0, 180.0);

            CityName = cleanName;
            CountryName = cleanCountry;
            Latitude = cleanLat;
            Longitude = cleanLon;
            LocationInitialized = true;

            FavoriteLocations ??= new List<SavedLocation>();
            foreach (SavedLocation loc in FavoriteLocations)
            {
                if (loc.Matches(cleanName, cleanLat, cleanLon))
                {
                    return;
                }
            }

            FavoriteLocations.Add(new SavedLocation
            {
                Name = cleanName,
                Country = cleanCountry,
                Latitude = cleanLat,
                Longitude = cleanLon
            });
        }

        public SavedLocation? PeekNextFavorite()
        {
            if (FavoriteLocations is null || FavoriteLocations.Count < 2)
            {
                return null;
            }

            int currentIndex = -1;
            for (int i = 0; i < FavoriteLocations.Count; i++)
            {
                if (FavoriteLocations[i].Matches(CityName, Latitude, Longitude))
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = currentIndex >= 0
                ? (currentIndex + 1) % FavoriteLocations.Count
                : 0;
            return FavoriteLocations[nextIndex];
        }

        public SavedLocation? CycleNextFavorite()
        {
            SavedLocation? next = PeekNextFavorite();
            if (next is null)
            {
                return null;
            }

            AddOrSelectLocation(next.Name, next.Country, next.Latitude, next.Longitude);
            return next;
        }

        public bool IsDefaultStyle()
        {
            return LayoutStyle == WidgetLayoutStyle.PixelCard
                && ThemeMode == WidgetThemeMode.DynamicWallpaper
                && Math.Abs(Scale - 1.0) < 0.01
                && Math.Abs(BackgroundOpacity - 0.80) < 0.01
                && EnableDynamicAtmosphere
                && EnableWeatherAnimations;
        }

        public void ResetStyleToDefaults()
        {
            LayoutStyle = WidgetLayoutStyle.PixelCard;
            ThemeMode = WidgetThemeMode.DynamicWallpaper;
            Scale = 1.0;
            BackgroundOpacity = 0.80;
            EnableDynamicAtmosphere = true;
            EnableWeatherAnimations = true;
        }

        public WidgetSettings Clone()
        {
            var clonedFavorites = new List<SavedLocation>(FavoriteLocations?.Count ?? 0);
            if (FavoriteLocations is not null)
            {
                foreach (SavedLocation loc in FavoriteLocations)
                {
                    clonedFavorites.Add(loc.Clone());
                }
            }

            return new WidgetSettings
            {
                Left = Left,
                Top = Top,
                LayoutStyle = LayoutStyle,
                ThemeMode = ThemeMode,
                PinMode = PinMode,
                Scale = Scale,
                BackgroundOpacity = BackgroundOpacity,
                Use24HourFormat = Use24HourFormat,
                ShowSeconds = ShowSeconds,
                ShowHourlyStrip = ShowHourlyStrip,
                LockPosition = LockPosition,
                TempUnit = TempUnit,
                EnableDynamicAtmosphere = EnableDynamicAtmosphere,
                EnableWeatherAnimations = EnableWeatherAnimations,
                EnableWeatherAlerts = EnableWeatherAlerts,
                Language = Language,
                RadarZoom = RadarZoom,
                CityName = CityName,
                CountryName = CountryName,
                Latitude = Latitude,
                Longitude = Longitude,
                LocationInitialized = LocationInitialized,
                FavoriteLocations = clonedFavorites
            };
        }
    }
}
