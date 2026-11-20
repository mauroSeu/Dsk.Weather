using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using PixelDesktopWidget.Models;

namespace PixelDesktopWidget.Controls
{
    public partial class WeatherSceneCanvas : UserControl
    {
        private readonly List<(IAnimatable Target, DependencyProperty Property)> _activeAnimations =
            new List<(IAnimatable Target, DependencyProperty Property)>();

        private WeatherIconKind? _currentKind;
        private bool _currentEnabled;
        private bool _currentIsLight;
        private double _builtWidth;
        private double _builtHeight;

        public WeatherSceneCanvas()
        {
            InitializeComponent();
        }

        internal int ActiveAnimationCount => _activeAnimations.Count;

        public void Configure(WeatherIconKind kind, bool enabled, bool isLightTheme)
        {
            if (!enabled)
            {
                _currentKind = kind;
                _currentEnabled = false;
                _currentIsLight = isLightTheme;
                Stop();
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;
            double w = ActualWidth > 20 ? ActualWidth : 390;
            double h = ActualHeight > 20 ? ActualHeight : 250;

            if (_currentEnabled &&
                _currentKind == kind &&
                _currentIsLight == isLightTheme &&
                Math.Abs(_builtWidth - w) < 6 &&
                Math.Abs(_builtHeight - h) < 6 &&
                _activeAnimations.Count > 0)
            {
                return;
            }

            _currentKind = kind;
            _currentEnabled = true;
            _currentIsLight = isLightTheme;
            RebuildScene(kind, isLightTheme, w, h);
        }

        public void Stop()
        {
            foreach ((IAnimatable target, DependencyProperty prop) in _activeAnimations)
            {
                target.BeginAnimation(prop, null);
            }
            _activeAnimations.Clear();
            SceneCanvas.Children.Clear();
        }

        private void WeatherSceneCanvas_Unloaded(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void WeatherSceneCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_currentEnabled || !_currentKind.HasValue)
            {
                return;
            }

            double w = e.NewSize.Width;
            double h = e.NewSize.Height;
            if (w <= 20 || h <= 20)
            {
                return;
            }

            if (Math.Abs(_builtWidth - w) >= 6 || Math.Abs(_builtHeight - h) >= 6)
            {
                RebuildScene(_currentKind.Value, _currentIsLight, w, h);
            }
        }

        private void RebuildScene(WeatherIconKind kind, bool isLight, double width, double height)
        {
            Stop();
            _builtWidth = width;
            _builtHeight = height;

            var rng = new Random(1729 + (int)kind * 31);

            switch (kind)
            {
                case WeatherIconKind.ClearDay:
                    BuildSunGlow(width, height, isLight, includeParticles: true, rng);
                    break;

                case WeatherIconKind.ClearNight:
                    BuildMoonAndStars(width, height, isLight, starCount: 20, includeShootingStar: true, rng);
                    break;

                case WeatherIconKind.PartlyCloudyDay:
                    BuildSunGlow(width, height, isLight, includeParticles: false, rng);
                    BuildDriftingClouds(width, height, isLight, cloudCount: 3, minAlpha: isLight ? 0.30 : 0.20, maxAlpha: isLight ? 0.44 : 0.30, rng);
                    break;

                case WeatherIconKind.PartlyCloudyNight:
                    BuildMoonAndStars(width, height, isLight, starCount: 12, includeShootingStar: false, rng);
                    BuildDriftingClouds(width, height, isLight, cloudCount: 3, minAlpha: isLight ? 0.28 : 0.18, maxAlpha: isLight ? 0.40 : 0.28, rng);
                    break;

                case WeatherIconKind.Cloudy:
                    BuildDriftingClouds(width, height, isLight, cloudCount: 5, minAlpha: isLight ? 0.34 : 0.22, maxAlpha: isLight ? 0.48 : 0.36, rng);
                    break;

                case WeatherIconKind.Fog:
                    BuildFogLayers(width, height, isLight);
                    break;

                case WeatherIconKind.Drizzle:
                    BuildDriftingClouds(width, height, isLight, cloudCount: 3, minAlpha: isLight ? 0.24 : 0.16, maxAlpha: isLight ? 0.34 : 0.24, rng);
                    BuildRainDrops(width, height, isLight, count: 18, minDur: 0.95, maxDur: 1.35, thickness: 1.4, length: 16, rng);
                    break;

                case WeatherIconKind.Rain:
                    BuildDriftingClouds(width, height, isLight, cloudCount: 3, minAlpha: isLight ? 0.26 : 0.18, maxAlpha: isLight ? 0.36 : 0.26, rng);
                    BuildRainDrops(width, height, isLight, count: 32, minDur: 0.65, maxDur: 1.05, thickness: 1.8, length: 22, rng);
                    break;

                case WeatherIconKind.Snow:
                    BuildSnowFlakes(width, height, isLight, count: 26, rng);
                    break;

                case WeatherIconKind.Thunderstorm:
                    BuildDriftingClouds(width, height, isLight, cloudCount: 3, minAlpha: isLight ? 0.28 : 0.20, maxAlpha: isLight ? 0.38 : 0.28, rng);
                    BuildRainDrops(width, height, isLight, count: 28, minDur: 0.62, maxDur: 0.95, thickness: 1.9, length: 22, rng);
                    BuildLightningFlash(width, height, isLight);
                    break;
            }
        }

