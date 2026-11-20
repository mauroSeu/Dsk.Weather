using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Dsk.Weather.Models;
using Dsk.Weather.Services;

namespace Dsk.Weather.Controls
{
    public partial class WeatherIconView : UserControl
    {
        public static readonly DependencyProperty WeatherCodeProperty =
            DependencyProperty.Register(
                nameof(WeatherCode),
                typeof(int),
                typeof(WeatherIconView),
                new PropertyMetadata(0, OnVisualPropertyChanged));

        public static readonly DependencyProperty IsDayProperty =
            DependencyProperty.Register(
                nameof(IsDay),
                typeof(bool),
                typeof(WeatherIconView),
                new PropertyMetadata(true, OnVisualPropertyChanged));

        public static readonly DependencyProperty IconKindProperty =
            DependencyProperty.Register(
                nameof(IconKind),
                typeof(WeatherIconKind?),
                typeof(WeatherIconView),
                new PropertyMetadata(null, OnVisualPropertyChanged));

        public static readonly DependencyProperty AnimateProperty =
            DependencyProperty.Register(
                nameof(Animate),
                typeof(bool),
                typeof(WeatherIconView),
                new PropertyMetadata(false, OnVisualPropertyChanged));

        private readonly List<(IAnimatable Target, DependencyProperty Property)> _activeAnimations =
            new List<(IAnimatable Target, DependencyProperty Property)>();

        private WeatherIconKind? _animatedKind;

        public WeatherIconView()
        {
            InitializeComponent();
            UpdateVisual();
        }

        internal int ActiveAnimationCount => _activeAnimations.Count;

        public int WeatherCode
        {
            get => (int)GetValue(WeatherCodeProperty);
            set => SetValue(WeatherCodeProperty, value);
        }

        public bool IsDay
        {
            get => (bool)GetValue(IsDayProperty);
            set => SetValue(IsDayProperty, value);
        }

        public WeatherIconKind? IconKind
        {
            get => (WeatherIconKind?)GetValue(IconKindProperty);
            set => SetValue(IconKindProperty, value);
        }

        public bool Animate
        {
            get => (bool)GetValue(AnimateProperty);
            set => SetValue(AnimateProperty, value);
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WeatherIconView view)
            {
                view.UpdateVisual();
            }
        }

        private void WeatherIconView_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void UpdateVisual()
        {
            WeatherIconKind kind = IconKind ?? WeatherService.MapWmoToIcon(WeatherCode, IsDay);

            LayerClearDay.Visibility = kind == WeatherIconKind.ClearDay ? Visibility.Visible : Visibility.Collapsed;
            LayerClearNight.Visibility = kind == WeatherIconKind.ClearNight ? Visibility.Visible : Visibility.Collapsed;
            LayerPartlyCloudyDay.Visibility = kind == WeatherIconKind.PartlyCloudyDay ? Visibility.Visible : Visibility.Collapsed;
            LayerPartlyCloudyNight.Visibility = kind == WeatherIconKind.PartlyCloudyNight ? Visibility.Visible : Visibility.Collapsed;
            LayerCloudy.Visibility = kind == WeatherIconKind.Cloudy ? Visibility.Visible : Visibility.Collapsed;
            LayerFog.Visibility = kind == WeatherIconKind.Fog ? Visibility.Visible : Visibility.Collapsed;
            LayerDrizzle.Visibility = kind == WeatherIconKind.Drizzle ? Visibility.Visible : Visibility.Collapsed;
            LayerRain.Visibility = kind == WeatherIconKind.Rain ? Visibility.Visible : Visibility.Collapsed;
            LayerSnow.Visibility = kind == WeatherIconKind.Snow ? Visibility.Visible : Visibility.Collapsed;
            LayerThunderstorm.Visibility = kind == WeatherIconKind.Thunderstorm ? Visibility.Visible : Visibility.Collapsed;

            if (!Animate)
            {
                StopAnimation();
                return;
            }

            if (_activeAnimations.Count > 0 && _animatedKind == kind)
            {
                return;
            }

            StartAnimation(kind);
        }

