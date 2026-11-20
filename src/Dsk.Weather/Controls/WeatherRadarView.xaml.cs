using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dsk.Weather.Models;
using Dsk.Weather.Services;

namespace Dsk.Weather.Controls
{
    public partial class WeatherRadarView : UserControl
    {
        private const int ViewportWidth = 308;
        private const int ViewportHeight = 176;
        private const int TileSize = 256;
        private const double RangeRingRadiusPx = 56.0;

        private readonly RadarService _radarService = new RadarService();
        private readonly DispatcherTimer _animTimer;
        private readonly List<List<TileLayer>> _renderedFrames = new List<List<TileLayer>>();
        private readonly List<RadarFrameInfo> _frameInfos = new List<RadarFrameInfo>();

        private CancellationTokenSource? _loadCts;
        private double _lat = 41.9028;
        private double _lon = 12.4964;
        private int _zoom = 7;
        private bool _darkMapMode = true;
        private bool _userOverrodeMapTheme;
        private bool _isPlaying = true;
        private int _currentFrameIndex;
        private int _liveHoldTicks;
        private string _cityName = "Roma";
        private MaterialThemePalette? _palette;

        public event Action<int>? ZoomChanged;

        public WeatherRadarView()
        {
            InitializeComponent();

            _animTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(650)
            };
            _animTimer.Tick += AnimTimer_Tick;
        }

        public void ApplyPalette(MaterialThemePalette palette, bool darkTheme)
        {
            _palette = palette;
            if (!_userOverrodeMapTheme)
            {
                _darkMapMode = darkTheme;
            }

            SolidColorBrush textBrush = MaterialThemePalette.ToBrush(palette.OnSurface);
            SolidColorBrush mutedBrush = MaterialThemePalette.ToBrush(palette.OnSurfaceMuted);
            SolidColorBrush borderBrush = MaterialThemePalette.ToBrush(palette.SurfaceBorder, 0.65);
            SolidColorBrush btnBg = MaterialThemePalette.ToBrush(palette.SurfaceVariant, 0.92);

            MapFrameBorder.BorderBrush = borderBrush;
            MapFrameBorder.Background = MaterialThemePalette.ToBrush(_darkMapMode
                ? Color.FromRgb(0x0D, 0x15, 0x22)
                : Color.FromRgb(0xE8, 0xEE, 0xF6));

            RadarRangeRing.Stroke = MaterialThemePalette.ToBrush(_darkMapMode
                ? Color.FromArgb(0x66, 0x38, 0xBD, 0xF8)
                : Color.FromArgb(0x99, 0x02, 0x84, 0xC7));
            SolidColorBrush crosshairBrush = MaterialThemePalette.ToBrush(_darkMapMode
                ? Color.FromArgb(0x38, 0x94, 0xA3, 0xB8)
                : Color.FromArgb(0x66, 0x33, 0x41, 0x55));
            CrosshairH.Stroke = crosshairBrush;
            CrosshairV.Stroke = crosshairBrush;

            BtnMapThemeToggle.ToolTip = _darkMapMode
                ? "Mappa Notturna Scura attiva (auto dal tramonto all'alba) · Clicca per passare alla Chiara"
                : "Mappa Diurna Chiara attiva (auto dall'alba al tramonto) · Clicca per passare alla Scura";

            TimestampText.Foreground = textBrush;
            LegendText.Foreground = mutedBrush;
            AttributionText.Foreground = mutedBrush;

            foreach (Button btn in new[] { BtnPlayPause, BtnMapThemeToggle, BtnZoomToggle, BtnRefreshRadar })
            {
                btn.Background = btnBg;
                btn.Foreground = textBrush;
                btn.BorderBrush = borderBrush;
            }

            UpdateTimelineSegments();
        }

        public async Task ActivateAsync(
            double lat,
            double lon,
            int zoom,
            bool darkTheme,
            MaterialThemePalette palette,
            string? cityName = null)
        {
            _lat = lat;
            _lon = lon;
            _zoom = Math.Clamp(zoom, 5, 8);
            if (!string.IsNullOrWhiteSpace(cityName))
            {
                _cityName = cityName.Trim();
            }

            ApplyPalette(palette, darkTheme);
            UpdateHudLabels();
            StartPulseAnimation();

            await LoadRadarDataAsync();
        }

        public void Deactivate()
        {
            _animTimer.Stop();
            StopPulseAnimation();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
        }