        private void BuildSunGlow(double width, double height, bool isLight, bool includeParticles, Random rng)
        {
            double orbSize = 195;
            var sunBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            sunBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)160 : (byte)145, 0xFF, 0xD1, 0x47), 0.0));
            sunBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)85 : (byte)70, 0xFF, 0x9E, 0x00), 0.48));
            sunBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x9E, 0x00), 1.0));
            sunBrush.Freeze();

            var orbScale = new ScaleTransform(1.0, 1.0);
            var orb = new Ellipse
            {
                Width = orbSize,
                Height = orbSize,
                Fill = sunBrush,
                Opacity = 0.45,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = orbScale
            };
            double sunCenterX = width - 48;
            double sunCenterY = 26;
            Canvas.SetLeft(orb, sunCenterX - orbSize / 2.0);
            Canvas.SetTop(orb, sunCenterY - orbSize / 2.0);
            SceneCanvas.Children.Add(orb);

            StartDoubleAnim(orbScale, ScaleTransform.ScaleXProperty, 0.90, 1.12, 3.6, autoReverse: true);
            StartDoubleAnim(orbScale, ScaleTransform.ScaleYProperty, 0.90, 1.12, 3.6, autoReverse: true);
            StartDoubleAnim(orb, OpacityProperty, 0.38, 0.60, 3.6, autoReverse: true);

            // 10 raggi solari morbidi in rotazione continua
            var raysRotate = new RotateTransform(0);
            var raysHost = new Canvas
            {
                Width = 220,
                Height = 220,
                Opacity = isLight ? 0.34 : 0.28,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = raysRotate
            };
            var rayBrush = new LinearGradientBrush(
                Color.FromArgb(165, 0xFF, 0xD5, 0x4F),
                Color.FromArgb(0, 0xFF, 0xB3, 0x00),
                new Point(0.5, 0.0),
                new Point(0.5, 1.0));
            rayBrush.Freeze();

            for (int i = 0; i < 10; i++)
            {
                double angle = i * 36.0;
                var ray = new Rectangle
                {
                    Width = 8,
                    Height = 90,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = rayBrush,
                    RenderTransformOrigin = new Point(0.5, 0.0),
                    RenderTransform = new RotateTransform(angle)
                };
                Canvas.SetLeft(ray, 110 - 4);
                Canvas.SetTop(ray, 110);
                raysHost.Children.Add(ray);
            }

            Canvas.SetLeft(raysHost, sunCenterX - 110);
            Canvas.SetTop(raysHost, sunCenterY - 110);
            SceneCanvas.Children.Add(raysHost);

            StartDoubleAnim(raysRotate, RotateTransform.AngleProperty, 0.0, 360.0, 22.0, autoReverse: false);

            var rayStroke = new SolidColorBrush(Color.FromArgb(isLight ? (byte)165 : (byte)140, 0xFF, 0xE0, 0x82));
            rayStroke.Freeze();

            if (includeParticles)
            {
                for (int i = 0; i < 6; i++)
                {
                    double size = 3.5 + (i % 3) * 1.8;
                    var dotTrans = new TranslateTransform(0, 0);
                    var dot = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = rayStroke,
                        Opacity = 0.0,
                        RenderTransform = dotTrans
                    };
                    Canvas.SetLeft(dot, width * (0.28 + rng.NextDouble() * 0.62));
                    Canvas.SetTop(dot, height * (0.35 + rng.NextDouble() * 0.45));
                    SceneCanvas.Children.Add(dot);

                    double dur = 3.8 + i * 0.7;
                    double phase = -(dur * i / 6.0);
                    StartDoubleAnim(dotTrans, TranslateTransform.YProperty, 14.0, -44.0, dur, autoReverse: false, beginSec: phase);
                    StartPulseKeyframes(dot, dur, phase, 0.0, 0.70);
                }
            }
        }

        private void BuildMoonAndStars(double width, double height, bool isLight, int starCount, bool includeShootingStar, Random rng)
        {
            var moonBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            moonBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)95 : (byte)125, 0xBF, 0xDB, 0xFE), 0.0));
            moonBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x60, 0xA5, 0xFA), 1.0));
            moonBrush.Freeze();

            var glowScale = new ScaleTransform(1.0, 1.0);
            var glow = new Ellipse
            {
                Width = 175,
                Height = 175,
                Fill = moonBrush,
                Opacity = 0.50,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = glowScale
            };
            Canvas.SetLeft(glow, width - 115);
            Canvas.SetTop(glow, -50);
            SceneCanvas.Children.Add(glow);

            StartDoubleAnim(glowScale, ScaleTransform.ScaleXProperty, 0.94, 1.10, 4.2, autoReverse: true);
            StartDoubleAnim(glowScale, ScaleTransform.ScaleYProperty, 0.94, 1.10, 4.2, autoReverse: true);
            StartDoubleAnim(glow, OpacityProperty, 0.36, 0.58, 4.2, autoReverse: true);

            Color starColor = isLight ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0xF1, 0xF5, 0xFF);
            var starBrush = new SolidColorBrush(starColor);
            starBrush.Freeze();

            for (int i = 0; i < starCount; i++)
            {
                double size = 2.0 + rng.NextDouble() * 2.6;
                UIElement starElement;

                if (i % 4 == 0)
                {
                    var cross = new Canvas { Width = 7, Height = 7, Opacity = 0.25 };
                    cross.Children.Add(SetTop(new Rectangle { Width = 7, Height = 1.4, RadiusX = 0.7, RadiusY = 0.7, Fill = starBrush }, 2.8));
                    cross.Children.Add(SetLeft(new Rectangle { Width = 1.4, Height = 7, RadiusX = 0.7, RadiusY = 0.7, Fill = starBrush }, 2.8));
                    starElement = cross;
                }
                else
                {
                    starElement = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = starBrush,
                        Opacity = 0.25
                    };
                }

                Canvas.SetLeft(starElement, 14 + rng.NextDouble() * Math.Max(40, width - 28));
                Canvas.SetTop(starElement, 10 + rng.NextDouble() * Math.Max(40, height * 0.68));
                SceneCanvas.Children.Add(starElement);

                double dur = 1.6 + rng.NextDouble() * 1.6;
                double phase = -(rng.NextDouble() * dur);
                StartDoubleAnim(starElement, OpacityProperty, 0.15, isLight ? 0.68 : 0.92, dur, autoReverse: true, beginSec: phase);
            }

            if (includeShootingStar)
            {
                var shootTrans = new TranslateTransform(0, 0);
                var shootBrush = new LinearGradientBrush(
                    Color.FromArgb(0, 0xE2, 0xEC, 0xFF),
                    Color.FromArgb(isLight ? (byte)190 : (byte)240, 0xFF, 0xFF, 0xFF),
                    new Point(0, 0),
                    new Point(1, 0.4));
                shootBrush.Freeze();

                var streak = new Rectangle
                {
                    Width = 42,
                    Height = 2.0,
                    RadiusX = 1.0,
                    RadiusY = 1.0,
                    Fill = shootBrush,
                    Opacity = 0.0,
                    RenderTransform = new TransformGroup
                    {
                        Children =
                        {
                            new RotateTransform(22),
                            shootTrans
                        }
                    }
                };
                Canvas.SetLeft(streak, width * 0.20);
                Canvas.SetTop(streak, 18);
                SceneCanvas.Children.Add(streak);

                var xKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.5), RepeatBehavior = RepeatBehavior.Forever };
                xKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(0)));
                xKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(4.4)));
                xKeys.KeyFrames.Add(new EasingDoubleKeyFrame(160, Kt(5.25)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                StartRawAnim(shootTrans, TranslateTransform.XProperty, xKeys);

                var yKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.5), RepeatBehavior = RepeatBehavior.Forever };
                yKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(0)));
                yKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(4.4)));
                yKeys.KeyFrames.Add(new EasingDoubleKeyFrame(64, Kt(5.25)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                StartRawAnim(shootTrans, TranslateTransform.YProperty, yKeys);

                var opKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.5), RepeatBehavior = RepeatBehavior.Forever };
                opKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(0)));
                opKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(4.4)));
                opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, Kt(4.58)));
                opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(5.30)));
                StartRawAnim(streak, OpacityProperty, opKeys);
            }
        }

        private void BuildDriftingClouds(double width, double height, bool isLight, int cloudCount, double minAlpha, double maxAlpha, Random rng)
        {
            Color cloudTop = isLight ? Color.FromRgb(0x64, 0x74, 0x8B) : Color.FromRgb(0xEC, 0xF3, 0xFF);
            Color cloudEdge = isLight ? Color.FromRgb(0x94, 0xA3, 0xB8) : Color.FromRgb(0x94, 0xA3, 0xB8);

            for (int i = 0; i < cloudCount; i++)
            {
                double cloudW = 140 + (i % 3) * 32;
                double cloudH = 54 + (i % 2) * 14;
                double alpha = minAlpha + ((maxAlpha - minAlpha) * i / Math.Max(1, cloudCount - 1));

                var cloudGroup = new Canvas
                {
                    Width = cloudW,
                    Height = cloudH,
                    Opacity = alpha
                };

                var puffBrush = new LinearGradientBrush(
                    Color.FromArgb(225, cloudTop.R, cloudTop.G, cloudTop.B),
                    Color.FromArgb(0, cloudEdge.R, cloudEdge.G, cloudEdge.B),
                    new Point(0.5, 0.0),
                    new Point(0.5, 1.0));
                puffBrush.Freeze();

                // Nuvole multi-sbuffo morbide stile Samsung One UI
                AddCloudPuff(cloudGroup, puffBrush, 0.08 * cloudW, 0.38 * cloudH, 0.44 * cloudW, 0.56 * cloudH);
                AddCloudPuff(cloudGroup, puffBrush, 0.26 * cloudW, 0.12 * cloudH, 0.48 * cloudW, 0.72 * cloudH);
                AddCloudPuff(cloudGroup, puffBrush, 0.52 * cloudW, 0.28 * cloudH, 0.40 * cloudW, 0.62 * cloudH);

                var cloudTrans = new TranslateTransform(0, 0);
                cloudGroup.RenderTransform = cloudTrans;

                Canvas.SetLeft(cloudGroup, -cloudW);
                Canvas.SetTop(cloudGroup, 6 + (i * Math.Max(16, (height * 0.46) / Math.Max(1, cloudCount))));
                SceneCanvas.Children.Add(cloudGroup);

                double dur = 12.0 + (i % 3) * 4.5 + rng.NextDouble() * 2.0;
                double phase = -(dur * i / Math.Max(1, cloudCount));
                StartDoubleAnim(cloudTrans, TranslateTransform.XProperty, 0.0, width + cloudW + 24.0, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(cloudTrans, TranslateTransform.YProperty, -2.5, 2.5, 3.8 + i * 0.4, autoReverse: true, beginSec: phase * 0.5);
            }
        }

        private static void AddCloudPuff(Canvas host, Brush fill, double left, double top, double w, double h)
        {
            var puff = new Ellipse
            {
                Width = w,
                Height = h,
                Fill = fill
            };
            Canvas.SetLeft(puff, left);
            Canvas.SetTop(puff, top);
            host.Children.Add(puff);
        }

        private void BuildFogLayers(double width, double height, bool isLight)
        {
            Color fogCol = isLight ? Color.FromRgb(0x64, 0x74, 0x8B) : Color.FromRgb(0xE2, 0xE8, 0xF0);

            for (int i = 0; i < 5; i++)
            {
                var trans = new TranslateTransform(0, 0);
                var fogBrush = new LinearGradientBrush(
                    Color.FromArgb(0, fogCol.R, fogCol.G, fogCol.B),
                    Color.FromArgb(isLight ? (byte)85 : (byte)68, fogCol.R, fogCol.G, fogCol.B),
                    new Point(0.0, 0.5),
                    new Point(0.5, 0.5))
                {
                    SpreadMethod = GradientSpreadMethod.Reflect
                };
                fogBrush.Freeze();

                double bandW = width + 110;
                var band = new Rectangle
                {
                    Width = bandW,
                    Height = 28 + (i % 2) * 10,
                    RadiusX = 16,
                    RadiusY = 16,
                    Fill = fogBrush,
                    Opacity = 0.25,
                    RenderTransform = trans
                };
                Canvas.SetLeft(band, -55);
                Canvas.SetTop(band, 14 + i * ((height - 38) / 5.0));
                SceneCanvas.Children.Add(band);

                double shift = i % 2 == 0 ? 48.0 : -48.0;
                double dur = 5.0 + i * 0.8;
                double phase = -(dur * i / 5.0);
                StartDoubleAnim(trans, TranslateTransform.XProperty, -shift, shift, dur, autoReverse: true, beginSec: phase);
                StartDoubleAnim(band, OpacityProperty, 0.18, 0.38, dur * 0.85, autoReverse: true, beginSec: phase);
            }
        }

        private void BuildRainDrops(double width, double height, bool isLight, int count, double minDur, double maxDur, double thickness, double length, Random rng)
        {
            Color dropCol = isLight ? Color.FromRgb(0x1D, 0x4E, 0xD8) : Color.FromRgb(0x7D, 0xD3, 0xFC);
            var dropBrush = new LinearGradientBrush(
                Color.FromArgb(0, dropCol.R, dropCol.G, dropCol.B),
                Color.FromArgb(isLight ? (byte)175 : (byte)205, dropCol.R, dropCol.G, dropCol.B),
                new Point(0, 0),
                new Point(0, 1));
            dropBrush.Freeze();

            for (int i = 0; i < count; i++)
            {
                var trans = new TranslateTransform(0, 0);
                var drop = new Rectangle
                {
                    Width = thickness,
                    Height = length + rng.NextDouble() * 8,
                    RadiusX = thickness * 0.5,
                    RadiusY = thickness * 0.5,
                    Fill = dropBrush,
                    Opacity = 0.52 + rng.NextDouble() * 0.28,
                    RenderTransform = new TransformGroup
                    {
                        Children =
                        {
                            new RotateTransform(14),
                            trans
                        }
                    }
                };

                double startX = -10 + rng.NextDouble() * (width + 65);
                Canvas.SetLeft(drop, startX);
                Canvas.SetTop(drop, -34);
                SceneCanvas.Children.Add(drop);

                double dur = minDur + rng.NextDouble() * (maxDur - minDur);
                double phase = -(dur * rng.NextDouble());
                double travelY = height + 54;
                double travelX = -travelY * 0.25;

                StartDoubleAnim(trans, TranslateTransform.YProperty, 0.0, travelY, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(trans, TranslateTransform.XProperty, 0.0, travelX, dur, autoReverse: false, beginSec: phase);
            }
        }

        private void BuildSnowFlakes(double width, double height, bool isLight, int count, Random rng)
        {
            Color flakeCol = isLight ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0xF0, 0xF9, 0xFF);
            var flakeBrush = new SolidColorBrush(flakeCol);
            flakeBrush.Freeze();

            for (int i = 0; i < count; i++)
            {
                double size = 2.6 + rng.NextDouble() * 2.8;
                var trans = new TranslateTransform(0, 0);
                var flake = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = flakeBrush,
                    Opacity = 0.58 + rng.NextDouble() * 0.34,
                    RenderTransform = trans
                };

                Canvas.SetLeft(flake, rng.NextDouble() * width);
                Canvas.SetTop(flake, -16);
                SceneCanvas.Children.Add(flake);

                double dur = 3.0 + rng.NextDouble() * 2.4;
                double phase = -(dur * rng.NextDouble());
                double sway = (i % 2 == 0 ? 1 : -1) * (10 + rng.NextDouble() * 8);

                StartDoubleAnim(trans, TranslateTransform.YProperty, 0.0, height + 28, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(trans, TranslateTransform.XProperty, -sway, sway, dur * 0.48, autoReverse: true, beginSec: phase);
            }
        }

        private void BuildLightningFlash(double width, double height, bool isLight)
        {
            var flashBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.75, 0.20),
                Center = new Point(0.75, 0.20),
                RadiusX = 0.85,
                RadiusY = 0.85
            };
            flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)180 : (byte)220, 0xE0, 0xF2, 0xFE), 0.0));
            flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x93, 0xC5, 0xFD), 1.0));
            flashBrush.Freeze();

            var flashPanel = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = flashBrush,
                Opacity = 0.0
            };
            SceneCanvas.Children.Add(flashPanel);

            var boltBrush = new SolidColorBrush(Color.FromArgb(220, 0xFD, 0xE0, 0x47));
            boltBrush.Freeze();

            var bolt = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(18, 0),
                    new Point(6, 22),
                    new Point(16, 22),
                    new Point(9, 44),
                    new Point(32, 17),
                    new Point(21, 17)
                },
                Fill = boltBrush,
                Opacity = 0.0
            };
            Canvas.SetLeft(bolt, width - 85);
            Canvas.SetTop(bolt, 16);
            SceneCanvas.Children.Add(bolt);

            var flashKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(5.5), RepeatBehavior = RepeatBehavior.Forever };
            flashKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(0.0)));
            flashKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(3.6)));
            flashKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.42, Kt(3.68)));
            flashKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.06, Kt(3.78)));
            flashKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.48, Kt(3.86)));
            flashKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(4.15)));
            StartRawAnim(flashPanel, OpacityProperty, flashKeys);

            var boltKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(5.5), RepeatBehavior = RepeatBehavior.Forever };
            boltKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(0.0)));
            boltKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(3.6)));
            boltKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, Kt(3.68)));
            boltKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.15, Kt(3.78)));
            boltKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, Kt(3.86)));
            boltKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(4.12)));
            StartRawAnim(bolt, OpacityProperty, boltKeys);
        }

        private static UIElement SetLeft(UIElement el, double left)
        {
            Canvas.SetLeft(el, left);
            return el;
        }

        private static UIElement SetTop(UIElement el, double top)
        {
            Canvas.SetTop(el, top);
            return el;
        }

        private static KeyTime Kt(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));

        private void StartDoubleAnim(
            IAnimatable target,
            DependencyProperty prop,
            double from,
            double to,
            double durationSeconds,
            bool autoReverse,
            double beginSec = 0)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                AutoReverse = autoReverse,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginSec)
            };
            StartRawAnim(target, prop, anim);
        }

        private void StartPulseKeyframes(IAnimatable target, double durationSeconds, double beginSec, double minOp, double maxOp)
        {
            var keys = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(durationSeconds),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginSec)
            };
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(minOp, Kt(0)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(maxOp, Kt(durationSeconds * 0.4)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(minOp, Kt(durationSeconds)));
            StartRawAnim(target, OpacityProperty, keys);
        }

        private void StartRawAnim(IAnimatable target, DependencyProperty prop, AnimationTimeline anim)
        {
            Timeline.SetDesiredFrameRate(anim, 30);
            target.BeginAnimation(prop, anim);
            _activeAnimations.Add((target, prop));
        }
    }
}