        private void StopAnimation()
        {
            foreach ((IAnimatable target, DependencyProperty prop) in _activeAnimations)
            {
                target.BeginAnimation(prop, null);
            }
            _activeAnimations.Clear();
            _animatedKind = null;
        }

        private void StartAnimation(WeatherIconKind kind)
        {
            StopAnimation();
            _animatedKind = kind;

            switch (kind)
            {
                case WeatherIconKind.ClearDay:
                    StartSineAnim(ClearSunHaloScale, ScaleTransform.ScaleXProperty, 0.86, 1.20, 2.0, autoReverse: true);
                    StartSineAnim(ClearSunHaloScale, ScaleTransform.ScaleYProperty, 0.86, 1.20, 2.0, autoReverse: true);
                    StartSineAnim(ClearSunHalo, OpacityProperty, 0.45, 1.0, 2.0, autoReverse: true);
                    StartSineAnim(ClearSunCoreScale, ScaleTransform.ScaleXProperty, 0.95, 1.06, 2.0, autoReverse: true);
                    StartSineAnim(ClearSunCoreScale, ScaleTransform.ScaleYProperty, 0.95, 1.06, 2.0, autoReverse: true);
                    break;

                case WeatherIconKind.ClearNight:
                    StartSineAnim(ClearMoonRotate, RotateTransform.AngleProperty, -9.0, 9.0, 3.0, autoReverse: true);
                    StartSineAnim(ClearStar1, OpacityProperty, 0.20, 1.0, 1.4, autoReverse: true);
                    StartSineAnim(ClearStar2, OpacityProperty, 1.0, 0.20, 1.7, autoReverse: true, beginSec: -0.6);
                    break;

                case WeatherIconKind.PartlyCloudyDay:
                    StartSineAnim(PartlyDaySunScale, ScaleTransform.ScaleXProperty, 0.90, 1.12, 2.4, autoReverse: true);
                    StartSineAnim(PartlyDaySunScale, ScaleTransform.ScaleYProperty, 0.90, 1.12, 2.4, autoReverse: true);
                    StartSineAnim(PartlyDayCloudTrans, TranslateTransform.XProperty, -4.2, 4.2, 2.6, autoReverse: true);
                    break;

                case WeatherIconKind.PartlyCloudyNight:
                    StartSineAnim(PartlyNightMoonRotate, RotateTransform.AngleProperty, -7.0, 7.0, 3.0, autoReverse: true);
                    StartSineAnim(PartlyNightCloudTrans, TranslateTransform.XProperty, -4.2, 4.2, 2.6, autoReverse: true);
                    break;

                case WeatherIconKind.Cloudy:
                    StartSineAnim(CloudyBackTrans, TranslateTransform.XProperty, -4.5, 4.5, 3.0, autoReverse: true);
                    StartSineAnim(CloudyFrontTrans, TranslateTransform.XProperty, 4.0, -4.0, 2.5, autoReverse: true);
                    break;

                case WeatherIconKind.Fog:
                    StartSineAnim(FogBar1Trans, TranslateTransform.XProperty, -4.5, 4.5, 2.4, autoReverse: true);
                    StartSineAnim(FogBar2Trans, TranslateTransform.XProperty, 4.5, -4.5, 2.6, autoReverse: true);
                    break;

                case WeatherIconKind.Drizzle:
                    StartSineAnim(DrizzleCloudTrans, TranslateTransform.YProperty, -1.2, 1.2, 2.2, autoReverse: true);
                    StartDropCycle(DrizzleDrop1, DrizzleDrop1Trans, -1.2, 6.2, 0.95, 0.0);
                    StartDropCycle(DrizzleDrop2, DrizzleDrop2Trans, -1.2, 6.2, 0.95, -0.32);
                    StartDropCycle(DrizzleDrop3, DrizzleDrop3Trans, -1.2, 6.2, 0.95, -0.64);
                    break;

                case WeatherIconKind.Rain:
                    StartSineAnim(RainCloudTrans, TranslateTransform.YProperty, -1.5, 1.5, 1.9, autoReverse: true);
                    StartDropCycle(RainDrop1, RainDrop1Trans, -2.0, 7.0, 0.75, 0.0);
                    StartDropCycle(RainDrop2, RainDrop2Trans, -2.0, 7.0, 0.75, -0.19);
                    StartDropCycle(RainDrop3, RainDrop3Trans, -2.0, 7.0, 0.75, -0.38);
                    StartDropCycle(RainDrop4, RainDrop4Trans, -1.6, 6.2, 0.75, -0.57);
                    break;

                case WeatherIconKind.Snow:
                    StartDropCycle(SnowFlake1, SnowFlake1Trans, 1.4, 7.0, 1.6, 0.0);
                    StartDropCycle(SnowFlake2, SnowFlake2Trans, -1.4, 6.5, 1.7, -0.4);
                    StartDropCycle(SnowFlake3, SnowFlake3Trans, 1.2, 7.0, 1.65, -0.8);
                    StartDropCycle(SnowFlake4, SnowFlake4Trans, -1.1, 5.8, 1.8, -1.1);
                    StartDropCycle(SnowFlake5, SnowFlake5Trans, 1.1, 5.8, 1.8, -0.55);
                    break;

                case WeatherIconKind.Thunderstorm:
                    StartSineAnim(StormCloudTrans, TranslateTransform.YProperty, -1.5, 1.5, 1.7, autoReverse: true);
                    StartDropCycle(StormDrop1, StormDrop1Trans, -1.8, 6.8, 0.72, 0.0);
                    StartDropCycle(StormDrop2, StormDrop2Trans, -1.8, 6.8, 0.72, -0.36);
                    StartLightningPulse(StormLightning);
                    break;
            }
        }

