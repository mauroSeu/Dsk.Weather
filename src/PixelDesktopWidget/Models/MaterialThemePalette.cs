using System;
using System.Windows.Media;

namespace PixelDesktopWidget.Models
{
    public sealed class MaterialThemePalette
    {
        public Color SeedColor { get; set; }
        public Color Surface { get; set; }
        public Color SurfaceVariant { get; set; }
        public Color SurfaceBorder { get; set; }
        public Color Primary { get; set; }
        public Color PrimaryContainer { get; set; }
        public Color OnSurface { get; set; }
        public Color OnSurfaceMuted { get; set; }
        public Color ShadowColor { get; set; }

        public static SolidColorBrush ToBrush(Color color, double alphaOverride = -1.0)
        {
            Color target = color;
            if (alphaOverride >= 0.0)
            {
                double clamped = Math.Clamp(alphaOverride, 0.0, 1.0);
                byte a = (byte)Math.Round(clamped * 255.0);
                target = Color.FromArgb(a, color.R, color.G, color.B);
            }

            var brush = new SolidColorBrush(target);
            brush.Freeze();
            return brush;
        }
    }
}
