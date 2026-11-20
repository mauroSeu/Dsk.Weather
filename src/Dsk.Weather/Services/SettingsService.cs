using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsk.Weather.Models;

namespace Dsk.Weather.Services
{
    public sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _dataDirectory;
        private readonly string _settingsFilePath;
        private readonly string _weatherCacheFilePath;

        public SettingsService(string? customDataDirectory = null)
        {
            if (!string.IsNullOrWhiteSpace(customDataDirectory))
            {
                _dataDirectory = customDataDirectory;
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _dataDirectory = Path.Combine(localAppData, "Dsk.Weather");
            }

            _settingsFilePath = Path.Combine(_dataDirectory, "settings.json");
            _weatherCacheFilePath = Path.Combine(_dataDirectory, "weather_cache.json");
        }

        public string DataDirectory => _dataDirectory;

        public WidgetSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    var defaults = new WidgetSettings();
                    defaults.Normalize();
                    return defaults;
                }

                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<WidgetSettings>(json, JsonOptions);
                if (loaded is null)
                {
                    var defaults = new WidgetSettings();
                    defaults.Normalize();
                    return defaults;
                }

                loaded.Normalize();
                return loaded;
            }
            catch
            {
                var defaults = new WidgetSettings();
                defaults.Normalize();
                return defaults;
            }
        }

        public void Save(WidgetSettings settings)
        {
            if (settings is null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Normalize();
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            WriteFileAtomic(_settingsFilePath, json);
        }

        public WeatherSnapshot? LoadWeatherCache()
        {
            try
            {
                if (!File.Exists(_weatherCacheFilePath))
                {
                    return null;
                }

                string json = File.ReadAllText(_weatherCacheFilePath);
                return JsonSerializer.Deserialize<WeatherSnapshot>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public void SaveWeatherCache(WeatherSnapshot snapshot)
        {
            if (snapshot is null)
            {
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                WriteFileAtomic(_weatherCacheFilePath, json);
            }
            catch
            {
                // Errore non critico sulla scrittura cache
            }
        }

        public static (double Left, double Top) ClampPosition(
            double left,
            double top,
            double width,
            double height,
            double virtualLeft,
            double virtualTop,
            double virtualWidth,
            double virtualHeight)
        {
            double safeWidth = double.IsNaN(width) || width <= 0 ? 340.0 : width;
            double safeHeight = double.IsNaN(height) || height <= 0 ? 220.0 : height;
            double safeVWidth = double.IsNaN(virtualWidth) || virtualWidth <= 0 ? 1920.0 : virtualWidth;
            double safeVHeight = double.IsNaN(virtualHeight) || virtualHeight <= 0 ? 1080.0 : virtualHeight;

            double maxLeft = virtualLeft + Math.Max(0.0, safeVWidth - safeWidth);
            double maxTop = virtualTop + Math.Max(0.0, safeVHeight - safeHeight);

            double clampedLeft = double.IsNaN(left) || double.IsInfinity(left)
                ? virtualLeft + 48.0
                : Math.Clamp(left, virtualLeft, maxLeft);

            double clampedTop = double.IsNaN(top) || double.IsInfinity(top)
                ? virtualTop + 48.0
                : Math.Clamp(top, virtualTop, maxTop);

            return (clampedLeft, clampedTop);
        }

        private void WriteFileAtomic(string targetPath, string content)
        {
            Directory.CreateDirectory(_dataDirectory);
            string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content);
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        File.Move(tempPath, targetPath, overwrite: true);
                        return;
                    }
                    catch (IOException) when (attempt < 3)
                    {
                        System.Threading.Thread.Sleep(35 * attempt);
                    }
                    catch (UnauthorizedAccessException) when (attempt < 3)
                    {
                        System.Threading.Thread.Sleep(35 * attempt);
                    }
                }
            }
            catch
            {
                // Evita crash UI se il file è temporaneamente bloccato da antivirus o indicizzatore
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Ignora pulizia file temporaneo
                }
            }
        }

        public void LogError(string context, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                string logPath = Path.Combine(_dataDirectory, "error.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(logPath, line);
            }
            catch
            {
                // Ignora errori di scrittura log
            }
        }
    }
}
