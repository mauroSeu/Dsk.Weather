using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Dsk.Weather.Models;
using Dsk.Weather.Services;

namespace Dsk.Weather.Controls
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
                    BuildMoonAndStars(width, height, isLight, starCount: 22, includeShootingStar: true, rng);
                    break;

                case WeatherIconKind.PartlyCloudyDay:
                    BuildSunGlow(width, height, isLight, includeParticles: false, rng);
                    BuildVolumetricClouds(width, height, isLight, isNight: false, isStorm: false, layerCount: 2, baseOpacity: isLight ? 0.42 : 0.34, minThreshold: 0.40, maxThreshold: 0.44);
                    break;

                case WeatherIconKind.PartlyCloudyNight:
                    BuildMoonAndStars(width, height, isLight, starCount: 14, includeShootingStar: false, rng);
                    BuildVolumetricClouds(width, height, isLight, isNight: true, isStorm: false, layerCount: 2, baseOpacity: isLight ? 0.38 : 0.30, minThreshold: 0.40, maxThreshold: 0.44);
                    break;

                case WeatherIconKind.Cloudy:
                    BuildVolumetricClouds(width, height, isLight, isNight: false, isStorm: false, layerCount: 3, baseOpacity: isLight ? 0.46 : 0.40, minThreshold: 0.29, maxThreshold: 0.35);
                    break;

                case WeatherIconKind.Fog:
                    BuildFogLayers(width, height, isLight);
                    break;

                case WeatherIconKind.Drizzle:
                    BuildVolumetricClouds(width, height, isLight, isNight: false, isStorm: false, layerCount: 2, baseOpacity: isLight ? 0.38 : 0.32, minThreshold: 0.32, maxThreshold: 0.36);
                    BuildRainDrops(width, height, isLight, count: 22, minDur: 0.92, maxDur: 1.32, thickness: 1.35, length: 16, rng);
                    break;

                case WeatherIconKind.Rain:
                    BuildVolumetricClouds(width, height, isLight, isNight: false, isStorm: false, layerCount: 2, baseOpacity: isLight ? 0.44 : 0.36, minThreshold: 0.28, maxThreshold: 0.33);
                    BuildRainDrops(width, height, isLight, count: 36, minDur: 0.62, maxDur: 1.02, thickness: 1.75, length: 22, rng);
                    break;

                case WeatherIconKind.Snow:
                    BuildVolumetricClouds(width, height, isLight, isNight: false, isStorm: false, layerCount: 2, baseOpacity: isLight ? 0.30 : 0.24, minThreshold: 0.38, maxThreshold: 0.43);
                    BuildSnowFlakes(width, height, isLight, count: 28, rng);
                    break;

                case WeatherIconKind.Thunderstorm:
                    BuildVolumetricClouds(width, height, isLight, isNight: false, isStorm: true, layerCount: 3, baseOpacity: isLight ? 0.52 : 0.48, minThreshold: 0.26, maxThreshold: 0.32);
                    BuildLightningFlash(width, height, isLight);
                    BuildRainDrops(width, height, isLight, count: 34, minDur: 0.58, maxDur: 0.92, thickness: 1.85, length: 23, rng);
                    break;
            }
        }

        private void BuildSunGlow(double width, double height, bool isLight, bool includeParticles, Random rng)
        {
            double sunCenterX = width - 46;
            double sunCenterY = 24;

            // Bagliore atmosferico esterno ampio
            double outerSize = 230;
            var outerBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            outerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)130 : (byte)115, 0xFF, 0xC1, 0x33), 0.0));
            outerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)65 : (byte)55, 0xFF, 0x98, 0x00), 0.50));
            outerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x8F, 0x00), 1.0));
            outerBrush.Freeze();

            var outerScale = new ScaleTransform(1.0, 1.0);
            var outerOrb = new Ellipse
            {
                Width = outerSize,
                Height = outerSize,
                Fill = outerBrush,
                Opacity = 0.44,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = outerScale
            };
            Canvas.SetLeft(outerOrb, sunCenterX - outerSize / 2.0);
            Canvas.SetTop(outerOrb, sunCenterY - outerSize / 2.0);
            SceneCanvas.Children.Add(outerOrb);

            StartDoubleAnim(outerScale, ScaleTransform.ScaleXProperty, 0.92, 1.10, 4.2, autoReverse: true, useSineEase: true);
            StartDoubleAnim(outerScale, ScaleTransform.ScaleYProperty, 0.92, 1.10, 4.2, autoReverse: true, useSineEase: true);
            StartDoubleAnim(outerOrb, OpacityProperty, 0.36, 0.56, 4.2, autoReverse: true, useSineEase: true);

            // Cuore solare morbido
            double coreSize = 112;
            var coreBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)195 : (byte)185, 0xFF, 0xF3, 0xC4), 0.0));
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)115 : (byte)95, 0xFF, 0xD5, 0x4F), 0.42));
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xB3, 0x00), 1.0));
            coreBrush.Freeze();

            var coreScale = new ScaleTransform(1.0, 1.0);
            var coreOrb = new Ellipse
            {
                Width = coreSize,
                Height = coreSize,
                Fill = coreBrush,
                Opacity = 0.55,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = coreScale
            };
            Canvas.SetLeft(coreOrb, sunCenterX - coreSize / 2.0);
            Canvas.SetTop(coreOrb, sunCenterY - coreSize / 2.0);
            SceneCanvas.Children.Add(coreOrb);

            StartDoubleAnim(coreScale, ScaleTransform.ScaleXProperty, 0.95, 1.07, 3.1, autoReverse: true, beginSec: -1.1, useSineEase: true);
            StartDoubleAnim(coreScale, ScaleTransform.ScaleYProperty, 0.95, 1.07, 3.1, autoReverse: true, beginSec: -1.1, useSineEase: true);

            // Due strati di fasci crepuscolari morbidi (god rays procedurali) in oscillazione lenta
            double raysW = Math.Clamp(width * 0.78, 220.0, 320.0);
            double raysH = Math.Clamp(height * 0.86, 175.0, 240.0);
            for (int layer = 0; layer < 2; layer++)
            {
                BitmapSource raysTex = ProceduralAtmosphereFactory.BuildSunRaysBitmap(220, 180, isLight, seed: 41 + layer * 29);
                var raysRotate = new RotateTransform(0);
                var raysImg = new Image
                {
                    Source = raysTex,
                    Width = raysW,
                    Height = raysH,
                    Stretch = Stretch.Fill,
                    Opacity = layer == 0 ? (isLight ? 0.42 : 0.36) : (isLight ? 0.28 : 0.24),
                    RenderTransformOrigin = new Point(0.88, 0.08),
                    RenderTransform = raysRotate
                };
                RenderOptions.SetBitmapScalingMode(raysImg, BitmapScalingMode.HighQuality);

                Canvas.SetLeft(raysImg, width - raysW);
                Canvas.SetTop(raysImg, -6);
                SceneCanvas.Children.Add(raysImg);

                double swing = layer == 0 ? 3.5 : -2.5;
                double dur = layer == 0 ? 9.5 : 12.0;
                double minOp = layer == 0 ? (isLight ? 0.30 : 0.25) : (isLight ? 0.18 : 0.14);
                double maxOp = layer == 0 ? (isLight ? 0.50 : 0.44) : (isLight ? 0.36 : 0.32);

                StartDoubleAnim(raysRotate, RotateTransform.AngleProperty, -swing, swing, dur, autoReverse: true, beginSec: -layer * 3.2, useSineEase: true);
                StartDoubleAnim(raysImg, OpacityProperty, minOp, maxOp, dur * 0.72, autoReverse: true, beginSec: -layer * 2.0, useSineEase: true);
            }

            if (includeParticles)
            {
                var bokehBrush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5
                };
                bokehBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)195 : (byte)180, 0xFF, 0xEE, 0xAD), 0.0));
                bokehBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)75 : (byte)65, 0xFF, 0xD5, 0x4F), 0.55));
                bokehBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xB3, 0x00), 1.0));
                bokehBrush.Freeze();

                for (int i = 0; i < 7; i++)
                {
                    double size = 6.5 + (i % 3) * 3.5;
                    var dotTrans = new TranslateTransform(0, 0);
                    var dot = new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Fill = bokehBrush,
                        Opacity = 0.0,
                        RenderTransform = dotTrans
                    };
                    Canvas.SetLeft(dot, width * (0.24 + rng.NextDouble() * 0.66));
                    Canvas.SetTop(dot, height * (0.26 + rng.NextDouble() * 0.54));
                    SceneCanvas.Children.Add(dot);

                    double dur = 4.2 + i * 0.65;
                    double phase = -(dur * i / 7.0);
                    StartDoubleAnim(dotTrans, TranslateTransform.YProperty, 12.0, -40.0, dur, autoReverse: false, beginSec: phase);
                    StartPulseKeyframes(dot, dur, phase, 0.0, 0.74);
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
            moonBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)105 : (byte)135, 0xBF, 0xDB, 0xFE), 0.0));
            moonBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)38 : (byte)52, 0x60, 0xA5, 0xFA), 0.52));
            moonBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x60, 0xA5, 0xFA), 1.0));
            moonBrush.Freeze();

            var glowScale = new ScaleTransform(1.0, 1.0);
            var glow = new Ellipse
            {
                Width = 190,
                Height = 190,
                Fill = moonBrush,
                Opacity = 0.52,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = glowScale
            };
            Canvas.SetLeft(glow, width - 122);
            Canvas.SetTop(glow, -56);
            SceneCanvas.Children.Add(glow);

            StartDoubleAnim(glowScale, ScaleTransform.ScaleXProperty, 0.94, 1.10, 4.4, autoReverse: true, useSineEase: true);
            StartDoubleAnim(glowScale, ScaleTransform.ScaleYProperty, 0.94, 1.10, 4.4, autoReverse: true, useSineEase: true);
            StartDoubleAnim(glow, OpacityProperty, 0.36, 0.60, 4.4, autoReverse: true, useSineEase: true);

            Color starColor = isLight ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0xF1, 0xF5, 0xFF);
            var starBrush = new SolidColorBrush(starColor);
            starBrush.Freeze();

            var starHaloBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            starHaloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(245, starColor.R, starColor.G, starColor.B), 0.0));
            starHaloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(95, starColor.R, starColor.G, starColor.B), 0.38));
            starHaloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, starColor.R, starColor.G, starColor.B), 1.0));
            starHaloBrush.Freeze();

            for (int i = 0; i < starCount; i++)
            {
                double size = 2.0 + rng.NextDouble() * 2.4;
                UIElement starElement;

                if (i % 5 == 0)
                {
                    var cross = new Canvas { Width = 10, Height = 10, Opacity = 0.25 };
                    cross.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = starHaloBrush, Opacity = 0.65 });
                    cross.Children.Add(SetTop(SetLeft(new Rectangle { Width = 7, Height = 1.2, RadiusX = 0.6, RadiusY = 0.6, Fill = starBrush }, 1.5), 4.4));
                    cross.Children.Add(SetTop(SetLeft(new Rectangle { Width = 1.2, Height = 7, RadiusX = 0.6, RadiusY = 0.6, Fill = starBrush }, 4.4), 1.5));
                    starElement = cross;
                }
                else
                {
                    starElement = new Ellipse
                    {
                        Width = size + 2.2,
                        Height = size + 2.2,
                        Fill = starHaloBrush,
                        Opacity = 0.25
                    };
                }

                Canvas.SetLeft(starElement, 14 + rng.NextDouble() * Math.Max(40, width - 28));
                Canvas.SetTop(starElement, 10 + rng.NextDouble() * Math.Max(40, height * 0.68));
                SceneCanvas.Children.Add(starElement);

                double dur = 1.7 + rng.NextDouble() * 1.8;
                double phase = -(rng.NextDouble() * dur);
                StartDoubleAnim(starElement, OpacityProperty, 0.16, isLight ? 0.72 : 0.95, dur, autoReverse: true, beginSec: phase, useSineEase: true);
            }

            if (includeShootingStar)
            {
                var shootTrans = new TranslateTransform(0, 0);
                var shootBrush = new LinearGradientBrush(
                    Color.FromArgb(0, 0xE2, 0xEC, 0xFF),
                    Color.FromArgb(isLight ? (byte)205 : (byte)245, 0xFF, 0xFF, 0xFF),
                    new Point(0, 0),
                    new Point(1, 0.4));
                shootBrush.Freeze();

                var streak = new Rectangle
                {
                    Width = 46,
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

                var xKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.8), RepeatBehavior = RepeatBehavior.Forever };
                xKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(0)));
                xKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(4.6)));
                xKeys.KeyFrames.Add(new EasingDoubleKeyFrame(168, Kt(5.45)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                StartRawAnim(shootTrans, TranslateTransform.XProperty, xKeys);

                var yKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.8), RepeatBehavior = RepeatBehavior.Forever };
                yKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(0)));
                yKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, Kt(4.6)));
                yKeys.KeyFrames.Add(new EasingDoubleKeyFrame(68, Kt(5.45)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                StartRawAnim(shootTrans, TranslateTransform.YProperty, yKeys);

                var opKeys = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6.8), RepeatBehavior = RepeatBehavior.Forever };
                opKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(0)));
                opKeys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(4.6)));
                opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, Kt(4.78)));
                opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(5.50)));
                StartRawAnim(streak, OpacityProperty, opKeys);
            }
        }

        private void BuildVolumetricClouds(
            double width,
            double height,
            bool isLight,
            bool isNight,
            bool isStorm,
            int layerCount,
            double baseOpacity,
            double minThreshold,
            double maxThreshold)
        {
            double tileW = Math.Max(width * 1.25, 360.0);
            double[] durations = new[] { 36.0, 25.0, 17.0 };

            for (int i = 0; i < layerCount; i++)
            {
                double t = layerCount <= 1 ? 0.5 : (double)i / (layerCount - 1);
                double coverageThreshold = minThreshold + (maxThreshold - minThreshold) * t;
                int seed = 101 + i * 37 + (isStorm ? 500 : 0) + (isNight ? 200 : 0);

                BitmapSource tex = ProceduralAtmosphereFactory.BuildCloudLayerBitmap(
                    256,
                    96,
                    isLight,
                    isNight,
                    isStorm,
                    seed,
                    coverageThreshold);

                double layerH = Math.Clamp(height * (0.48 + i * 0.12), 78.0, 155.0);
                double layerOpacity = Math.Clamp(baseOpacity * (0.72 + t * 0.34), 0.15, 0.85);

                var stripHost = new Canvas
                {
                    Width = tileW * 2.0,
                    Height = layerH,
                    Opacity = layerOpacity
                };

                for (int copy = 0; copy < 2; copy++)
                {
                    var img = new Image
                    {
                        Source = tex,
                        Width = tileW,
                        Height = layerH,
                        Stretch = Stretch.Fill
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Canvas.SetLeft(img, copy * tileW);
                    Canvas.SetTop(img, 0);
                    stripHost.Children.Add(img);
                }

                var trans = new TranslateTransform(0, 0);
                stripHost.RenderTransform = trans;

                double topOffset = -6.0 + i * Math.Max(14.0, (height * 0.24) / Math.Max(1, layerCount));
                Canvas.SetLeft(stripHost, 0);
                Canvas.SetTop(stripHost, topOffset);
                SceneCanvas.Children.Add(stripHost);

                double dur = durations[Math.Min(i, durations.Length - 1)];
                double phase = -(dur * i / Math.Max(1, layerCount));
                StartDoubleAnim(trans, TranslateTransform.XProperty, 0.0, -tileW, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(trans, TranslateTransform.YProperty, -2.0, 2.0, 5.2 + i * 0.9, autoReverse: true, beginSec: phase * 0.4, useSineEase: true);
            }
        }

        private void BuildFogLayers(double width, double height, bool isLight)
        {
            double tileW = Math.Max(width * 1.2, 340.0);
            double[] durations = new[] { 34.0, 26.0, 21.0 };

            for (int i = 0; i < 3; i++)
            {
                BitmapSource tex = ProceduralAtmosphereFactory.BuildFogLayerBitmap(256, 72, isLight, seed: 301 + i * 29);
                double layerH = Math.Clamp(height * (0.44 + i * 0.08), 64.0, 120.0);

                var stripHost = new Canvas
                {
                    Width = tileW * 2.0,
                    Height = layerH,
                    Opacity = isLight ? 0.36 : 0.32
                };

                for (int copy = 0; copy < 2; copy++)
                {
                    var img = new Image
                    {
                        Source = tex,
                        Width = tileW,
                        Height = layerH,
                        Stretch = Stretch.Fill
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Canvas.SetLeft(img, copy * tileW);
                    Canvas.SetTop(img, 0);
                    stripHost.Children.Add(img);
                }

                var trans = new TranslateTransform(0, 0);
                stripHost.RenderTransform = trans;

                Canvas.SetLeft(stripHost, 0);
                Canvas.SetTop(stripHost, 8.0 + i * ((height * 0.56) / 3.0));
                SceneCanvas.Children.Add(stripHost);

                double dur = durations[i];
                double phase = -(dur * i / 3.0);
                double fromX = i % 2 == 0 ? 0.0 : -tileW;
                double toX = i % 2 == 0 ? -tileW : 0.0;

                StartDoubleAnim(trans, TranslateTransform.XProperty, fromX, toX, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(stripHost, OpacityProperty, isLight ? 0.24 : 0.20, isLight ? 0.46 : 0.40, 6.2 + i * 1.1, autoReverse: true, beginSec: phase * 0.5, useSineEase: true);
            }
        }

        private void BuildRainDrops(double width, double height, bool isLight, int count, double minDur, double maxDur, double thickness, double length, Random rng)
        {
            Color dropCol = isLight ? Color.FromRgb(0x1D, 0x4E, 0xD8) : Color.FromRgb(0x7D, 0xD3, 0xFC);
            var dropBrush = new LinearGradientBrush(
                Color.FromArgb(0, dropCol.R, dropCol.G, dropCol.B),
                Color.FromArgb(isLight ? (byte)180 : (byte)215, dropCol.R, dropCol.G, dropCol.B),
                new Point(0, 0),
                new Point(0, 1));
            dropBrush.Freeze();

            int farCount = Math.Max(1, (int)Math.Round(count * 0.45));

            for (int i = 0; i < count; i++)
            {
                bool isFar = i < farCount;
                double dropThickness = isFar ? Math.Max(0.9, thickness * 0.65) : thickness;
                double dropLength = isFar ? length * 0.68 + rng.NextDouble() * 4.0 : length + rng.NextDouble() * 9.0;
                double dropOpacity = isFar ? (0.22 + rng.NextDouble() * 0.16) : (0.48 + rng.NextDouble() * 0.28);
                double durBase = minDur + rng.NextDouble() * (maxDur - minDur);
                double dur = isFar ? durBase * 1.35 : durBase;

                var trans = new TranslateTransform(0, 0);
                var drop = new Rectangle
                {
                    Width = dropThickness,
                    Height = dropLength,
                    RadiusX = dropThickness * 0.5,
                    RadiusY = dropThickness * 0.5,
                    Fill = dropBrush,
                    Opacity = dropOpacity,
                    RenderTransform = new TransformGroup
                    {
                        Children =
                        {
                            new RotateTransform(13),
                            trans
                        }
                    }
                };

                double startX = -10 + rng.NextDouble() * (width + 68);
                Canvas.SetLeft(drop, startX);
                Canvas.SetTop(drop, -34);
                SceneCanvas.Children.Add(drop);

                double phase = -(dur * rng.NextDouble());
                double travelY = height + 54;
                double travelX = -travelY * 0.23;

                StartDoubleAnim(trans, TranslateTransform.YProperty, 0.0, travelY, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(trans, TranslateTransform.XProperty, 0.0, travelX, dur, autoReverse: false, beginSec: phase);
            }
        }

        private void BuildSnowFlakes(double width, double height, bool isLight, int count, Random rng)
        {
            Color flakeCol = isLight ? Color.FromRgb(0x25, 0x63, 0xEB) : Color.FromRgb(0xF0, 0xF9, 0xFF);
            var bokehFlakeBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            bokehFlakeBrush.GradientStops.Add(new GradientStop(Color.FromArgb(245, flakeCol.R, flakeCol.G, flakeCol.B), 0.0));
            bokehFlakeBrush.GradientStops.Add(new GradientStop(Color.FromArgb(145, flakeCol.R, flakeCol.G, flakeCol.B), 0.45));
            bokehFlakeBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, flakeCol.R, flakeCol.G, flakeCol.B), 1.0));
            bokehFlakeBrush.Freeze();

            int farCount = Math.Max(1, count / 2);

            for (int i = 0; i < count; i++)
            {
                bool isFar = i < farCount;
                double size = isFar ? (2.8 + rng.NextDouble() * 1.4) : (5.0 + rng.NextDouble() * 2.5);
                double opacity = isFar ? (0.36 + rng.NextDouble() * 0.24) : (0.62 + rng.NextDouble() * 0.30);

                var trans = new TranslateTransform(0, 0);
                var flake = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = bokehFlakeBrush,
                    Opacity = opacity,
                    RenderTransform = trans
                };

                Canvas.SetLeft(flake, rng.NextDouble() * width);
                Canvas.SetTop(flake, -16);
                SceneCanvas.Children.Add(flake);

                double dur = isFar ? (4.0 + rng.NextDouble() * 2.4) : (2.9 + rng.NextDouble() * 1.9);
                double phase = -(dur * rng.NextDouble());
                double sway = (i % 2 == 0 ? 1 : -1) * ((isFar ? 7.0 : 12.0) + rng.NextDouble() * 6.0);

                StartDoubleAnim(trans, TranslateTransform.YProperty, 0.0, height + 28, dur, autoReverse: false, beginSec: phase);
                StartDoubleAnim(trans, TranslateTransform.XProperty, -sway, sway, dur * 0.50, autoReverse: true, beginSec: phase, useSineEase: true);
            }
        }

        private void BuildLightningFlash(double width, double height, bool isLight)
        {
            // Sheet lightning diffuso dentro i banchi nuvolosi (2 centri sfalsati)
            var flashMain = CreateCloudFlashRect(width, height, isLight, new Point(0.76, 0.16), 210);
            var flashSec = CreateCloudFlashRect(width, height, isLight, new Point(0.32, 0.20), 170);
            SceneCanvas.Children.Add(flashMain);
            SceneCanvas.Children.Add(flashSec);

            // Scarica elettrica ramificata sottile (bagliore + nucleo)
            var boltGeom = new StreamGeometry();
            using (StreamGeometryContext ctx = boltGeom.Open())
            {
                ctx.BeginFigure(new Point(22, 0), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(14, 15), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(21, 18), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(10, 36), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(16, 39), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(7, 56), isStroked: true, isSmoothJoin: false);

                ctx.BeginFigure(new Point(21, 18), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(29, 29), isStroked: true, isSmoothJoin: true);
                ctx.LineTo(new Point(26, 38), isStroked: true, isSmoothJoin: false);

                ctx.BeginFigure(new Point(10, 36), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(2, 46), isStroked: true, isSmoothJoin: false);
            }
            boltGeom.Freeze();

            var haloBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA));
            haloBrush.Freeze();
            var coreBrush = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            coreBrush.Freeze();

            var boltHalo = new Path
            {
                Data = boltGeom,
                Stroke = haloBrush,
                StrokeThickness = 5.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.0
            };
            var boltCore = new Path
            {
                Data = boltGeom,
                Stroke = coreBrush,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.0
            };
            Canvas.SetLeft(boltHalo, width - 92);
            Canvas.SetTop(boltHalo, 14);
            Canvas.SetLeft(boltCore, width - 92);
            Canvas.SetTop(boltCore, 14);
            SceneCanvas.Children.Add(boltHalo);
            SceneCanvas.Children.Add(boltCore);

            StartFlashKeyframes(flashMain, durationSec: 5.8, tStart: 3.6, peak1: 0.42, peak2: 0.54);
            StartFlashKeyframes(flashSec, durationSec: 5.8, tStart: 1.2, peak1: 0.24, peak2: 0.30);
            StartFlashKeyframes(boltHalo, durationSec: 5.8, tStart: 3.6, peak1: 0.42, peak2: 0.52);
            StartFlashKeyframes(boltCore, durationSec: 5.8, tStart: 3.6, peak1: 0.85, peak2: 0.96);
        }

        private static Rectangle CreateCloudFlashRect(double width, double height, bool isLight, Point center, byte peakAlpha)
        {
            var flashBrush = new RadialGradientBrush
            {
                GradientOrigin = center,
                Center = center,
                RadiusX = 0.65,
                RadiusY = 0.55
            };
            flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(isLight ? (byte)(peakAlpha - 35) : peakAlpha, 0xE0, 0xF2, 0xFE), 0.0));
            flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x93, 0xC5, 0xFD), 1.0));
            flashBrush.Freeze();

            return new Rectangle
            {
                Width = width,
                Height = height,
                Fill = flashBrush,
                Opacity = 0.0
            };
        }

        private void StartFlashKeyframes(IAnimatable target, double durationSec, double tStart, double peak1, double peak2)
        {
            var keys = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(durationSec),
                RepeatBehavior = RepeatBehavior.Forever
            };
            keys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(0.0)));
            keys.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, Kt(tStart)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(peak1, Kt(tStart + 0.07)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(peak1 * 0.15, Kt(tStart + 0.16)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(peak2, Kt(tStart + 0.23)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(tStart + 0.52)));
            StartRawAnim(target, OpacityProperty, keys);
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
            double beginSec = 0,
            bool useSineEase = false)
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
            if (useSineEase)
            {
                anim.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
            }
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