        private void StartSineAnim(
            IAnimatable target,
            DependencyProperty prop,
            double from,
            double to,
            double durationSec,
            bool autoReverse,
            double beginSec = 0)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSec),
                AutoReverse = autoReverse,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginSec),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            StartRawAnim(target, prop, anim);
        }

        private void StartDropCycle(
            UIElement element,
            TranslateTransform trans,
            double dx,
            double dy,
            double durationSec,
            double beginSec)
        {
            var yAnim = new DoubleAnimation
            {
                From = -2.0,
                To = dy,
                Duration = TimeSpan.FromSeconds(durationSec),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginSec)
            };
            StartRawAnim(trans, TranslateTransform.YProperty, yAnim);

            var xAnim = new DoubleAnimation
            {
                From = 0.5,
                To = dx,
                Duration = TimeSpan.FromSeconds(durationSec),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginSec)
            };
            StartRawAnim(trans, TranslateTransform.XProperty, xAnim);

            var opKeys = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(durationSec),
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(beginSec)
            };
            opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(0.0)));
            opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, Kt(durationSec * 0.22)));
            opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, Kt(durationSec * 0.72)));
            opKeys.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, Kt(durationSec)));
            StartRawAnim(element, OpacityProperty, opKeys);
        }

        private void StartLightningPulse(UIElement lightning)
        {
            var keys = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(2.6),
                RepeatBehavior = RepeatBehavior.Forever
            };
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, Kt(0.0)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, Kt(1.5)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, Kt(1.58)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(0.20, Kt(1.66)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, Kt(1.74)));
            keys.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, Kt(2.05)));
            StartRawAnim(lightning, OpacityProperty, keys);
        }

        private void StartRawAnim(IAnimatable target, DependencyProperty prop, AnimationTimeline anim)
        {
            Timeline.SetDesiredFrameRate(anim, 30);
            target.BeginAnimation(prop, anim);
            _activeAnimations.Add((target, prop));
        }

        private static KeyTime Kt(double seconds) => KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }
}
