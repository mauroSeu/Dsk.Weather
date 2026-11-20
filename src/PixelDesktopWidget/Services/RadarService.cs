using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelDesktopWidget.Services
{
    public sealed record RadarFrameInfo(DateTime TimestampUtc, string Path, bool IsForecast = false);

    public sealed record RadarTimeline(string Host, List<RadarFrameInfo> Frames);

    public sealed record RadarTilePlacement(int X, int Y, double CanvasLeft, double CanvasTop);

    public sealed record RadarViewportPlan(int Zoom, List<RadarTilePlacement> Tiles, double MarkerX, double MarkerY);

    public sealed class RadarService
    {
        private const string WeatherMapsApiUrl = "https://api.rainviewer.com/public/weather-maps.json";
        private const int TileSize = 256;
        private const int MaxBasemapCache = 64;
        private const int MaxRadarCache = 128;

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, BitmapSource> _basemapCache = new Dictionary<string, BitmapSource>();
        private readonly Queue<string> _basemapOrder = new Queue<string>();
        private readonly Dictionary<string, BitmapSource> _radarCache = new Dictionary<string, BitmapSource>();
        private readonly Queue<string> _radarOrder = new Queue<string>();

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PixelDesktopWidget/1.2 (Windows 11 Desktop Weather Widget; OpenSource)");
            return client;
        }

        public static (double TileX, double TileY) LatLonToTileExact(double lat, double lon, int zoom)
        {
            int clampedZoom = Math.Clamp(zoom, 0, 18);
            double clampedLat = Math.Clamp(lat, -85.05112878, 85.05112878);
            double n = Math.Pow(2.0, clampedZoom);

            double x = ((lon + 180.0) / 360.0) * n;
            double latRad = clampedLat * Math.PI / 180.0;
            double y = (1.0 - (Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI)) / 2.0 * n;

            return (x, y);
        }

        public static int ComputeRadiusKm(double lat, int zoom, double radiusPixels)
        {
            int clampedZoom = Math.Clamp(zoom, 5, 8);
            double latRad = Math.Clamp(lat, -80.0, 80.0) * Math.PI / 180.0;
            double metersPerPixel = (156543.03392 * Math.Cos(latRad)) / (1 << clampedZoom);
            double km = (radiusPixels * metersPerPixel) / 1000.0;
            return Math.Max(1, (int)Math.Round(km));
        }

        public static RadarViewportPlan ComputeTileGrid(
            double lat,
            double lon,
            int zoom,
            int viewportWidth,
            int viewportHeight)
        {
            int clampedZoom = Math.Clamp(zoom, 5, 8);
            int maxIndex = 1 << clampedZoom;

            (double centerTileX, double centerTileY) = LatLonToTileExact(lat, lon, clampedZoom);
            double centerPixelX = centerTileX * TileSize;
            double centerPixelY = centerTileY * TileSize;

            double markerX = viewportWidth / 2.0;
            double markerY = viewportHeight / 2.0;

            double topLeftPixelX = centerPixelX - markerX;
            double topLeftPixelY = centerPixelY - markerY;

            int minTileX = (int)Math.Floor(topLeftPixelX / TileSize);
            int maxTileX = (int)Math.Floor((topLeftPixelX + viewportWidth - 1.0) / TileSize);
            int minTileY = (int)Math.Floor(topLeftPixelY / TileSize);
            int maxTileY = (int)Math.Floor((topLeftPixelY + viewportHeight - 1.0) / TileSize);

            var placements = new List<RadarTilePlacement>();
            for (int ty = minTileY; ty <= maxTileY; ty++)
            {
                if (ty < 0 || ty >= maxIndex)
                {
                    continue;
                }

                for (int tx = minTileX; tx <= maxTileX; tx++)
                {
                    int wrappedX = ((tx % maxIndex) + maxIndex) % maxIndex;
                    double left = (tx * TileSize) - topLeftPixelX;
                    double top = (ty * TileSize) - topLeftPixelY;
                    placements.Add(new RadarTilePlacement(wrappedX, ty, left, top));
                }
            }

            return new RadarViewportPlan(clampedZoom, placements, markerX, markerY);
        }

        public async Task<RadarTimeline?> FetchTimelineAsync(CancellationToken ct = default)
        {
            try
            {
                using var response = await SharedHttpClient.GetAsync(WeatherMapsApiUrl, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseTimelineJson(json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        internal static RadarTimeline? ParseTimelineJson(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("host", out JsonElement hostEl) || hostEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string host = (hostEl.GetString() ?? string.Empty).TrimEnd('/');
            if (!host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!root.TryGetProperty("radar", out JsonElement radarEl) ||
                !radarEl.TryGetProperty("past", out JsonElement pastEl) ||
                pastEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var pastFrames = new List<RadarFrameInfo>();
            foreach (JsonElement item in pastEl.EnumerateArray())
            {
                if (!item.TryGetProperty("time", out JsonElement timeEl) || !timeEl.TryGetInt64(out long unixSec))
                {
                    continue;
                }

                if (!item.TryGetProperty("path", out JsonElement pathEl) || pathEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string path = (pathEl.GetString() ?? string.Empty).Trim();
                if (!path.StartsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                DateTime tsUtc = DateTimeOffset.FromUnixTimeSeconds(unixSec).UtcDateTime;
                pastFrames.Add(new RadarFrameInfo(tsUtc, path, IsForecast: false));
            }

            if (pastFrames.Count == 0)
            {
                return null;
            }

            pastFrames.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));

            var forecastFrames = new List<RadarFrameInfo>();
            if (radarEl.TryGetProperty("nowcast", out JsonElement nowcastEl) && nowcastEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in nowcastEl.EnumerateArray())
                {
                    if (!item.TryGetProperty("time", out JsonElement timeEl) || !timeEl.TryGetInt64(out long unixSec))
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("path", out JsonElement pathEl) || pathEl.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string path = (pathEl.GetString() ?? string.Empty).Trim();
                    if (!path.StartsWith("/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    DateTime tsUtc = DateTimeOffset.FromUnixTimeSeconds(unixSec).UtcDateTime;
                    forecastFrames.Add(new RadarFrameInfo(tsUtc, path, IsForecast: true));
                }
                forecastFrames.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));
            }

            var selected = new List<RadarFrameInfo>(8);
            int maxPast = forecastFrames.Count > 0 ? 6 : 8;
            int takePast = Math.Min(maxPast, pastFrames.Count);
            selected.AddRange(pastFrames.GetRange(pastFrames.Count - takePast, takePast));

            int takeForecast = Math.Min(8 - selected.Count, forecastFrames.Count);
            if (takeForecast > 0)
            {
                selected.AddRange(forecastFrames.GetRange(0, takeForecast));
            }

            return new RadarTimeline(host, selected);
        }

        public async Task<BitmapSource?> GetBasemapTileAsync(int zoom, int x, int y, bool darkTheme, CancellationToken ct = default)
        {
            int clampedZoom = Math.Clamp(zoom, 5, 8);
            string style = darkTheme ? "osm_dark" : "osm_light";
            string key = string.Create(CultureInfo.InvariantCulture, $"{style}/{clampedZoom}/{x}/{y}");

            lock (_cacheLock)
            {
                if (_basemapCache.TryGetValue(key, out BitmapSource? cached))
                {
                    return cached;
                }
            }

            // Tile OpenStreetMap ufficiale (zero watermark API KEY, nomi città, strade e coste nitide)
            string url = string.Create(CultureInfo.InvariantCulture, $"https://tile.openstreetmap.org/{clampedZoom}/{x}/{y}.png");
            BitmapImage? rawImage = await DownloadImageAsync(url, ct).ConfigureAwait(false);
            if (rawImage is null)
            {
                return null;
            }

            BitmapSource finalImage = darkTheme ? ConvertToDarkRadarMap(rawImage) : rawImage;

            lock (_cacheLock)
            {
                if (!_basemapCache.ContainsKey(key))
                {
                    if (_basemapOrder.Count >= MaxBasemapCache)
                    {
                        string oldest = _basemapOrder.Dequeue();
                        _basemapCache.Remove(oldest);
                    }
                    _basemapCache[key] = finalImage;
                    _basemapOrder.Enqueue(key);
                }
            }

            return finalImage;
        }

        private static BitmapSource ConvertToDarkRadarMap(BitmapSource src)
        {
            var formatted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                // Rileva acqua (mare, laghi, fiumi in OpenStreetMap: #AAD3DF)
                bool isWater = b > r + 15 && b >= g && r < 200;
                if (isWater)
                {
                    pixels[i] = 48;      // B (#112130)
                    pixels[i + 1] = 33;  // G
                    pixels[i + 2] = 17;  // R
                    continue;
                }

                // Luminanza percepita (0..255)
                int lum = (r * 77 + g * 150 + b * 29) >> 8;

                if (lum < 135)
                {
                    // Testi delle città, confini e scritte scure -> invertiti in argento/bianco molto leggibile
                    double boost = (135 - lum) / 135.0;
                    int val = (int)(70 + (boost * 170));
                    pixels[i] = (byte)Math.Clamp(val + 14, 0, 255);     // B
                    pixels[i + 1] = (byte)Math.Clamp(val + 6, 0, 255);  // G
                    pixels[i + 2] = (byte)Math.Clamp(val, 0, 255);      // R
                }
                else
                {
                    // Terraferma, parchi e aree chiare -> ardesia scura radar (#1E2736 .. #283447)
                    double norm = (lum - 135) / 120.0;
                    int baseVal = (int)(30 + (1.0 - norm) * 20);
                    pixels[i] = (byte)Math.Clamp(baseVal + 16, 28, 68);     // B
                    pixels[i + 1] = (byte)Math.Clamp(baseVal + 7, 22, 56);  // G
                    pixels[i + 2] = (byte)Math.Clamp(baseVal, 18, 46);      // R
                }
            }

            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            wb.Freeze();
            return wb;
        }

        public async Task<BitmapSource?> GetRadarTileAsync(string host, string framePath, int zoom, int x, int y, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(host) || !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(framePath) || !framePath.StartsWith("/", StringComparison.Ordinal))
            {
                return null;
            }

            int clampedZoom = Math.Clamp(zoom, 5, 8);
            string cleanHost = host.TrimEnd('/');
            string key = string.Create(CultureInfo.InvariantCulture, $"{framePath}/{clampedZoom}/{x}/{y}");

            lock (_cacheLock)
            {
                if (_radarCache.TryGetValue(key, out BitmapSource? cached))
                {
                    return cached;
                }
            }

            // Schema colori 2 (Universal Blue) con smoothing 1_1
            string url = string.Create(CultureInfo.InvariantCulture, $"{cleanHost}{framePath}/{TileSize}/{clampedZoom}/{x}/{y}/2/1_1.png");
            BitmapImage? image = await DownloadImageAsync(url, ct).ConfigureAwait(false);
            if (image is not null)
            {
                lock (_cacheLock)
                {
                    if (!_radarCache.ContainsKey(key))
                    {
                        if (_radarOrder.Count >= MaxRadarCache)
                        {
                            string oldest = _radarOrder.Dequeue();
                            _radarCache.Remove(oldest);
                        }
                        _radarCache[key] = image;
                        _radarOrder.Enqueue(key);
                    }
                }
            }

            return image;
        }

        private static async Task<BitmapImage?> DownloadImageAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await SharedHttpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    return null;
                }

                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
    }
}