        private void UpdateHudLabels()
        {
            BtnZoomToggle.Content = $"Z{_zoom}";
            CityPinLabel.Text = string.IsNullOrWhiteSpace(_cityName) ? "Posizione" : _cityName;
            int radiusKm = RadarService.ComputeRadiusKm(_lat, _zoom, RangeRingRadiusPx);
            RangeRadiusText.Text = $"{radiusKm} km";
        }

        private void StartPulseAnimation()
        {
            var scaleAnim = new DoubleAnimation(0.55, 2.25, TimeSpan.FromSeconds(2.4))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0.85, 0.0, TimeSpan.FromSeconds(2.4))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Timeline.SetDesiredFrameRate(scaleAnim, 30);
            Timeline.SetDesiredFrameRate(opacityAnim, 30);

            RadarPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            RadarPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            RadarPulseRing.BeginAnimation(OpacityProperty, opacityAnim);
        }

        private void StopPulseAnimation()
        {
            RadarPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RadarPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            RadarPulseRing.BeginAnimation(OpacityProperty, null);
        }

        private async Task LoadRadarDataAsync()
        {
            _animTimer.Stop();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            CancellationToken ct = _loadCts.Token;

            UpdateHudLabels();
            StatusOverlayText.Text = "Caricamento radar...";
            StatusOverlay.Visibility = Visibility.Visible;
            TimestampText.Text = "Aggiornamento radar...";
            BtnRefreshRadar.IsEnabled = false;

            try
            {
                RadarViewportPlan plan = RadarService.ComputeTileGrid(_lat, _lon, _zoom, ViewportWidth, ViewportHeight);

                // 1. Carica mappa base OpenStreetMap (Dark Tactical o Light)
                var basemapTaskList = new List<Task<(RadarTilePlacement Placement, BitmapSource? Image)>>(plan.Tiles.Count);
                foreach (RadarTilePlacement placement in plan.Tiles)
                {
                    basemapTaskList.Add(FetchBasemapPlacementAsync(plan.Zoom, placement, _darkMapMode, ct));
                }

                // 2. Carica timeline RainViewer in parallelo
                Task<RadarTimeline?> timelineTask = _radarService.FetchTimelineAsync(ct);

                (RadarTilePlacement Placement, BitmapSource? Image)[] basemapResults = await Task.WhenAll(basemapTaskList);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                RenderBasemap(basemapResults);

                RadarTimeline? timeline = await timelineTask;
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                if (timeline is null || timeline.Frames.Count == 0)
                {
                    RadarOverlayCanvas.Children.Clear();
                    _renderedFrames.Clear();
                    _frameInfos.Clear();
                    StatusOverlayText.Text = "Radar non disponibile offline";
                    StatusOverlay.Visibility = Visibility.Visible;
                    TimestampText.Text = "Nessun dato radar";
                    UpdateTimelineSegments();
                    return;
                }

                // 3. Carica i tile radar per i frame disponibili (max 4 download simultanei)
                using var throttle = new SemaphoreSlim(4, 4);
                var frameTasks = new List<Task<List<TileLayer>>>(timeline.Frames.Count);

                foreach (RadarFrameInfo frame in timeline.Frames)
                {
                    frameTasks.Add(FetchFrameTilesAsync(timeline.Host, frame, plan, throttle, ct));
                }

                List<TileLayer>[] frameResults = await Task.WhenAll(frameTasks);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                _renderedFrames.Clear();
                _frameInfos.Clear();
                int liveIndex = 0;
                for (int i = 0; i < timeline.Frames.Count; i++)
                {
                    _frameInfos.Add(timeline.Frames[i]);
                    _renderedFrames.Add(frameResults[i]);
                    if (!timeline.Frames[i].IsForecast)
                    {
                        liveIndex = i;
                    }
                }

                StatusOverlay.Visibility = Visibility.Collapsed;
                _currentFrameIndex = liveIndex;
                _liveHoldTicks = 0;
                DisplayFrame(_currentFrameIndex, animateFade: false);

                if (_isPlaying && _renderedFrames.Count > 1)
                {
                    _animTimer.Start();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellato da nuovo caricamento o chiusura drawer
            }
            catch
            {
                StatusOverlayText.Text = "Errore caricamento radar";
                StatusOverlay.Visibility = Visibility.Visible;
                TimestampText.Text = "Errore rete";
            }
            finally
            {
                BtnRefreshRadar.IsEnabled = true;
            }
        }

        private async Task<(RadarTilePlacement Placement, BitmapSource? Image)> FetchBasemapPlacementAsync(
            int zoom,
            RadarTilePlacement placement,
            bool darkTheme,
            CancellationToken ct)
        {
            BitmapSource? img = await _radarService.GetBasemapTileAsync(zoom, placement.X, placement.Y, darkTheme, ct);
            return (placement, img);
        }

        private async Task<List<TileLayer>> FetchFrameTilesAsync(
            string host,
            RadarFrameInfo frame,
            RadarViewportPlan plan,
            SemaphoreSlim throttle,
            CancellationToken ct)
        {
            var layers = new List<TileLayer>(plan.Tiles.Count);
            var tasks = new List<Task<(RadarTilePlacement Placement, BitmapSource? Image)>>(plan.Tiles.Count);

            foreach (RadarTilePlacement placement in plan.Tiles)
            {
                tasks.Add(FetchSingleRadarTileThrottledAsync(host, frame.Path, plan.Zoom, placement, throttle, ct));
            }

            (RadarTilePlacement Placement, BitmapSource? Image)[] results = await Task.WhenAll(tasks);
            foreach ((RadarTilePlacement p, BitmapSource? img) in results)
            {
                if (img is not null)
                {
                    layers.Add(new TileLayer(p.CanvasLeft, p.CanvasTop, img));
                }
            }

            return layers;
        }

        private async Task<(RadarTilePlacement Placement, BitmapSource? Image)> FetchSingleRadarTileThrottledAsync(
            string host,
            string framePath,
            int zoom,
            RadarTilePlacement placement,
            SemaphoreSlim throttle,
            CancellationToken ct)
        {
            await throttle.WaitAsync(ct);
            try
            {
                BitmapSource? img = await _radarService.GetRadarTileAsync(host, framePath, zoom, placement.X, placement.Y, ct);
                return (placement, img);
            }
            finally
            {
                throttle.Release();
            }
        }

        private void RenderBasemap((RadarTilePlacement Placement, BitmapSource? Image)[] tiles)
        {
            BasemapCanvas.Children.Clear();
            foreach ((RadarTilePlacement p, BitmapSource? img) in tiles)
            {
                if (img is null)
                {
                    continue;
                }

                var imageControl = new Image
                {
                    Source = img,
                    Width = TileSize,
                    Height = TileSize
                };
                RenderOptions.SetBitmapScalingMode(imageControl, BitmapScalingMode.HighQuality);
                Canvas.SetLeft(imageControl, p.CanvasLeft);
                Canvas.SetTop(imageControl, p.CanvasTop);
                BasemapCanvas.Children.Add(imageControl);
            }
        }

        private void DisplayFrame(int index, bool animateFade = true)
        {
            if (index < 0 || index >= _renderedFrames.Count)
            {
                return;
            }

            RadarOverlayCanvas.Children.Clear();
            foreach (TileLayer layer in _renderedFrames[index])
            {
                var imageControl = new Image
                {
                    Source = layer.Image,
                    Width = TileSize,
                    Height = TileSize
                };
                RenderOptions.SetBitmapScalingMode(imageControl, BitmapScalingMode.Linear);
                Canvas.SetLeft(imageControl, layer.Left);
                Canvas.SetTop(imageControl, layer.Top);
                RadarOverlayCanvas.Children.Add(imageControl);
            }

            if (animateFade)
            {
                var fade = new DoubleAnimation(0.45, 0.80, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                Timeline.SetDesiredFrameRate(fade, 30);
                RadarOverlayCanvas.BeginAnimation(OpacityProperty, fade);
            }
            else
            {
                RadarOverlayCanvas.BeginAnimation(OpacityProperty, null);
                RadarOverlayCanvas.Opacity = 0.78;
            }

            RadarFrameInfo info = _frameInfos[index];
            DateTime localTs = info.TimestampUtc.ToLocalTime();

            int lastPastIdx = 0;
            for (int i = 0; i < _frameInfos.Count; i++)
            {
                if (!_frameInfos[i].IsForecast)
                {
                    lastPastIdx = i;
                }
            }

            if (info.IsForecast)
            {
                int minsAhead = Math.Max(5, (int)Math.Round((info.TimestampUtc - _frameInfos[lastPastIdx].TimestampUtc).TotalMinutes));
                TimestampText.Text = $"{localTs.ToString("HH:mm", CultureInfo.InvariantCulture)} (+{minsAhead}m Prev.)";
            }
            else if (index == lastPastIdx)
            {
                TimestampText.Text = $"{localTs.ToString("HH:mm", CultureInfo.InvariantCulture)} · LIVE";
            }
            else
            {
                int minsAgo = (int)Math.Round((_frameInfos[lastPastIdx].TimestampUtc - info.TimestampUtc).TotalMinutes);
                TimestampText.Text = minsAgo > 0
                    ? $"{localTs.ToString("HH:mm", CultureInfo.InvariantCulture)} (-{minsAgo} min)"
                    : localTs.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            UpdateTimelineSegments();
        }

        private void UpdateTimelineSegments()
        {
            Border[] segments = { Seg0, Seg1, Seg2, Seg3, Seg4, Seg5, Seg6, Seg7 };
            Color trackColor = _palette?.SurfaceVariant ?? Color.FromRgb(0x33, 0x41, 0x55);
            Color activeColor = _palette?.Primary ?? Color.FromRgb(0x60, 0xA5, 0xFA);

            SolidColorBrush trackBrush = MaterialThemePalette.ToBrush(trackColor, 0.75);
            SolidColorBrush passedBrush = MaterialThemePalette.ToBrush(activeColor, 0.45);
            SolidColorBrush currentBrush = MaterialThemePalette.ToBrush(activeColor, 1.0);
            SolidColorBrush forecastBrush = MaterialThemePalette.ToBrush(Color.FromRgb(0x38, 0xBD, 0xF8), 0.90);

            for (int i = 0; i < segments.Length; i++)
            {
                if (i >= _frameInfos.Count)
                {
                    segments[i].Background = trackBrush;
                    segments[i].Visibility = _frameInfos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    segments[i].Visibility = Visibility.Visible;
                    if (i == _currentFrameIndex)
                    {
                        segments[i].Background = _frameInfos[i].IsForecast ? forecastBrush : currentBrush;
                    }
                    else if (i < _currentFrameIndex)
                    {
                        segments[i].Background = passedBrush;
                    }
                    else
                    {
                        segments[i].Background = trackBrush;
                    }
                }
            }
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            if (_renderedFrames.Count <= 1)
            {
                return;
            }

            int lastIdx = _renderedFrames.Count - 1;
            if (_currentFrameIndex == lastIdx)
            {
                _liveHoldTicks++;
                if (_liveHoldTicks < 2)
                {
                    return;
                }
                _liveHoldTicks = 0;
                _currentFrameIndex = 0;
            }
            else
            {
                _currentFrameIndex++;
            }

            DisplayFrame(_currentFrameIndex);
        }

        private void TimelineSegment_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border &&
                border.Tag is string tagStr &&
                int.TryParse(tagStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
                index >= 0 &&
                index < _renderedFrames.Count)
            {
                _currentFrameIndex = index;
                _liveHoldTicks = 0;
                DisplayFrame(_currentFrameIndex);
            }
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = !_isPlaying;
            BtnPlayPause.Content = _isPlaying ? "\uE769" : "\uE768";

            if (_isPlaying && _renderedFrames.Count > 1)
            {
                _animTimer.Start();
            }
            else
            {
                _animTimer.Stop();
            }
        }

        private void BtnMapThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _userOverrodeMapTheme = true;
            _darkMapMode = !_darkMapMode;
            if (_palette is not null)
            {
                ApplyPalette(_palette, _darkMapMode);
            }
            _ = LoadRadarDataAsync();
        }

        private void BtnZoomToggle_Click(object sender, RoutedEventArgs e)
        {
            int nextZoom = _zoom >= 8 ? 5 : _zoom + 1;
            SetZoomLevel(nextZoom);
        }

        private void MapFrameBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            int delta = e.Delta > 0 ? 1 : -1;
            int nextZoom = Math.Clamp(_zoom + delta, 5, 8);
            if (nextZoom != _zoom)
            {
                SetZoomLevel(nextZoom);
            }
        }

        private void SetZoomLevel(int zoom)
        {
            _zoom = Math.Clamp(zoom, 5, 8);
            UpdateHudLabels();
            ZoomChanged?.Invoke(_zoom);
            _ = LoadRadarDataAsync();
        }

        private void BtnRefreshRadar_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadRadarDataAsync();
        }

        private sealed record TileLayer(double Left, double Top, BitmapSource Image);
    }
}
