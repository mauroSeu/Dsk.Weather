using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PixelDesktopWidget.Models;

namespace PixelDesktopWidget.Services
{
    public sealed class MaterialColorService
    {
        private const int SpiGetDesktopWallpaper = 0x0073;
        private const int BucketCount = 24;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfoW(int uiAction, int uiParam, StringBuilder pvParam, int fWinIni);

        public string? GetCurrentWallpaperPath()
        {
            try
            {
                var sb = new StringBuilder(520);
                if (SystemParametersInfoW(SpiGetDesktopWallpaper, sb.Capacity, sb, 0))
                {
                    string path = sb.ToString().TrimEnd('\0').Trim();
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Fallback su registro
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                string? regPath = key?.GetValue("WallPaper") as string;
                if (!string.IsNullOrWhiteSpace(regPath) && File.Exists(regPath))
                {
                    return regPath;
                }
            }
            catch
            {
                // Ignora errori di lettura registro
            }

            return null;
        }

        public Color GetWindowsDwmAccentColor()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                object? raw = key?.GetValue("AccentColor");
                if (raw is int intVal)
                {
                    uint u = unchecked((uint)intVal);
                    byte r = (byte)(u & 0xFF);
                    byte g = (byte)((u >> 8) & 0xFF);
                    byte b = (byte)((u >> 16) & 0xFF);
                    return Color.FromRgb(r, g, b);
                }
            }
            catch
            {
                // Fallback colore predefinito Pixel
            }

            return Color.FromRgb(0x4A, 0x90, 0xE2);
        }

        public Color? ExtractSeedFromImageFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using var fs = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = fs;
                bitmap.DecodePixelWidth = 48;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0.0);
                converted.Freeze();

                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                return ExtractDominantSeedFromBgra32(pixels, width * height);
            }
            catch
            {
                return null;
            }
        }

        internal static Color? ExtractDominantSeedFromBgra32(byte[] bgraPixels, int pixelCount)
        {
            if (bgraPixels is null || pixelCount <= 0 || bgraPixels.Length < pixelCount * 4)
            {
                return null;
            }

            double[] bucketWeights = new double[BucketCount];
            double[] bucketR = new double[BucketCount];
            double[] bucketG = new double[BucketCount];
            double[] bucketB = new double[BucketCount];

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 4;
                byte b = bgraPixels[offset];
                byte g = bgraPixels[offset + 1];
                byte r = bgraPixels[offset + 2];
                byte a = bgraPixels[offset + 3];

                if (a < 128)
                {
                    continue;
                }

                (double h, double s, double l) = RgbToHsl(r, g, b);
                if (l < 0.12 || l > 0.90 || s < 0.12)
                {
                    continue;
                }

                double weight = s * Math.Max(0.15, 1.0 - Math.Abs(l - 0.52));
                int bucket = (int)Math.Floor((h / 360.0) * BucketCount) % BucketCount;
                if (bucket < 0)
                {
                    bucket += BucketCount;
                }

                bucketWeights[bucket] += weight;
                bucketR[bucket] += r * weight;
                bucketG[bucket] += g * weight;
                bucketB[bucket] += b * weight;
            }

            int bestBucket = -1;
            double bestWeight = 0.0;
            for (int bIdx = 0; bIdx < BucketCount; bIdx++)
            {
                if (bucketWeights[bIdx] > bestWeight)
                {
                    bestWeight = bucketWeights[bIdx];
                    bestBucket = bIdx;
                }
            }

            if (bestBucket < 0 || bestWeight <= 0.0)
            {
                return null;
            }

            byte avgR = (byte)Math.Clamp(Math.Round(bucketR[bestBucket] / bestWeight), 0, 255);
            byte avgG = (byte)Math.Clamp(Math.Round(bucketG[bestBucket] / bestWeight), 0, 255);
            byte avgB = (byte)Math.Clamp(Math.Round(bucketB[bestBucket] / bestWeight), 0, 255);

            return Color.FromRgb(avgR, avgG, avgB);
        }

        public MaterialThemePalette BuildPalette(WidgetThemeMode mode, Color? dynamicSeedOverride = null)
        {
            switch (mode)
            {
                case WidgetThemeMode.PixelDark:
                    return new MaterialThemePalette
                    {
                        SeedColor = Color.FromRgb(0x8A, 0xB4, 0xF8),
                        Surface = Color.FromRgb(0x16, 0x1B, 0x26),
                        SurfaceVariant = Color.FromRgb(0x21, 0x29, 0x3A),
                        SurfaceBorder = Color.FromRgb(0x36, 0x42, 0x59),
                        Primary = Color.FromRgb(0xA8, 0xC7, 0xFA),
                        PrimaryContainer = Color.FromRgb(0x23, 0x36, 0x54),
                        OnSurface = Color.FromRgb(0xF1, 0xF5, 0xF9),
                        OnSurfaceMuted = Color.FromRgb(0xA6, 0xB4, 0xCA),
                        ShadowColor = Color.FromRgb(0x00, 0x00, 0x00)
                    };

                case WidgetThemeMode.GlassDark:
                    return new MaterialThemePalette
                    {
                        SeedColor = Color.FromRgb(0x7D, 0xD3, 0xFC),
                        Surface = Color.FromRgb(0x0D, 0x13, 0x1E),
                        SurfaceVariant = Color.FromRgb(0x18, 0x22, 0x34),
                        SurfaceBorder = Color.FromRgb(0x47, 0x5B, 0x78),
                        Primary = Color.FromRgb(0x93, 0xC5, 0xFD),
                        PrimaryContainer = Color.FromRgb(0x1E, 0x34, 0x4D),
                        OnSurface = Color.FromRgb(0xF8, 0xFA, 0xFC),
                        OnSurfaceMuted = Color.FromRgb(0xB4, 0xC2, 0xD6),
                        ShadowColor = Color.FromRgb(0x00, 0x00, 0x00)
                    };

                case WidgetThemeMode.MaterialLight:
                    return new MaterialThemePalette
                    {
                        SeedColor = Color.FromRgb(0x1A, 0x73, 0xE8),
                        Surface = Color.FromRgb(0xF3, 0xF6, 0xFC),
                        SurfaceVariant = Color.FromRgb(0xE3, 0xEA, 0xF6),
                        SurfaceBorder = Color.FromRgb(0xC7, 0xD3, 0xE5),
                        Primary = Color.FromRgb(0x1A, 0x73, 0xE8),
                        PrimaryContainer = Color.FromRgb(0xD3, 0xE3, 0xFD),
                        OnSurface = Color.FromRgb(0x16, 0x1D, 0x29),
                        OnSurfaceMuted = Color.FromRgb(0x4E, 0x5D, 0x73),
                        ShadowColor = Color.FromRgb(0x0F, 0x17, 0x2A)
                    };

                case WidgetThemeMode.DynamicWallpaper:
                default:
                    Color seed = dynamicSeedOverride
                                 ?? ExtractSeedFromImageFile(GetCurrentWallpaperPath())
                                 ?? GetWindowsDwmAccentColor();
                    return BuildMonetDarkPalette(seed);
            }
        }

        public static Brush BuildAtmosphereBrush(
            WeatherSnapshot? weather,
            MaterialThemePalette palette,
            WidgetThemeMode themeMode,
            double opacity,
            DateTime localNow)
        {
            byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
            Color baseSurface = palette.Surface;
            bool isLight = themeMode == WidgetThemeMode.MaterialLight;

            Color? accentTint = null;
            double blendFactor = isLight ? 0.14 : 0.22;

            if (weather is not null)
            {
                bool isGoldenHour =
                    (weather.SunsetLocal.HasValue && Math.Abs((weather.SunsetLocal.Value - localNow).TotalMinutes) <= 45.0) ||
                    (weather.SunriseLocal.HasValue && Math.Abs((weather.SunriseLocal.Value - localNow).TotalMinutes) <= 45.0);

                if (weather.WeatherCode >= 95)
                {
                    accentTint = Color.FromRgb(0x63, 0x66, 0xF1);
                    blendFactor = isLight ? 0.16 : 0.24;
                }
                else if ((weather.WeatherCode >= 51 && weather.WeatherCode <= 67) || (weather.WeatherCode >= 80 && weather.WeatherCode <= 82))
                {
                    accentTint = Color.FromRgb(0x25, 0x63, 0xEB);
                    blendFactor = isLight ? 0.15 : 0.23;
                }
                else if ((weather.WeatherCode >= 71 && weather.WeatherCode <= 77) || weather.WeatherCode is 85 or 86)
                {
                    accentTint = Color.FromRgb(0x93, 0xC5, 0xFD);
                    blendFactor = isLight ? 0.18 : 0.20;
                }
                else if (isGoldenHour && weather.WeatherCode <= 2)
                {
                    accentTint = Color.FromRgb(0xF5, 0x9E, 0x0B);
                    blendFactor = isLight ? 0.16 : 0.22;
                }
                else if (weather.WeatherCode <= 1)
                {
                    accentTint = weather.IsDay
                        ? Color.FromRgb(0x38, 0xBD, 0xF8)
                        : Color.FromRgb(0x31, 0x2E, 0x81);
                    blendFactor = isLight ? 0.12 : 0.20;
                }
                else if (weather.WeatherCode is 3 or 45 or 48)
                {
                    accentTint = Color.FromRgb(0x64, 0x74, 0x8B);
                    blendFactor = isLight ? 0.12 : 0.18;
                }
                else
                {
                    accentTint = weather.IsDay
                        ? Color.FromRgb(0x60, 0xA5, 0xFA)
                        : Color.FromRgb(0x3B, 0x4E, 0x7A);
                }
            }

            Color endColor = Color.FromArgb(alpha, baseSurface.R, baseSurface.G, baseSurface.B);
            Color startColor = accentTint.HasValue
                ? LerpColor(baseSurface, accentTint.Value, blendFactor, alpha)
                : endColor;

            var gradient = new LinearGradientBrush(
                startColor,
                endColor,
                new System.Windows.Point(1.0, 0.0),
                new System.Windows.Point(0.0, 1.0));
            gradient.Freeze();
            return gradient;
        }

        private static Color LerpColor(Color from, Color to, double ratio, byte alpha)
        {
            double t = Math.Clamp(ratio, 0.0, 1.0);
            byte r = (byte)Math.Clamp(Math.Round(from.R + (to.R - from.R) * t), 0, 255);
            byte g = (byte)Math.Clamp(Math.Round(from.G + (to.G - from.G) * t), 0, 255);
            byte b = (byte)Math.Clamp(Math.Round(from.B + (to.B - from.B) * t), 0, 255);
            return Color.FromArgb(alpha, r, g, b);
        }

        private static MaterialThemePalette BuildMonetDarkPalette(Color seed)
        {
            (double hue, double sat, _) = RgbToHsl(seed.R, seed.G, seed.B);
            double mutedSat = Math.Clamp(sat * 0.45, 0.14, 0.36);
            double richSat = Math.Clamp(sat * 0.85, 0.35, 0.82);

            Color surface = HslToRgb(hue, mutedSat, 0.12);
            Color surfaceVariant = HslToRgb(hue, mutedSat * 1.1, 0.18);
            Color surfaceBorder = HslToRgb(hue, mutedSat * 0.9, 0.32);
            Color primary = HslToRgb(hue, richSat, 0.78);
            Color primaryContainer = HslToRgb(hue, Math.Clamp(richSat * 0.65, 0.25, 0.55), 0.24);
            Color onSurface = HslToRgb(hue, 0.18, 0.95);
            Color onSurfaceMuted = HslToRgb(hue, 0.22, 0.74);

            return new MaterialThemePalette
            {
                SeedColor = seed,
                Surface = surface,
                SurfaceVariant = surfaceVariant,
                SurfaceBorder = surfaceBorder,
                Primary = primary,
                PrimaryContainer = primaryContainer,
                OnSurface = onSurface,
                OnSurfaceMuted = onSurfaceMuted,
                ShadowColor = Color.FromRgb(0x00, 0x00, 0x00)
            };
        }

        internal static (double H, double S, double L) RgbToHsl(byte rByte, byte gByte, byte bByte)
        {
            double r = rByte / 255.0;
            double g = gByte / 255.0;
            double b = bByte / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double l = (max + min) / 2.0;
            double h = 0.0;
            double s = 0.0;

            if (delta > 1e-6)
            {
                s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

                if (Math.Abs(max - r) < 1e-6)
                {
                    h = ((g - b) / delta) + (g < b ? 6.0 : 0.0);
                }
                else if (Math.Abs(max - g) < 1e-6)
                {
                    h = ((b - r) / delta) + 2.0;
                }
                else
                {
                    h = ((r - g) / delta) + 4.0;
                }

                h *= 60.0;
                if (h < 0.0)
                {
                    h += 360.0;
                }
            }

            return (h, s, l);
        }

        internal static Color HslToRgb(double h, double s, double l)
        {
            double normalizedH = ((h % 360.0) + 360.0) % 360.0 / 360.0;
            double clampedS = Math.Clamp(s, 0.0, 1.0);
            double clampedL = Math.Clamp(l, 0.0, 1.0);

            if (clampedS < 1e-6)
            {
                byte v = (byte)Math.Round(clampedL * 255.0);
                return Color.FromRgb(v, v, v);
            }

            double q = clampedL < 0.5
                ? clampedL * (1.0 + clampedS)
                : clampedL + clampedS - (clampedL * clampedS);
            double p = 2.0 * clampedL - q;

            double r = HueToChannel(p, q, normalizedH + (1.0 / 3.0));
            double g = HueToChannel(p, q, normalizedH);
            double b = HueToChannel(p, q, normalizedH - (1.0 / 3.0));

            return Color.FromRgb(
                (byte)Math.Clamp(Math.Round(r * 255.0), 0, 255),
                (byte)Math.Clamp(Math.Round(g * 255.0), 0, 255),
                (byte)Math.Clamp(Math.Round(b * 255.0), 0, 255));
        }

        private static double HueToChannel(double p, double q, double t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }
}
