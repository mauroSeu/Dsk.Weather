using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dsk.Weather.Services
{
    /// <summary>
    /// Generatore procedurale 100% nativo WPF di texture atmosferiche seamless (nuvole fBm volumetriche,
    /// veli di nebbia organica e fasci di luce crepuscolare) in formato Pbgra32 congelate (.Freeze()).
    /// </summary>
    internal static class ProceduralAtmosphereFactory
    {
        private static readonly ConcurrentDictionary<string, BitmapSource> TextureCache =
            new ConcurrentDictionary<string, BitmapSource>(StringComparer.Ordinal);

        public static BitmapSource BuildCloudLayerBitmap(
            int width,
            int height,
            bool isLightTheme,
            bool isNight,
            bool isStorm,
            int seed,
            double coverageThreshold)
        {
            width = Math.Clamp(width, 32, 1024);
            height = Math.Clamp(height, 16, 512);
            double clampedCov = Math.Clamp(coverageThreshold, 0.05, 0.85);

            string key = string.Create(
                CultureInfo.InvariantCulture,
                $"cloud:{width}x{height}:{isLightTheme}:{isNight}:{isStorm}:{seed}:{clampedCov:F3}");

            return TextureCache.GetOrAdd(key, _ => RenderCloudLayer(width, height, isLightTheme, isNight, isStorm, seed, clampedCov));
        }

        public static BitmapSource BuildFogLayerBitmap(
            int width,
            int height,
            bool isLightTheme,
            int seed)
        {
            width = Math.Clamp(width, 32, 1024);
            height = Math.Clamp(height, 16, 512);

            string key = string.Create(
                CultureInfo.InvariantCulture,
                $"fog:{width}x{height}:{isLightTheme}:{seed}");

            return TextureCache.GetOrAdd(key, _ => RenderFogLayer(width, height, isLightTheme, seed));
        }

        public static BitmapSource BuildSunRaysBitmap(
            int width,
            int height,
            bool isLightTheme,
            int seed)
        {
            width = Math.Clamp(width, 32, 1024);
            height = Math.Clamp(height, 32, 1024);

            string key = string.Create(
                CultureInfo.InvariantCulture,
                $"sunrays:{width}x{height}:{isLightTheme}:{seed}");

            return TextureCache.GetOrAdd(key, _ => RenderSunRays(width, height, isLightTheme, seed));
        }

        private static BitmapSource RenderCloudLayer(
            int width,
            int height,
            bool isLightTheme,
            bool isNight,
            bool isStorm,
            int seed,
            double coverageThreshold)
        {
            (byte hiR, byte hiG, byte hiB, byte shR, byte shG, byte shB) = GetCloudPalette(isLightTheme, isNight, isStorm);

            byte[] pixels = new byte[width * height * 4];
            double invWidth = 1.0 / width;
            double invHeightMinus1 = 1.0 / Math.Max(1, height - 1);

            for (int y = 0; y < height; y++)
            {
                double ny = y * invHeightMinus1;
                double topFade = SmoothStep(0.02, 0.30, ny);
                double bottomFade = 1.0 - SmoothStep(0.58, 0.98, ny);
                double vMask = topFade * bottomFade;
                int rowOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    // Divisione per width (non width - 1) per garantire tiling seamless perfetto su X
                    double nx = x * invWidth;

                    double warpX = (FbmTileable(nx, ny, 2, 2.2, seed + 19, 2) - 0.5) * 0.095;
                    double warpY = (FbmTileable(nx + 0.37, ny + 0.19, 2, 2.2, seed + 53, 2) - 0.5) * 0.095;

                    double density = FbmTileable(nx + warpX, ny + warpY, 4, 2.6, seed, 4) * vMask;
                    double alphaNorm = SmoothStep(coverageThreshold, coverageThreshold + 0.34, density);

                    if (alphaNorm <= 0.002)
                    {
                        continue;
                    }

                    double nyUp = Math.Max(0.0, ny - 0.075);
                    double topFadeUp = SmoothStep(0.02, 0.30, nyUp);
                    double bottomFadeUp = 1.0 - SmoothStep(0.58, 0.98, nyUp);
                    double vMaskUp = topFadeUp * bottomFadeUp;
                    double densityUp = FbmTileable(nx + warpX, nyUp + warpY, 4, 2.6, seed, 3) * vMaskUp;

                    double lit = Math.Clamp(0.50 + (density - densityUp) * 2.1 + (1.0 - ny) * 0.35, 0.0, 1.0);

                    byte r = (byte)Math.Round(shR + (hiR - shR) * lit);
                    byte g = (byte)Math.Round(shG + (hiG - shG) * lit);
                    byte b = (byte)Math.Round(shB + (hiB - shB) * lit);
                    byte a = (byte)Math.Clamp((int)Math.Round(alphaNorm * 242.0), 0, 255);

                    int idx = rowOffset + x * 4;
                    WritePbgra(pixels, idx, b, g, r, a);
                }
            }

            return CreateFrozenBitmap(width, height, pixels);
        }

        private static (byte HiR, byte HiG, byte HiB, byte ShR, byte ShG, byte ShB) GetCloudPalette(
            bool isLightTheme,
            bool isNight,
            bool isStorm)
        {
            if (isStorm)
            {
                return isLightTheme
                    ? ((byte)0x94, (byte)0xA3, (byte)0xB8, (byte)0x47, (byte)0x55, (byte)0x69)
                    : ((byte)0xCB, (byte)0xD5, (byte)0xE1, (byte)0x1E, (byte)0x29, (byte)0x3B);
            }

            if (isNight)
            {
                return isLightTheme
                    ? ((byte)0xE2, (byte)0xE8, (byte)0xF0, (byte)0x64, (byte)0x74, (byte)0x8B)
                    : ((byte)0xE2, (byte)0xE8, (byte)0xF0, (byte)0x33, (byte)0x41, (byte)0x55);
            }

            return isLightTheme
                ? ((byte)0xFF, (byte)0xFF, (byte)0xFF, (byte)0x94, (byte)0xA3, (byte)0xB8)
                : ((byte)0xF8, (byte)0xFA, (byte)0xFC, (byte)0x64, (byte)0x74, (byte)0x8B);
        }

        private static BitmapSource RenderFogLayer(int width, int height, bool isLightTheme, int seed)
        {
            byte baseR = isLightTheme ? (byte)0x64 : (byte)0xE2;
            byte baseG = isLightTheme ? (byte)0x74 : (byte)0xE8;
            byte baseB = isLightTheme ? (byte)0x8B : (byte)0xF0;

            byte[] pixels = new byte[width * height * 4];
            double invWidth = 1.0 / width;
            double invHeightMinus1 = 1.0 / Math.Max(1, height - 1);

            for (int y = 0; y < height; y++)
            {
                double ny = y * invHeightMinus1;
                double vMask = SmoothStep(0.05, 0.35, ny) * (1.0 - SmoothStep(0.65, 0.95, ny));
                if (vMask <= 0.002)
                {
                    continue;
                }

                int rowOffset = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    double nx = x * invWidth;
                    double warpX = (FbmTileable(nx, ny, 2, 3.0, seed + 11, 2) - 0.5) * 0.08;
                    double fbm = FbmTileable(nx + warpX, ny, 2, 5.5, seed, 4);
                    double alphaNorm = SmoothStep(0.24, 0.78, fbm) * vMask;
                    if (alphaNorm <= 0.002)
                    {
                        continue;
                    }

                    byte a = (byte)Math.Clamp((int)Math.Round(alphaNorm * (isLightTheme ? 185.0 : 165.0)), 0, 255);
                    WritePbgra(pixels, rowOffset + x * 4, baseB, baseG, baseR, a);
                }
            }

            return CreateFrozenBitmap(width, height, pixels);
        }

        private static BitmapSource RenderSunRays(int width, int height, bool isLightTheme, int seed)
        {
            byte[] pixels = new byte[width * height * 4];
            double invWidthMinus1 = 1.0 / Math.Max(1, width - 1);
            double invHeightMinus1 = 1.0 / Math.Max(1, height - 1);
            double aspect = (double)width / Math.Max(1, height);

            const double ox = 0.88;
            const double oy = 0.08;

            double p1 = ((seed * 37) % 628) * 0.01;
            double p2 = ((seed * 73 + 17) % 628) * 0.01;
            double p3 = ((seed * 113 + 41) % 628) * 0.01;

            for (int y = 0; y < height; y++)
            {
                double ny = y * invHeightMinus1;
                double bottomFade = 1.0 - SmoothStep(0.70, 0.99, ny);
                if (bottomFade <= 0.002)
                {
                    continue;
                }

                double dy = ny - oy;
                int rowOffset = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    double nx = x * invWidthMinus1;
                    double leftFade = SmoothStep(0.0, 0.24, nx);
                    if (leftFade <= 0.002)
                    {
                        continue;
                    }

                    double dx = (nx - ox) * aspect;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double theta = Math.Atan2(dy, -dx);

                    double h1 = 0.5 + 0.5 * Math.Sin(theta * 5.0 + p1);
                    double h2 = 0.5 + 0.5 * Math.Sin(theta * 9.0 + p2);
                    double h3 = 0.5 + 0.5 * Math.Sin(theta * 14.0 + p3);
                    double beam = Math.Pow(Math.Clamp(h1 * 0.52 + h2 * 0.30 + h3 * 0.18, 0.0, 1.0), 1.7);

                    double radial = Math.Exp(-dist * 2.15);
                    double intensity = Math.Clamp((0.22 + 0.78 * beam) * radial * leftFade * bottomFade, 0.0, 1.0);
                    if (intensity <= 0.003)
                    {
                        continue;
                    }

                    double warmth = Math.Clamp(dist * 1.1, 0.0, 1.0);
                    byte r = 0xFF;
                    byte g = (byte)Math.Round(0xE0 + (0xB3 - 0xE0) * warmth);
                    byte b = (byte)Math.Round(0x82 + (0x00 - 0x82) * warmth);
                    byte a = (byte)Math.Clamp((int)Math.Round(intensity * (isLightTheme ? 155.0 : 140.0)), 0, 255);

                    WritePbgra(pixels, rowOffset + x * 4, b, g, r, a);
                }
            }

            return CreateFrozenBitmap(width, height, pixels);
        }

        private static BitmapSource CreateFrozenBitmap(int width, int height, byte[] pixels)
        {
            var bmp = BitmapSource.Create(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Pbgra32,
                null,
                pixels,
                width * 4);
            bmp.Freeze();
            return bmp;
        }

        private static void WritePbgra(byte[] pixels, int idx, byte b, byte g, byte r, byte a)
        {
            // Premoltiplicazione esatta Pbgra32 (B, G, R <= A) per DirectX/WPF
            pixels[idx] = (byte)((b * a + 127) / 255);
            pixels[idx + 1] = (byte)((g * a + 127) / 255);
            pixels[idx + 2] = (byte)((r * a + 127) / 255);
            pixels[idx + 3] = a;
        }

        private static double FbmTileable(double nx, double ny, int basePeriodX, double scaleY, int seed, int octaves)
        {
            double sum = 0.0;
            double amplitude = 1.0;
            double maxAmplitude = 0.0;
            int periodX = Math.Max(1, basePeriodX);
            double curScaleY = scaleY;

            for (int oct = 0; oct < octaves; oct++)
            {
                sum += amplitude * TileableValueNoise2D(nx * periodX, ny * curScaleY, periodX, seed + oct * 97);
                maxAmplitude += amplitude;
                amplitude *= 0.5;
                periodX <<= 1;
                curScaleY *= 2.0;
            }

            return maxAmplitude > 0.0 ? sum / maxAmplitude : 0.0;
        }

        private static double TileableValueNoise2D(double px, double py, int periodX, int seed)
        {
            int x0 = (int)Math.Floor(px);
            int y0 = (int)Math.Floor(py);
            double fx = px - x0;
            double fy = py - y0;

            double ux = Quintic(fx);
            double uy = Quintic(fy);

            int ix0 = ModPositive(x0, periodX);
            int ix1 = ModPositive(x0 + 1, periodX);
            int iy0 = y0;
            int iy1 = y0 + 1;

            double n00 = Hash01(ix0, iy0, seed);
            double n10 = Hash01(ix1, iy0, seed);
            double n01 = Hash01(ix0, iy1, seed);
            double n11 = Hash01(ix1, iy1, seed);

            double nx0 = n00 + (n10 - n00) * ux;
            double nx1 = n01 + (n11 - n01) * ux;
            return nx0 + (nx1 - nx0) * uy;
        }

        private static int ModPositive(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        private static double Quintic(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Clamp((x - edge0) / Math.Max(1e-6, edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        private static double Hash01(int ix, int iy, int seed)
        {
            unchecked
            {
                uint h = (uint)ix * 374761393u + (uint)iy * 668265263u + (uint)seed * 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777216.0;
            }
        }
    }
}
