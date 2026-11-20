using System;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace Dsk.Weather.Icons
{
    public static class TrayIconFactory
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Drawing.Icon CreateTrayIcon()
        {
            using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);

                // Pillola Material You scura
                using var bgPath = CreateRoundedRectPath(new Drawing.RectangleF(2f, 2f, 28f, 28f), 8f);
                using (var bgBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 0x18, 0x22, 0x35)))
                {
                    g.FillPath(bgBrush, bgPath);
                }

                using (var borderPen = new Drawing.Pen(Drawing.Color.FromArgb(140, 0xA8, 0xC7, 0xFA), 1.4f))
                {
                    g.DrawPath(borderPen, bgPath);
                }

                // Sole Pixel in alto a destra
                using (var sunGlow = new Drawing.SolidBrush(Drawing.Color.FromArgb(80, 0xFF, 0xD5, 0x4F)))
                {
                    g.FillEllipse(sunGlow, 15.5f, 5.5f, 11f, 11f);
                }

                using (var sunBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 0xFF, 0xD5, 0x4F)))
                {
                    g.FillEllipse(sunBrush, 17.5f, 7.5f, 7f, 7f);
                }

                // Lancette orologio Material in primo piano
                using (var handPen = new Drawing.Pen(Drawing.Color.FromArgb(255, 0xF1, 0xF5, 0xF9), 2.2f))
                {
                    handPen.StartCap = Drawing2D.LineCap.Round;
                    handPen.EndCap = Drawing2D.LineCap.Round;
                    g.DrawLine(handPen, 12.5f, 18.5f, 12.5f, 11.5f);
                    g.DrawLine(handPen, 12.5f, 18.5f, 18.5f, 21.0f);
                }

                using (var centerBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 0xA8, 0xC7, 0xFA)))
                {
                    g.FillEllipse(centerBrush, 10.8f, 16.8f, 3.4f, 3.4f);
                }
            }

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                using Drawing.Icon raw = Drawing.Icon.FromHandle(hIcon);
                return (Drawing.Icon)raw.Clone();
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        private static Drawing2D.GraphicsPath CreateRoundedRectPath(Drawing.RectangleF bounds, float radius)
        {
            float d = radius * 2f;
            var path = new Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
