using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Dsk.Weather.Models;
using Dsk.Weather.Services;

namespace Dsk.Weather.Windows
{
    public partial class WidgetWindow : Window
    {
        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");

        public static readonly DependencyProperty AnimatedHourlyOffsetProperty =
            DependencyProperty.Register(
                nameof(AnimatedHourlyOffset),
                typeof(double),
                typeof(WidgetWindow),
                new PropertyMetadata(0.0, OnAnimatedHourlyOffsetChanged));

        private readonly SettingsService _settingsService;
        private readonly WeatherService _weatherService;
        private readonly MaterialColorService _colorService;
        private readonly DesktopPinService _pinService;
        private readonly AutostartService _autostartService;
        private readonly OfficialAlertService _officialAlertService = new OfficialAlertService();
        private readonly Action _openSettingsAction;
        private readonly Action _exitAction;

        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _weatherTimer;
        private readonly DispatcherTimer _wallpaperWatchTimer;

        private WidgetSettings _settings;
        private WeatherSnapshot? _currentWeather;
        private OfficialAlertFetchResult? _officialAlertResult;
        private MaterialThemePalette _currentPalette;
        private CancellationTokenSource? _weatherFetchCts;

        private string _lastDisplayedClock = string.Empty;
        private string _lastDisplayedMinute = string.Empty;
        private string _lastDisplayedDate = string.Empty;
        private string? _lastWallpaperPath;
        private DateTime _lastWallpaperWriteUtc;
        private DateTime _lastAutoDetectUtc = DateTime.MinValue;
        private bool _isDrawerOpen;
        private bool _isRadarTabActive;
        private int _weatherRequestVersion;
        private int _startupRetryCount;
        private bool _isHourlyPointerDown;
        private bool _isHourlyDragging;
        private bool _isHourlyScrollAnimating;
        private Point _hourlyDragStart;
        private double _hourlyStartOffset;
        private double _hourlyScrollTarget;

        public double AnimatedHourlyOffset
        {
            get => (double)GetValue(AnimatedHourlyOffsetProperty);
            set => SetValue(AnimatedHourlyOffsetProperty, value);
        }

        private static void OnAnimatedHourlyOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WidgetWindow window)
            {
                window.HourlyScrollViewer.ScrollToHorizontalOffset((double)e.NewValue);
            }
        }

        public WidgetWindow(
            WidgetSettings settings,
            SettingsService settingsService,
            WeatherService weatherService,
            MaterialColorService colorService,
            DesktopPinService pinService,
            AutostartService autostartService,
            Action openSettingsAction,
            Action exitAction)
        {
            InitializeComponent();

            _settings = settings.Clone();
            _settings.Normalize();
            _settingsService = settingsService;
            _weatherService = weatherService;
            _colorService = colorService;
            _pinService = pinService;
            _autostartService = autostartService;
            _openSettingsAction = openSettingsAction;
            _exitAction = exitAction;

            _currentPalette = _colorService.BuildPalette(_settings.ThemeMode);

            RadarView.ZoomChanged += zoom =>
            {
                _settings.RadarZoom = zoom;
                _settingsService.Save(_settings);
            };

            _clockTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _clockTimer.Tick += (_, _) =>
            {
                UpdateClockDisplay();
                _pinService.CheckDesktopForegroundState();
            };

            _weatherTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMinutes(15)
            };
            _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();

            _wallpaperWatchTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _wallpaperWatchTimer.Tick += (_, _) => CheckWallpaperChanges();
        }

        public WidgetSettings CurrentSettings => _settings;
        public MaterialThemePalette CurrentPalette => _currentPalette;

        private void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CaptureWallpaperSignature();
            ApplyThemeAndLayout();
            UpdateClockDisplay(force: true);
            RestoreOrInitializePosition();

            _pinService.Attach(this, _settings.PinMode, () => Dispatcher.Invoke(() =>
            {
                CaptureWallpaperSignature();
                if (_settings.ThemeMode == WidgetThemeMode.DynamicWallpaper)
                {
                    ApplyThemeAndLayout();
                }
            }));

            _clockTimer.Start();
            _weatherTimer.Start();
            _wallpaperWatchTimer.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

            WeatherSnapshot? cached = _settingsService.LoadWeatherCache();
            if (cached is not null)
            {
                cached.IsFromCache = true;
                _currentWeather = cached;
                ApplyThemeAndLayout();
                UpdateWeatherDisplay();
            }

            _ = InitializeLocationAndWeatherAsync();
        }

        private void WidgetWindow_Closed(object? sender, EventArgs e)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            _clockTimer.Stop();
            _weatherTimer.Stop();
            _wallpaperWatchTimer.Stop();
            _weatherFetchCts?.Cancel();
            _weatherFetchCts?.Dispose();
            WeatherScene.Stop();
            RadarView.Deactivate();
            _pinService.Detach();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    UpdateClockDisplay(force: true);
                    _ = RefreshWeatherWithAutoLocationAsync();
                }));
            }
        }

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (e.IsAvailable)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _ = RefreshWeatherWithAutoLocationAsync();
                }));
            }
        }

        public void ApplySettings(WidgetSettings updated, bool refreshWeather)
        {
            bool pinChanged = _settings.PinMode != updated.PinMode;
            bool locationChanged =
                Math.Abs(_settings.Latitude - updated.Latitude) > 0.0001 ||
                Math.Abs(_settings.Longitude - updated.Longitude) > 0.0001;
            bool themeOrLocationChanged =
                _settings.ThemeMode != updated.ThemeMode ||
                locationChanged;
            bool languageChanged = !string.Equals(_settings.Language, updated.Language, StringComparison.OrdinalIgnoreCase);

            if (locationChanged)
            {
                _officialAlertResult = null;
            }

            _settings = updated.Clone();
            _settings.Normalize();

            ApplyThemeAndLayout();
            UpdateClockDisplay(force: true);
            UpdateWeatherDisplay();

            if (pinChanged)
            {
                _pinService.ApplyMode(_settings.PinMode);
            }

            ClampAndPersistCurrentPosition();

            if (_isDrawerOpen && _isRadarTabActive && themeOrLocationChanged)
            {
                _ = ActivateRadarAsync();
            }

            if (refreshWeather || (languageChanged && _settings.EnableWeatherAlerts))
            {
                _ = RefreshWeatherAsync();
            }
        }

        public void CenterOnPrimaryScreen()
        {
            UpdateLayout();
            Rect workArea = SystemParameters.WorkArea;
            double targetLeft = workArea.Left + Math.Max(0.0, (workArea.Width - ActualWidth) / 2.0);
            double targetTop = workArea.Top + Math.Max(0.0, (workArea.Height - ActualHeight) / 2.0);

            Left = Math.Round(targetLeft);
            Top = Math.Round(targetTop);
            _settings.Left = Left;
            _settings.Top = Top;
            _settingsService.Save(_settings);
        }

        public void ResetToDefaultPosition()
        {
            UpdateLayout();
            Rect workArea = SystemParameters.WorkArea;
            double targetLeft = workArea.Right - ActualWidth - 48.0;
            double targetTop = workArea.Top + 40.0;

            (double clampedLeft, double clampedTop) = SettingsService.ClampPosition(
                targetLeft,
                targetTop,
                ActualWidth,
                ActualHeight,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            Left = Math.Round(clampedLeft);
            Top = Math.Round(clampedTop);
            _settings.Left = Left;
            _settings.Top = Top;
            _settingsService.Save(_settings);
        }

        private async Task InitializeLocationAndWeatherAsync()
        {
            if (!_settings.LocationInitialized || string.IsNullOrWhiteSpace(_settings.CityName))
            {
                GeoLocationResult detected = await _weatherService.DetectLocationByIpAsync(_settings.Language);
                _settings.AddOrSelectLocation(detected.Name, detected.Country, detected.Latitude, detected.Longitude);
                _settingsService.Save(_settings);
                _lastAutoDetectUtc = DateTime.UtcNow;
                UpdateWeatherDisplay();
            }
            else if (_settings.AutoDetectLocation)
            {
                await TryAutoUpdateLocationAsync();
            }

            await RefreshWeatherAsync();
        }

        private async Task RefreshWeatherWithAutoLocationAsync()
        {
            if (_settings.AutoDetectLocation)
            {
                await TryAutoUpdateLocationAsync();
            }

            await RefreshWeatherAsync();
        }

        private async Task TryAutoUpdateLocationAsync()
        {
            if (!_settings.AutoDetectLocation)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastAutoDetectUtc) < TimeSpan.FromMinutes(2))
            {
                return;
            }

            _lastAutoDetectUtc = DateTime.UtcNow;
            GeoLocationResult? detected = await _weatherService.TryDetectLocationByIpAsync(_settings.Language);
            if (detected is null)
            {
                return;
            }

            bool moved =
                Math.Abs(_settings.Latitude - detected.Latitude) >= 0.03 ||
                Math.Abs(_settings.Longitude - detected.Longitude) >= 0.03;

            if (!moved)
            {
                return;
            }

            _officialAlertResult = null;
            _settings.AddOrSelectLocation(detected.Name, detected.Country, detected.Latitude, detected.Longitude);
            _settingsService.Save(_settings);
            UpdateWeatherDisplay();
            UpdateQuickCitySwitchHints();
            if (_isDrawerOpen && _isRadarTabActive)
            {
                _ = ActivateRadarAsync();
            }
        }

        public async Task RefreshWeatherAsync()
        {
            int requestVersion = ++_weatherRequestVersion;
            _weatherFetchCts?.Cancel();
            _weatherFetchCts?.Dispose();
            _weatherFetchCts = new CancellationTokenSource();
            CancellationToken ct = _weatherFetchCts.Token;

            try
            {
                if (_currentWeather is null)
                {
                    string updatingText = LocalizationService.T(_settings.Language, "updating_weather");
                    CardSummaryText.Text = updatingText;
                    MinimalSubText.Text = updatingText;
                }

                string city = string.IsNullOrWhiteSpace(_settings.CityName) ? "Roma" : _settings.CityName;
                Task<WeatherSnapshot?> weatherTask = _weatherService.FetchForecastAsync(
                    _settings.Latitude,
                    _settings.Longitude,
                    city,
                    WeatherModelPreference.Auto,
                    ct);

                Task<OfficialAlertFetchResult> officialAlertTask = _settings.EnableWeatherAlerts
                    ? SafeFetchOfficialAlertsAsync(_settings.Latitude, _settings.Longitude, _settings.Language, ct)
                    : Task.FromResult(new OfficialAlertFetchResult(false, null));

                await Task.WhenAll(weatherTask, officialAlertTask);

                WeatherSnapshot? result = weatherTask.Result;
                OfficialAlertFetchResult alertResult = officialAlertTask.Result;

                if (ct.IsCancellationRequested || requestVersion != _weatherRequestVersion)
                {
                    return;
                }

                if (result is not null)
                {
                    _currentWeather = result;
                }
                _officialAlertResult = alertResult;

                ApplyThemeAndLayout();
                UpdateWeatherDisplay();

                if ((result is null || result.IsFromCache) && _startupRetryCount < 2)
                {
                    _startupRetryCount++;
                    _ = ScheduleQuickWeatherRetryAsync(requestVersion);
                }
                else if (result is { IsFromCache: false })
                {
                    _startupRetryCount = 0;
                }
            }
            catch (OperationCanceledException)
            {
                // Ignora cancellazione da refresh successivo
            }
        }

        private async Task ScheduleQuickWeatherRetryAsync(int originVersion)
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (originVersion == _weatherRequestVersion && (_currentWeather is null || _currentWeather.IsFromCache))
            {
                await RefreshWeatherAsync();
            }
        }

        private void CardContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double w = e.NewSize.Width;
            double h = e.NewSize.Height;
            if (w > 0 && h > 0)
            {
                var clip = new RectangleGeometry(new Rect(0, 0, w, h), 27.0, 27.0);
                clip.Freeze();
                CardContentHost.Clip = clip;
            }
        }

        private void RestoreOrInitializePosition()
        {
            UpdateLayout();
            if (double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top))
            {
                ResetToDefaultPosition();
                return;
            }

            (double clampedLeft, double clampedTop) = SettingsService.ClampPosition(
                _settings.Left,
                _settings.Top,
                ActualWidth,
                ActualHeight,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            Left = clampedLeft;
            Top = clampedTop;
            _settings.Left = Left;
            _settings.Top = Top;
        }

        private void ClampAndPersistCurrentPosition()
        {
            UpdateLayout();
            (double clampedLeft, double clampedTop) = SettingsService.ClampPosition(
                Left,
                Top,
                ActualWidth,
                ActualHeight,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            Left = clampedLeft;
            Top = clampedTop;
            _settings.Left = Left;
            _settings.Top = Top;
            _settingsService.Save(_settings);
        }

        private void ApplyThemeAndLayout()
        {
            _currentPalette = _colorService.BuildPalette(_settings.ThemeMode);
            WidgetScaleTransform.ScaleX = _settings.Scale;
            WidgetScaleTransform.ScaleY = _settings.Scale;

            LayoutPixelCard.Visibility = _settings.LayoutStyle == WidgetLayoutStyle.PixelCard
                ? Visibility.Visible
                : Visibility.Collapsed;
            LayoutPixelMinimal.Visibility = _settings.LayoutStyle == WidgetLayoutStyle.PixelMinimalTransparent
                ? Visibility.Visible
                : Visibility.Collapsed;
            LayoutMaterialCompact.Visibility = _settings.LayoutStyle == WidgetLayoutStyle.MaterialCompact
                ? Visibility.Visible
                : Visibility.Collapsed;

            SolidColorBrush textBrush = MaterialThemePalette.ToBrush(_currentPalette.OnSurface);
            SolidColorBrush mutedBrush = MaterialThemePalette.ToBrush(_currentPalette.OnSurfaceMuted);
            SolidColorBrush primaryBrush = MaterialThemePalette.ToBrush(_currentPalette.Primary);
            SolidColorBrush borderBrush = MaterialThemePalette.ToBrush(_currentPalette.SurfaceBorder, 0.55);
            SolidColorBrush pillBrush = MaterialThemePalette.ToBrush(_currentPalette.SurfaceVariant, 0.85);
            SolidColorBrush cardSurfaceBrush = MaterialThemePalette.ToBrush(_currentPalette.Surface, _settings.BackgroundOpacity);
            double drawerOpacity = Math.Max(0.84, _settings.BackgroundOpacity);
            SolidColorBrush drawerSurfaceBrush = MaterialThemePalette.ToBrush(_currentPalette.Surface, drawerOpacity);

            bool isMinimal = _settings.LayoutStyle == WidgetLayoutStyle.PixelMinimalTransparent;
            bool isCompact = _settings.LayoutStyle == WidgetLayoutStyle.MaterialCompact;
            WeatherSnapshot? atmosphereWeather = _currentWeather;

            LayoutPixelCard.Margin = _settings.ShowHourlyStrip
                ? new Thickness(18, 14, 18, 0)
                : new Thickness(18, 14, 18, 13);
            LayoutPixelMinimal.Margin = _settings.ShowHourlyStrip
                ? new Thickness(18, 14, 18, 0)
                : new Thickness(18, 14, 18, 14);
            LayoutMaterialCompact.Margin = _settings.ShowHourlyStrip
                ? new Thickness(18, 12, 18, 0)
                : new Thickness(18, 14, 18, 14);

            if (isMinimal)
            {
                // Alpha = 1 invisibile ma garantisce hit-testing mouse su tutta la card layered
                MainCard.Background = MaterialThemePalette.ToBrush(Color.FromArgb(1, 0, 0, 0));
                MainCard.BorderBrush = Brushes.Transparent;
                MainCard.BorderThickness = new Thickness(0);
                MainCard.Effect = null;

                var whiteBrush = MaterialThemePalette.ToBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                var softWhiteBrush = MaterialThemePalette.ToBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                MinimalClockText.Foreground = whiteBrush;
                MinimalSecondsText.Foreground = softWhiteBrush;
                MinimalAmPmText.Foreground = softWhiteBrush;
                MinimalDateText.Foreground = whiteBrush;
                MinimalWeatherText.Foreground = whiteBrush;
                MinimalSubText.Foreground = softWhiteBrush;
            }
            else
            {
                MainCard.Background = _settings.EnableDynamicAtmosphere
                    ? MaterialColorService.BuildAtmosphereBrush(
                        atmosphereWeather,
                        _currentPalette,
                        _settings.ThemeMode,
                        _settings.BackgroundOpacity,
                        DateTime.Now)
                    : cardSurfaceBrush;
                MainCard.BorderBrush = borderBrush;
                MainCard.BorderThickness = new Thickness(1);
                MainCard.Effect = (System.Windows.Media.Effects.Effect)FindResource("CardDropShadow");
            }

            // Stile Card Completa
            CardDateText.Foreground = textBrush;
            CardWeatherPill.Background = pillBrush;
            CardPillTempText.Foreground = textBrush;
            CardPillRangeText.Foreground = mutedBrush;
            CardClockText.Foreground = textBrush;
            CardSecondsText.Foreground = mutedBrush;
            CardAmPmText.Foreground = primaryBrush;
            CardInsightText.Foreground = primaryBrush;
            CardSummaryText.Foreground = mutedBrush;

            // Adatta stile e spaziatura della striscia 24h al layout attivo
            CardHourlyContainer.Visibility = _settings.ShowHourlyStrip ? Visibility.Visible : Visibility.Collapsed;
            if (isMinimal)
            {
                CardHourlyContainer.Effect = (System.Windows.Media.Effects.Effect)FindResource("MinimalTextShadow");
                CardHourlyContainer.BorderBrush = MaterialThemePalette.ToBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
                CardHourlyContainer.Margin = new Thickness(18, 10, 18, 10);
                CardHourlyContainer.Padding = new Thickness(0, 8, 0, 0);
            }
            else if (isCompact)
            {
                CardHourlyContainer.Effect = null;
                CardHourlyContainer.BorderBrush = MaterialThemePalette.ToBrush(_currentPalette.SurfaceBorder, 0.35);
                CardHourlyContainer.Margin = new Thickness(18, 8, 18, 8);
                CardHourlyContainer.Padding = new Thickness(0, 6, 0, 0);
            }
            else
            {
                CardHourlyContainer.Effect = null;
                CardHourlyContainer.BorderBrush = MaterialThemePalette.ToBrush(_currentPalette.SurfaceBorder, 0.45);
                CardHourlyContainer.Margin = new Thickness(18, 10, 18, 10);
                CardHourlyContainer.Padding = new Thickness(0, 8, 0, 0);
            }

            ApplyHourlyCurvesVisibility();

            // Stile MaterialCompact (pulito senza riquadro opaco invasivo)
            CompactClockText.Foreground = textBrush;
            CompactSecondsText.Foreground = mutedBrush;
            CompactAmPmText.Foreground = primaryBrush;
            CompactDateText.Foreground = mutedBrush;
            CompactDivider.Background = MaterialThemePalette.ToBrush(_currentPalette.SurfaceBorder, 0.5);
            CompactWeatherPill.Background = Brushes.Transparent;
            CompactTempText.Foreground = textBrush;
            CompactRangeText.Foreground = mutedBrush;
            CompactLocationText.Foreground = mutedBrush;

            // Drawer 7 giorni + Radar
            ForecastDrawer.Background = _settings.EnableDynamicAtmosphere
                ? MaterialColorService.BuildAtmosphereBrush(
                    atmosphereWeather,
                    _currentPalette,
                    _settings.ThemeMode,
                    drawerOpacity,
                    DateTime.Now)
                : drawerSurfaceBrush;
            ForecastDrawer.BorderBrush = borderBrush;
            DrawerMetaText.Foreground = mutedBrush;

            SolidColorBrush chipBg = MaterialThemePalette.ToBrush(_currentPalette.SurfaceVariant, 0.72);
            ChipSunBorder.Background = chipBg;
            ChipUvBorder.Background = chipBg;
            ChipWindBorder.Background = chipBg;
            ChipSunTitle.Foreground = mutedBrush;
            ChipUvTitle.Foreground = mutedBrush;
            ChipWindTitle.Foreground = mutedBrush;
            ChipSunText.Foreground = textBrush;
            ChipUvText.Foreground = textBrush;
            ChipWindText.Foreground = textBrush;
            ChipWindArrow.Fill = primaryBrush;

            UpdateDrawerTabVisuals();
            RadarView.ApplyPalette(_currentPalette, IsRadarNightMode());

            // Stile pulsante discreto hover e indicatore scorrimento orario
            SolidColorBrush btnBg = isMinimal
                ? MaterialThemePalette.ToBrush(Color.FromArgb(0x99, 0x0F, 0x17, 0x2A))
                : (_settings.ThemeMode == WidgetThemeMode.MaterialLight
                    ? MaterialThemePalette.ToBrush(Color.FromRgb(0xE2, 0xE8, 0xF0), 0.90)
                    : MaterialThemePalette.ToBrush(Color.FromRgb(0x0F, 0x17, 0x2A), 0.55));
            SolidColorBrush minimalBtnFg = MaterialThemePalette.ToBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            SolidColorBrush minimalBtnBorder = MaterialThemePalette.ToBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

            BtnSettings.Background = btnBg;
            BtnSettings.Foreground = isMinimal ? minimalBtnFg : textBrush;
            BtnSettings.BorderBrush = isMinimal ? minimalBtnBorder : borderBrush;

            HourlyScrollTrack.Background = isMinimal
                ? MaterialThemePalette.ToBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))
                : MaterialThemePalette.ToBrush(_currentPalette.SurfaceVariant, 0.35);
            HourlyScrollThumb.Background = isMinimal
                ? MaterialThemePalette.ToBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF))
                : MaterialThemePalette.ToBrush(_currentPalette.Primary, 0.85);
            UpdateHourlyScrollIndicators();

            // Animazioni meteo (sfondo card e icone hero)
            bool animateIcons = _settings.EnableWeatherAnimations;
            CardWeatherIcon.IconKind = null;
            MinimalWeatherIcon.IconKind = null;
            CompactWeatherIcon.IconKind = null;
            CardWeatherIcon.Animate = animateIcons;
            MinimalWeatherIcon.Animate = animateIcons;
            CompactWeatherIcon.Animate = animateIcons;

            WeatherIconKind sceneKind = _currentWeather is not null
                ? WeatherService.MapWmoToIcon(_currentWeather.WeatherCode, _currentWeather.IsDay)
                : WeatherIconKind.ClearDay;
            bool sceneEnabled = _settings.EnableWeatherAnimations &&
                                _settings.LayoutStyle != WidgetLayoutStyle.PixelMinimalTransparent;
            WeatherScene.Configure(sceneKind, sceneEnabled, _settings.ThemeMode == WidgetThemeMode.MaterialLight);
            ApplyLanguageToChrome();
            UpdateAlertBadge();
            UpdateQuickCitySwitchHints();
            RebuildHourlyVisuals(resetScroll: false);
        }

        private void ApplyLanguageToChrome()
        {
            string lang = _settings.Language;
            TabForecastLabel.Text = LocalizationService.T(lang, "tab_forecast");
            TabRadarLabel.Text = LocalizationService.T(lang, "tab_radar");
            BtnCloseDrawer.ToolTip = LocalizationService.T(lang, "close_panel_tooltip");
            BtnSettings.ToolTip = LocalizationService.T(lang, "settings_tooltip");

            string forecastTip = LocalizationService.T(lang, "click_forecast_tooltip");
            CardWeatherPill.ToolTip = forecastTip;
            MinimalWeatherPill.ToolTip = forecastTip;
            CompactWeatherPill.ToolTip = forecastTip;

            ChipSunTitle.Text = LocalizationService.T(lang, "chip_sun_title");
            ChipUvTitle.Text = LocalizationService.T(lang, "chip_uv_title");
            ChipWindTitle.Text = LocalizationService.T(lang, "chip_wind_title");
            RadarView.ApplyLanguage(lang);
        }

        private void UpdateDrawerTabVisuals()
        {
            SolidColorBrush activeBg = MaterialThemePalette.ToBrush(_currentPalette.PrimaryContainer, 0.95);
            SolidColorBrush activeFg = MaterialThemePalette.ToBrush(_currentPalette.Primary);
            SolidColorBrush inactiveBg = MaterialThemePalette.ToBrush(_currentPalette.SurfaceVariant, 0.55);
            SolidColorBrush inactiveFg = MaterialThemePalette.ToBrush(_currentPalette.OnSurfaceMuted);

            TabForecastPill.Background = _isRadarTabActive ? inactiveBg : activeBg;
            TabForecastLabel.Foreground = _isRadarTabActive ? inactiveFg : activeFg;

            TabRadarPill.Background = _isRadarTabActive ? activeBg : inactiveBg;
            TabRadarLabel.Foreground = _isRadarTabActive ? activeFg : inactiveFg;
        }

        private void UpdateClockDisplay(bool force = false)
        {
            DateTime now = DateTime.Now;
            string clockStr = _settings.Use24HourFormat
                ? now.ToString("HH:mm", CultureInfo.InvariantCulture)
                : now.ToString("hh:mm", CultureInfo.InvariantCulture);

            string secondsStr = _settings.ShowSeconds ? now.ToString(":ss", CultureInfo.InvariantCulture) : string.Empty;
            string amPmStr = _settings.Use24HourFormat ? string.Empty : now.ToString("tt", CultureInfo.InvariantCulture).ToUpperInvariant();
            string clockKey = $"{clockStr}{secondsStr}|{amPmStr}";

            if (force || !string.Equals(_lastDisplayedClock, clockKey, StringComparison.Ordinal))
            {
                bool minuteChanged = !force && !string.Equals(_lastDisplayedMinute, clockStr, StringComparison.Ordinal);
                _lastDisplayedClock = clockKey;
                _lastDisplayedMinute = clockStr;

                CardClockText.Text = clockStr;
                MinimalClockText.Text = clockStr;
                CompactClockText.Text = clockStr;

                if (minuteChanged && _settings.EnableWeatherAnimations)
                {
                    var fade = new DoubleAnimation(0.68, 1.0, TimeSpan.FromMilliseconds(240))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    CardClockText.BeginAnimation(OpacityProperty, fade);
                    MinimalClockText.BeginAnimation(OpacityProperty, fade);
                    CompactClockText.BeginAnimation(OpacityProperty, fade);
                }

                Visibility secVis = _settings.ShowSeconds ? Visibility.Visible : Visibility.Collapsed;
                CardSecondsText.Text = secondsStr;
                CardSecondsText.Visibility = secVis;
                MinimalSecondsText.Text = secondsStr;
                MinimalSecondsText.Visibility = secVis;
                CompactSecondsText.Text = secondsStr;
                CompactSecondsText.Visibility = secVis;

                Visibility amPmVis = _settings.Use24HourFormat ? Visibility.Collapsed : Visibility.Visible;
                CardAmPmText.Text = amPmStr;
                CardAmPmText.Visibility = amPmVis;
                MinimalAmPmText.Text = amPmStr;
                MinimalAmPmText.Visibility = amPmVis;
                CompactAmPmText.Text = amPmStr;
                CompactAmPmText.Visibility = amPmVis;
            }

            CultureInfo culture = LocalizationService.GetCulture(_settings.Language);
            string dateFull = CapitalizeFirst(now.ToString("dddd, d MMMM", culture), culture);
            if (force || !string.Equals(_lastDisplayedDate, dateFull, StringComparison.Ordinal))
            {
                _lastDisplayedDate = dateFull;
                CardDateText.Text = dateFull;
                MinimalDateText.Text = dateFull;
                CompactDateText.Text = CapitalizeFirst(now.ToString("ddd d MMM", culture), culture);
            }
        }

        private void UpdateWeatherDisplay()
        {
            string lang = _settings.Language;
            CultureInfo culture = LocalizationService.GetCulture(lang);

            if (_currentWeather is null)
            {
                string fallbackCity = string.IsNullOrWhiteSpace(_settings.CityName)
                    ? LocalizationService.T(lang, "detecting")
                    : _settings.CityName;
                CardPillTempText.Text = "--°";
                CardPillRangeText.Text = string.Empty;
                CardInsightText.Text = LocalizationService.T(lang, "waiting_weather");
                CardSummaryText.Text = fallbackCity;
                MinimalWeatherText.Text = "--°";
                MinimalSubText.Text = fallbackCity;
                CompactTempText.Text = "--°";
                CompactRangeText.Text = string.Empty;
                CompactLocationText.Text = fallbackCity;
                ChipSunText.Text = "-- / --";
                ChipUvText.Text = "--";
                ChipWindText.Text = "--";
                CardHourlyItems.ItemsSource = null;
                HourlySparklineCanvas.Children.Clear();
                DailyForecastItems.ItemsSource = null;
                return;
            }

            WeatherSnapshot w = _currentWeather;
            TemperatureUnit unit = _settings.TempUnit;
            string currentTemp = WeatherService.FormatTemp(w.TemperatureC, unit);
            string apparentTemp = WeatherService.FormatTemp(w.ApparentTemperatureC, unit);
            string maxTemp = WeatherService.FormatTemp(w.TodayMaxC, unit);
            string minTemp = WeatherService.FormatTemp(w.TodayMinC, unit);
            string condition = WeatherService.DescribeWmoCodeItalian(w.WeatherCode, lang);
            string city = string.IsNullOrWhiteSpace(w.CityName) ? _settings.CityName : w.CityName;
            string cacheTag = w.IsFromCache ? LocalizationService.T(lang, "cache_tag") : string.Empty;
            string feelsLike = LocalizationService.Format(lang, "feels_like", apparentTemp);
            string insight = WeatherService.BuildAtAGlanceInsight(w, DateTime.Now, lang);

            // Card Completa
            CardWeatherIcon.WeatherCode = w.WeatherCode;
            CardWeatherIcon.IsDay = w.IsDay;
            CardPillTempText.Text = currentTemp;
            CardPillRangeText.Text = $"{maxTemp}/{minTemp}";
            CardInsightText.Text = insight;
            CardInsightText.Foreground = MaterialThemePalette.ToBrush(_currentPalette.Primary);
            CardSummaryText.Text = $"{city} · {condition} · {feelsLike}{cacheTag}";

            // Minimal
            MinimalWeatherIcon.WeatherCode = w.WeatherCode;
            MinimalWeatherIcon.IsDay = w.IsDay;
            MinimalWeatherText.Text = $"{currentTemp} · {condition}";
            MinimalSubText.Text = $"{city} · {insight}{cacheTag}";

            // Compact
            CompactWeatherIcon.WeatherCode = w.WeatherCode;
            CompactWeatherIcon.IsDay = w.IsDay;
            CompactTempText.Text = currentTemp;
            CompactRangeText.Text = $"{maxTemp}/{minTemp}";
            CompactLocationText.Text = $"{city} · {condition}";

            // Drawer header + chip dettagli
            DrawerMetaText.Text = $"{city} · {condition} · {feelsLike}{cacheTag}";

            string sunriseStr = w.SunriseLocal?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";
            string sunsetStr = w.SunsetLocal?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";
            ChipSunText.Text = $"{sunriseStr} / {sunsetStr}";

            string uvNum = w.CurrentUvIndex.ToString("0.#", CultureInfo.InvariantCulture);
            ChipUvText.Text = $"{uvNum} · {WeatherService.DescribeUvLevelItalian(w.CurrentUvIndex, lang)}";

            string currentCardinal = WeatherService.DescribeWindDirectionCardinal(w.WindDirectionDeg, lang);
            ChipWindArrowRotate.Angle = (w.WindDirectionDeg + 180) % 360;
            ChipWindBorder.ToolTip = LocalizationService.Format(lang, "wind_tooltip", currentCardinal, w.WindDirectionDeg, w.RelativeHumidity);
            ChipWindText.Text = $"{Math.Round(w.WindSpeedKmh).ToString("0", CultureInfo.InvariantCulture)} km/h · {w.RelativeHumidity}%";

            SolidColorBrush primaryText = MaterialThemePalette.ToBrush(_currentPalette.OnSurface);
            SolidColorBrush mutedText = MaterialThemePalette.ToBrush(_currentPalette.OnSurfaceMuted);
            SolidColorBrush accentBrush = MaterialThemePalette.ToBrush(_currentPalette.Primary);

            // Striscia oraria (prossime 24 ore scorribili + % pioggia + curva termica, adattata al layout attivo)
            RebuildHourlyVisuals(resetScroll: true);

            // Drawer 7 giorni (formato "Lun 23", gocciolina + % pioggia sempre visibile, triangolino direzione vento + km/h, min/max)
            var dailyVms = new List<DailyItemViewModel>(w.Daily.Count);
            for (int i = 0; i < w.Daily.Count; i++)
            {
                DailyForecastPoint dp = w.Daily[i];
                string dayName = CapitalizeFirst(dp.Date.ToString("ddd d", culture), culture).Replace(".", string.Empty);
                string dayCardinal = WeatherService.DescribeWindDirectionCardinal(dp.WindDirectionDeg, lang);
                string windSpeedStr = $"{Math.Round(dp.WindSpeedMaxKmh).ToString("0", CultureInfo.InvariantCulture)} km/h";

                dailyVms.Add(new DailyItemViewModel
                {
                    DayLabel = dayName,
                    WeatherCode = dp.WeatherCode,
                    ConditionLabel = WeatherService.DescribeWmoCodeItalian(dp.WeatherCode, lang),
                    PrecipLabel = $"{dp.PrecipProbabilityMax}%",
                    PrecipVisibility = Visibility.Visible,
                    WindLabel = windSpeedStr,
                    WindArrowAngle = (dp.WindDirectionDeg + 180) % 360,
                    WindTooltip = LocalizationService.Format(lang, "wind_daily_tooltip", dayCardinal, dp.WindDirectionDeg, windSpeedStr),
                    MinLabel = WeatherService.FormatTemp(dp.MinC, unit),
                    MaxLabel = WeatherService.FormatTemp(dp.MaxC, unit),
                    PrimaryTextBrush = primaryText,
                    MutedBrush = mutedText,
                    AccentBrush = dp.PrecipProbabilityMax >= 15 ? accentBrush : mutedText
                });
            }

            DailyForecastItems.ItemsSource = dailyVms;
            UpdateAlertBadge();
            UpdateQuickCitySwitchHints();
        }

        private void ApplyHourlyCurvesVisibility()
        {
            if (_settings is null ||
                HourlySparklineCanvas is null ||
                HourlyPrecipCanvas is null ||
                HourlyHumidityCanvas is null ||
                HourlyCurvesLegend is null)
            {
                return;
            }

            string lang = _settings.Language;
            bool showTemp = _settings.ShowHourlyTempCurve;
            bool showPrecip = _settings.ShowHourlyPrecipCurve;
            bool showHumidity = _settings.ShowHourlyHumidityCurve;

            HourlySparklineCanvas.Visibility = showTemp ? Visibility.Visible : Visibility.Collapsed;
            HourlyPrecipCanvas.Visibility = showPrecip ? Visibility.Visible : Visibility.Collapsed;
            HourlyHumidityCanvas.Visibility = showHumidity ? Visibility.Visible : Visibility.Collapsed;

            bool showLegend = showPrecip || showHumidity;
            HourlyCurvesLegend.Visibility = showLegend ? Visibility.Visible : Visibility.Collapsed;
            if (!showLegend)
            {
                return;
            }

            bool isMinimal = _settings.LayoutStyle == WidgetLayoutStyle.PixelMinimalTransparent;
            bool isLightTheme = !isMinimal && _settings.ThemeMode == WidgetThemeMode.MaterialLight;
            Color tempColor = isMinimal ? Color.FromRgb(0xF8, 0xFA, 0xFC) : _currentPalette.Primary;
            Color precipColor = isLightTheme ? Color.FromRgb(0x02, 0x84, 0xC7) : Color.FromRgb(0x38, 0xBD, 0xF8);
            Color humidityColor = isLightTheme ? Color.FromRgb(0x0F, 0x76, 0x6E) : Color.FromRgb(0x2D, 0xD4, 0xBF);
            SolidColorBrush legendTextBrush = MaterialThemePalette.ToBrush(
                isMinimal ? Color.FromRgb(0xE2, 0xE8, 0xF0) : _currentPalette.OnSurfaceMuted);

            LegendTempItem.Visibility = showTemp ? Visibility.Visible : Visibility.Collapsed;
            LegendPrecipItem.Visibility = showPrecip ? Visibility.Visible : Visibility.Collapsed;
            LegendHumidityItem.Visibility = showHumidity ? Visibility.Visible : Visibility.Collapsed;

            LegendTempDot.Fill = MaterialThemePalette.ToBrush(tempColor);
            LegendPrecipDot.Fill = MaterialThemePalette.ToBrush(precipColor);
            LegendHumidityDot.Fill = MaterialThemePalette.ToBrush(humidityColor);

            LegendTempText.Foreground = legendTextBrush;
            LegendPrecipText.Foreground = legendTextBrush;
            LegendHumidityText.Foreground = legendTextBrush;

            LegendTempText.Text = LocalizationService.T(lang, "curve_legend_temp");
            LegendPrecipText.Text = LocalizationService.T(lang, "curve_legend_precip");
            LegendHumidityText.Text = LocalizationService.T(lang, "curve_legend_humidity");
        }

        private void RebuildHourlyVisuals(bool resetScroll)
        {
            ApplyHourlyCurvesVisibility();

            if (_currentWeather is null)
            {
                CardHourlyItems.ItemsSource = null;
                HourlySparklineCanvas.Children.Clear();
                HourlySparklineCanvas.Width = 0;
                HourlyPrecipCanvas.Children.Clear();
                HourlyPrecipCanvas.Width = 0;
                HourlyHumidityCanvas.Children.Clear();
                HourlyHumidityCanvas.Width = 0;
                return;
            }

            string lang = _settings.Language;
            CultureInfo culture = LocalizationService.GetCulture(lang);
            bool isMinimal = _settings.LayoutStyle == WidgetLayoutStyle.PixelMinimalTransparent;
            SolidColorBrush hourMutedBrush = isMinimal
                ? MaterialThemePalette.ToBrush(Color.FromRgb(0xE2, 0xE8, 0xF0))
                : MaterialThemePalette.ToBrush(_currentPalette.OnSurfaceMuted);
            SolidColorBrush hourAccentBrush = isMinimal
                ? MaterialThemePalette.ToBrush(Color.FromRgb(0x7D, 0xD3, 0xFC))
                : MaterialThemePalette.ToBrush(_currentPalette.Primary);

            Color sparkPrimaryColor = isMinimal ? Color.FromRgb(0xF8, 0xFA, 0xFC) : _currentPalette.Primary;
            Color sparkTextColor = isMinimal ? Color.FromRgb(0xF8, 0xFA, 0xFC) : _currentPalette.OnSurface;
            Color sparkDotStrokeColor = isMinimal ? Color.FromRgb(0x0F, 0x17, 0x2A) : _currentPalette.Surface;
            SolidColorBrush compactTempBrush = MaterialThemePalette.ToBrush(sparkTextColor);
            bool isLightTheme = !isMinimal && _settings.ThemeMode == WidgetThemeMode.MaterialLight;
            SolidColorBrush solarBrush = MaterialThemePalette.ToBrush(
                isLightTheme ? Color.FromRgb(0xD9, 0x77, 0x06) : Color.FromRgb(0xFB, 0xBF, 0x24));

            Visibility compactTempVis = _settings.ShowHourlyTempCurve ? Visibility.Collapsed : Visibility.Visible;
            List<HourlyStripSlot> slots = BuildHourlyStripSlots(_currentWeather);
            var hourlyVms = new List<HourlyItemViewModel>(slots.Count);
            foreach (HourlyStripSlot slot in slots)
            {
                string dateLabel = slot.LocalTime.Date > DateTime.Now.Date
                    ? CapitalizeFirst(slot.LocalTime.ToString("ddd dd/MM", culture), culture) + " · "
                    : string.Empty;
                string hhmm = slot.LocalTime.ToString("HH:mm", CultureInfo.InvariantCulture);

                if (slot.Kind != HourlySlotKind.Forecast)
                {
                    bool isSunset = slot.Kind == HourlySlotKind.Sunset;
                    string shortLabel = LocalizationService.T(lang, isSunset ? "sun_sunset_short" : "sun_sunrise_short");
                    string fullLabel = LocalizationService.T(lang, isSunset ? "sun_sunset_full" : "sun_sunrise_full");
                    hourlyVms.Add(new HourlyItemViewModel
                    {
                        HourLabel = hhmm,
                        HourBrush = solarBrush,
                        WeatherIconVisibility = Visibility.Collapsed,
                        SolarIconVisibility = Visibility.Visible,
                        PrecipVisibility = Visibility.Collapsed,
                        SolarLabel = shortLabel,
                        SolarLabelBrush = solarBrush,
                        SolarArrowData = isSunset
                            ? "M 10,3 L 10,8.5 M 7.5,6 L 10,8.5 L 12.5,6"
                            : "M 10,8.5 L 10,3 M 7.5,5.5 L 10,3 L 12.5,5.5",
                        CompactTempLabel = WeatherService.FormatTemp(slot.TemperatureC, _settings.TempUnit),
                        CompactTempVisibility = compactTempVis,
                        CompactTempBrush = compactTempBrush,
                        TooltipText = $"{dateLabel}{hhmm} · {fullLabel}"
                    });
                    continue;
                }

                HourlyForecastPoint hp = slot.Forecast!;
                bool isMidnight = hp.LocalTime.Hour == 0;
                bool showInlinePrecip = !_settings.ShowHourlyPrecipCurve && hp.PrecipProbability >= 20;
                string condText = WeatherService.DescribeWmoCodeItalian(hp.WeatherCode, lang);
                string hrCardinal = WeatherService.DescribeWindDirectionCardinal(hp.WindDirectionDeg, lang);
                string windRounded = Math.Round(hp.WindSpeedKmh).ToString("0", CultureInfo.InvariantCulture);

                hourlyVms.Add(new HourlyItemViewModel
                {
                    HourLabel = hhmm,
                    PrecipLabel = showInlinePrecip ? $"{hp.PrecipProbability}%" : string.Empty,
                    PrecipVisibility = showInlinePrecip ? Visibility.Visible : Visibility.Hidden,
                    WeatherIconVisibility = Visibility.Visible,
                    SolarIconVisibility = Visibility.Collapsed,
                    CompactTempLabel = WeatherService.FormatTemp(hp.TemperatureC, _settings.TempUnit),
                    CompactTempVisibility = compactTempVis,
                    CompactTempBrush = compactTempBrush,
                    TooltipText = LocalizationService.Format(lang, "hourly_tooltip", dateLabel, hhmm, condText, hp.PrecipProbability, windRounded, hrCardinal, hp.RelativeHumidity),
                    WeatherCode = hp.WeatherCode,
                    IsDay = hp.IsDay,
                    HourBrush = isMidnight ? hourAccentBrush : hourMutedBrush,
                    PrecipBrush = hourAccentBrush
                });
            }

            CardHourlyItems.ItemsSource = hourlyVms;
            RenderHourlySparkline(slots, _settings.TempUnit, sparkPrimaryColor, sparkTextColor, sparkDotStrokeColor, isLightTheme);

            if (resetScroll)
            {
                BeginAnimation(AnimatedHourlyOffsetProperty, null);
                _isHourlyScrollAnimating = false;
                _hourlyScrollTarget = 0.0;
                HourlyScrollViewer.ScrollToHorizontalOffset(0);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateHourlyScrollIndicators));
        }

        internal static List<HourlyStripSlot> BuildHourlyStripSlots(WeatherSnapshot weather)
        {
            var result = new List<HourlyStripSlot>();
            if (weather?.NextHours is null || weather.NextHours.Count == 0)
            {
                return result;
            }

            List<HourlyForecastPoint> hours = weather.NextHours;
            DateTime windowStart = hours[0].LocalTime.AddMinutes(-59);
            DateTime windowEnd = hours[hours.Count - 1].LocalTime.AddMinutes(30);

            var solarEvents = new List<(DateTime Time, HourlySlotKind Kind)>();
            void TryAddSolar(DateTime? dt, HourlySlotKind kind)
            {
                if (!dt.HasValue) return;
                DateTime val = dt.Value;
                if (val < windowStart || val > windowEnd) return;
                foreach ((DateTime existing, HourlySlotKind existingKind) in solarEvents)
                {
                    if (existingKind == kind && Math.Abs((existing - val).TotalMinutes) < 5.0)
                    {
                        return;
                    }
                }
                solarEvents.Add((val, kind));
            }

            if (weather.Daily is not null)
            {
                foreach (DailyForecastPoint dp in weather.Daily)
                {
                    TryAddSolar(dp.SunriseLocal, HourlySlotKind.Sunrise);
                    TryAddSolar(dp.SunsetLocal, HourlySlotKind.Sunset);
                }
            }
            TryAddSolar(weather.SunriseLocal, HourlySlotKind.Sunrise);
            TryAddSolar(weather.SunsetLocal, HourlySlotKind.Sunset);
            solarEvents.Sort((a, b) => a.Time.CompareTo(b.Time));

            int solarIdx = 0;
            for (int i = 0; i < hours.Count; i++)
            {
                HourlyForecastPoint current = hours[i];
                while (solarIdx < solarEvents.Count && solarEvents[solarIdx].Time <= current.LocalTime)
                {
                    (DateTime sTime, HourlySlotKind sKind) = solarEvents[solarIdx++];
                    double interpTemp = InterpolateValueAt(hours, i, sTime, hp => hp.TemperatureC);
                    int interpPrecip = (int)Math.Round(InterpolateValueAt(hours, i, sTime, hp => hp.PrecipProbability));
                    int interpHum = (int)Math.Round(InterpolateValueAt(hours, i, sTime, hp => hp.RelativeHumidity));
                    result.Add(new HourlyStripSlot(sKind, sTime, interpTemp, null, interpPrecip, interpHum));
                }

                result.Add(new HourlyStripSlot(
                    HourlySlotKind.Forecast,
                    current.LocalTime,
                    current.TemperatureC,
                    current,
                    current.PrecipProbability,
                    current.RelativeHumidity));
            }

            HourlyForecastPoint tail = hours[hours.Count - 1];
            while (solarIdx < solarEvents.Count)
            {
                (DateTime sTime, HourlySlotKind sKind) = solarEvents[solarIdx++];
                result.Add(new HourlyStripSlot(
                    sKind,
                    sTime,
                    tail.TemperatureC,
                    null,
                    tail.PrecipProbability,
                    tail.RelativeHumidity));
            }

            return result;
        }

        private static double InterpolateValueAt(
            List<HourlyForecastPoint> hours,
            int nextIndex,
            DateTime targetTime,
            Func<HourlyForecastPoint, double> selector)
        {
            if (nextIndex <= 0)
            {
                return selector(hours[0]);
            }

            HourlyForecastPoint prev = hours[nextIndex - 1];
            HourlyForecastPoint next = hours[nextIndex];
            double totalMins = (next.LocalTime - prev.LocalTime).TotalMinutes;
            double v0 = selector(prev);
            double v1 = selector(next);
            if (totalMins <= 0.1)
            {
                return v1;
            }

            double ratio = Math.Clamp((targetTime - prev.LocalTime).TotalMinutes / totalMins, 0.0, 1.0);
            return v0 + ((v1 - v0) * ratio);
        }

        private void RenderHourlySparkline(
            IReadOnlyList<HourlyStripSlot> slots,
            TemperatureUnit unit,
            Color primaryColor,
            Color textColor,
            Color surfaceColor,
            bool isLightTheme)
        {
            Color solarDotColor = Color.FromRgb(0xFB, 0xBF, 0x24);

            if (_settings.ShowHourlyTempCurve && slots is not null && slots.Count > 0)
            {
                double minT = double.MaxValue;
                double maxT = double.MinValue;
                foreach (HourlyStripSlot slot in slots)
                {
                    if (slot.TemperatureC < minT) minT = slot.TemperatureC;
                    if (slot.TemperatureC > maxT) maxT = slot.TemperatureC;
                }

                RenderBezierMetricCanvas(
                    HourlySparklineCanvas,
                    34.0,
                    slots.Count,
                    i => slots[i].TemperatureC,
                    i => WeatherService.FormatTemp(slots[i].TemperatureC, unit),
                    i => slots[i].Kind != HourlySlotKind.Forecast,
                    minT,
                    maxT,
                    primaryColor,
                    textColor,
                    surfaceColor,
                    solarDotColor);
            }
            else
            {
                HourlySparklineCanvas.Children.Clear();
                HourlySparklineCanvas.Width = 0;
            }

            if (_settings.ShowHourlyPrecipCurve && slots is not null && slots.Count > 0)
            {
                Color precipLine = isLightTheme ? Color.FromRgb(0x02, 0x84, 0xC7) : Color.FromRgb(0x38, 0xBD, 0xF8);
                Color precipLabelColor = isLightTheme ? Color.FromRgb(0x03, 0x69, 0xA1) : Color.FromRgb(0xBA, 0xE6, 0xFD);
                RenderBezierMetricCanvas(
                    HourlyPrecipCanvas,
                    30.0,
                    slots.Count,
                    i => slots[i].PrecipProbability,
                    i => slots[i].Kind == HourlySlotKind.Forecast ? $"{slots[i].PrecipProbability}%" : string.Empty,
                    _ => false,
                    0.0,
                    100.0,
                    precipLine,
                    precipLabelColor,
                    surfaceColor,
                    precipLine);
            }
            else
            {
                HourlyPrecipCanvas.Children.Clear();
                HourlyPrecipCanvas.Width = 0;
            }

            if (_settings.ShowHourlyHumidityCurve && slots is not null && slots.Count > 0)
            {
                double rawMin = double.MaxValue;
                double rawMax = double.MinValue;
                foreach (HourlyStripSlot slot in slots)
                {
                    if (slot.RelativeHumidity < rawMin) rawMin = slot.RelativeHumidity;
                    if (slot.RelativeHumidity > rawMax) rawMax = slot.RelativeHumidity;
                }

                double minH = Math.Max(0.0, rawMin - 5.0);
                double maxH = Math.Min(100.0, Math.Max(rawMax + 5.0, minH + 20.0));
                if (maxH - minH < 20.0)
                {
                    minH = Math.Max(0.0, maxH - 20.0);
                }

                Color humLine = isLightTheme ? Color.FromRgb(0x0F, 0x76, 0x6E) : Color.FromRgb(0x2D, 0xD4, 0xBF);
                Color humLabelColor = isLightTheme ? Color.FromRgb(0x11, 0x5E, 0x59) : Color.FromRgb(0x99, 0xF6, 0xE4);
                RenderBezierMetricCanvas(
                    HourlyHumidityCanvas,
                    30.0,
                    slots.Count,
                    i => slots[i].RelativeHumidity,
                    i => slots[i].Kind == HourlySlotKind.Forecast ? $"{slots[i].RelativeHumidity}%" : string.Empty,
                    _ => false,
                    minH,
                    maxH,
                    humLine,
                    humLabelColor,
                    surfaceColor,
                    humLine);
            }
            else
            {
                HourlyHumidityCanvas.Children.Clear();
                HourlyHumidityCanvas.Width = 0;
            }
        }

        private static void RenderBezierMetricCanvas(
            Canvas targetCanvas,
            double canvasHeight,
            int count,
            Func<int, double> valueSelector,
            Func<int, string> labelSelector,
            Func<int, bool> isHighlightDot,
            double minVal,
            double maxVal,
            Color lineColor,
            Color textColor,
            Color dotStrokeColor,
            Color highlightDotColor)
        {
            targetCanvas.Children.Clear();
            if (count <= 0)
            {
                targetCanvas.Width = 0;
                return;
            }

            const double colWidth = 60.0;
            bool isTempCanvas = canvasHeight >= 33.0;
            double topDotY = isTempCanvas ? 16.5 : 15.0;
            double bottomDotY = canvasHeight - 5.5;
            double labelOffset = isTempCanvas ? 16.0 : 14.5;
            double fontSize = isTempCanvas ? 11.5 : 10.0;

            double totalWidth = count * colWidth;
            targetCanvas.Width = totalWidth;

            double span = maxVal - minVal;
            var points = new Point[count];
            for (int i = 0; i < count; i++)
            {
                double cx = (i * colWidth) + (colWidth / 2.0);
                double norm = span < 0.3 ? 0.5 : Math.Clamp((valueSelector(i) - minVal) / span, 0.0, 1.0);
                double cy = bottomDotY - (norm * (bottomDotY - topDotY));
                points[i] = new Point(cx, cy);
            }

            if (count >= 2)
            {
                var lineFigure = new PathFigure { StartPoint = points[0], IsClosed = false };
                var areaFigure = new PathFigure { StartPoint = new Point(points[0].X, canvasHeight), IsClosed = true };
                areaFigure.Segments.Add(new LineSegment(points[0], isStroked: false));

                for (int i = 0; i < count - 1; i++)
                {
                    Point p0 = points[i];
                    Point p1 = points[i + 1];
                    double dx = (p1.X - p0.X) * 0.40;
                    var c1 = new Point(p0.X + dx, p0.Y);
                    var c2 = new Point(p1.X - dx, p1.Y);

                    lineFigure.Segments.Add(new BezierSegment(c1, c2, p1, isStroked: true));
                    areaFigure.Segments.Add(new BezierSegment(c1, c2, p1, isStroked: false));
                }

                areaFigure.Segments.Add(new LineSegment(new Point(points[count - 1].X, canvasHeight), isStroked: false));

                var areaGeometry = new PathGeometry(new[] { areaFigure });
                areaGeometry.Freeze();

                var areaBrush = new LinearGradientBrush(
                    Color.FromArgb(isTempCanvas ? (byte)64 : (byte)54, lineColor.R, lineColor.G, lineColor.B),
                    Color.FromArgb(0, lineColor.R, lineColor.G, lineColor.B),
                    new Point(0.5, 0.0),
                    new Point(0.5, 1.0));
                areaBrush.Freeze();

                targetCanvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = areaGeometry,
                    Fill = areaBrush
                });

                var lineGeometry = new PathGeometry(new[] { lineFigure });
                lineGeometry.Freeze();

                targetCanvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = lineGeometry,
                    Stroke = MaterialThemePalette.ToBrush(lineColor, 0.92),
                    StrokeThickness = isTempCanvas ? 1.8 : 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            SolidColorBrush dotFill = MaterialThemePalette.ToBrush(lineColor);
            SolidColorBrush highlightFill = MaterialThemePalette.ToBrush(highlightDotColor);
            SolidColorBrush dotStroke = MaterialThemePalette.ToBrush(dotStrokeColor);
            SolidColorBrush labelBrush = MaterialThemePalette.ToBrush(textColor);
            var fontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");

            for (int i = 0; i < count; i++)
            {
                Point pt = points[i];
                bool highlight = isHighlightDot(i);
                string labelText = labelSelector(i);

                if (!string.IsNullOrEmpty(labelText))
                {
                    var label = new TextBlock
                    {
                        Text = labelText,
                        Width = colWidth,
                        TextAlignment = TextAlignment.Center,
                        FontFamily = fontFamily,
                        FontSize = fontSize,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = labelBrush
                    };
                    Canvas.SetLeft(label, i * colWidth);
                    Canvas.SetTop(label, Math.Max(0.0, pt.Y - labelOffset));
                    targetCanvas.Children.Add(label);
                }

                double dotSize = highlight ? 6.2 : (isTempCanvas ? 5.2 : 4.6);
                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = highlight ? highlightFill : dotFill,
                    Stroke = dotStroke,
                    StrokeThickness = 1.2
                };
                Canvas.SetLeft(dot, pt.X - (dotSize / 2.0));
                Canvas.SetTop(dot, pt.Y - (dotSize / 2.0));
                targetCanvas.Children.Add(dot);
            }
        }

        private void CheckWallpaperChanges()
        {
            if (_settings.ThemeMode != WidgetThemeMode.DynamicWallpaper)
            {
                return;
            }

            string? path = _colorService.GetCurrentWallpaperPath();
            DateTime writeUtc = DateTime.MinValue;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    writeUtc = File.GetLastWriteTimeUtc(path);
                }
                catch
                {
                    return;
                }
            }

            if (!string.Equals(path, _lastWallpaperPath, StringComparison.OrdinalIgnoreCase) ||
                writeUtc != _lastWallpaperWriteUtc)
            {
                _lastWallpaperPath = path;
                _lastWallpaperWriteUtc = writeUtc;
                ApplyThemeAndLayout();
                UpdateWeatherDisplay();
            }
        }

        private void CaptureWallpaperSignature()
        {
            _lastWallpaperPath = _colorService.GetCurrentWallpaperPath();
            if (!string.IsNullOrWhiteSpace(_lastWallpaperPath) && File.Exists(_lastWallpaperPath))
            {
                try
                {
                    _lastWallpaperWriteUtc = File.GetLastWriteTimeUtc(_lastWallpaperPath);
                }
                catch
                {
                    _lastWallpaperWriteUtc = DateTime.MinValue;
                }
            }
        }

        private void WidgetWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed || _settings.LockPosition)
            {
                return;
            }

            if (IsClickInsideInteractiveControl(e.OriginalSource as DependencyObject))
            {
                return;
            }

            try
            {
                DragMove();

                // Snap morbido su griglia da 8px e clamp nei limiti dello schermo virtuale
                double snappedLeft = Math.Round(Left / 8.0) * 8.0;
                double snappedTop = Math.Round(Top / 8.0) * 8.0;

                (double clampedLeft, double clampedTop) = SettingsService.ClampPosition(
                    snappedLeft,
                    snappedTop,
                    ActualWidth,
                    ActualHeight,
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);

                Left = clampedLeft;
                Top = clampedTop;
                _settings.Left = Left;
                _settings.Top = Top;
                _settingsService.Save(_settings);
            }
            catch
            {
                // Ignora DragMove interrotto
            }
        }

        private bool IsClickInsideInteractiveControl(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is Button ||
                    ReferenceEquals(current, CardWeatherPill) ||
                    ReferenceEquals(current, CardAlertBadge) ||
                    ReferenceEquals(current, MinimalWeatherPill) ||
                    ReferenceEquals(current, CompactWeatherPill) ||
                    ReferenceEquals(current, CardHourlyContainer) ||
                    ReferenceEquals(current, TabForecastPill) ||
                    ReferenceEquals(current, TabRadarPill) ||
                    ReferenceEquals(current, RadarView) ||
                    (_settings.FavoriteLocations.Count > 1 &&
                     (ReferenceEquals(current, CardSummaryText) ||
                      ReferenceEquals(current, MinimalSubText) ||
                      ReferenceEquals(current, CompactLocationText) ||
                      ReferenceEquals(current, DrawerMetaText))))
                {
                    return true;
                }
                if (current is not Visual && current is not System.Windows.Media.Media3D.Visual3D)
                {
                    current = LogicalTreeHelper.GetParent(current);
                    continue;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void HourlyScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (HourlyScrollViewer.ScrollableWidth <= 0)
            {
                return;
            }

            SetHourlyIndicatorVisible(true);
            double baseOffset = _isHourlyScrollAnimating ? _hourlyScrollTarget : HourlyScrollViewer.HorizontalOffset;
            double step = e.Delta > 0 ? -60.0 : 60.0;
            AnimateHourlyScrollTo(baseOffset + step);
            e.Handled = true;
        }

        private void HourlyScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (HourlyScrollViewer.ScrollableWidth <= 0)
            {
                return;
            }

            _isHourlyPointerDown = true;
            _isHourlyDragging = false;
            _hourlyDragStart = e.GetPosition(HourlyScrollViewer);
            _hourlyStartOffset = HourlyScrollViewer.HorizontalOffset;
        }

        private void HourlyScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isHourlyPointerDown || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point currentPos = e.GetPosition(HourlyScrollViewer);
            double dx = currentPos.X - _hourlyDragStart.X;

            if (!_isHourlyDragging && Math.Abs(dx) > 4.0)
            {
                _isHourlyDragging = true;
                SetHourlyIndicatorVisible(true);
                BeginAnimation(AnimatedHourlyOffsetProperty, null);
                _isHourlyScrollAnimating = false;
                HourlyScrollViewer.CaptureMouse();
            }

            if (_isHourlyDragging)
            {
                double target = Math.Clamp(_hourlyStartOffset - dx, 0.0, HourlyScrollViewer.ScrollableWidth);
                _hourlyScrollTarget = target;
                HourlyScrollViewer.ScrollToHorizontalOffset(target);
                e.Handled = true;
            }
        }

        private void HourlyScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndHourlyDrag();
        }

        private void HourlyScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndHourlyDrag();
        }

        private void EndHourlyDrag()
        {
            _isHourlyPointerDown = false;
            _isHourlyDragging = false;
            if (HourlyScrollViewer.IsMouseCaptured)
            {
                HourlyScrollViewer.ReleaseMouseCapture();
            }
            if (!CardHourlyContainer.IsMouseOver)
            {
                SetHourlyIndicatorVisible(false);
            }
        }

        private void CardHourlyContainer_MouseEnter(object sender, MouseEventArgs e)
        {
            SetHourlyIndicatorVisible(true);
        }

        private void CardHourlyContainer_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isHourlyDragging)
            {
                SetHourlyIndicatorVisible(false);
            }
        }

        private void SetHourlyIndicatorVisible(bool visible)
        {
            double targetOpacity = (visible && HourlyScrollViewer.ScrollableWidth > 1.0) ? 0.90 : 0.0;
            var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(visible ? 150 : 240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            HourlyScrollIndicator.BeginAnimation(OpacityProperty, anim);
        }

        private void AnimateHourlyScrollTo(double targetOffset)
        {
            double maxOffset = HourlyScrollViewer.ScrollableWidth;
            if (maxOffset <= 0.0)
            {
                return;
            }

            double clamped = Math.Clamp(targetOffset, 0.0, maxOffset);
            _hourlyScrollTarget = clamped;
            _isHourlyScrollAnimating = true;

            var anim = new DoubleAnimation
            {
                From = HourlyScrollViewer.HorizontalOffset,
                To = clamped,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Timeline.SetDesiredFrameRate(anim, 30);
            anim.Completed += (_, _) => _isHourlyScrollAnimating = false;
            BeginAnimation(AnimatedHourlyOffsetProperty, anim);
        }

        private void HourlyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateHourlyScrollIndicators();
        }

        private void UpdateHourlyScrollIndicators()
        {
            double scrollable = HourlyScrollViewer.ScrollableWidth;
            if (scrollable <= 1.0)
            {
                HourlyScrollTrack.Visibility = Visibility.Collapsed;
                HourlyScrollThumb.Visibility = Visibility.Collapsed;
                SetHourlyIndicatorVisible(false);
                return;
            }

            double offset = HourlyScrollViewer.HorizontalOffset;
            HourlyScrollTrack.Visibility = Visibility.Visible;
            HourlyScrollThumb.Visibility = Visibility.Visible;

            double trackWidth = HourlyScrollTrack.ActualWidth > 10.0 ? HourlyScrollTrack.ActualWidth : 264.0;
            double extent = Math.Max(1.0, HourlyScrollViewer.ExtentWidth);
            double viewport = Math.Max(1.0, HourlyScrollViewer.ViewportWidth);

            double thumbWidth = Math.Clamp(trackWidth * (viewport / extent), 28.0, trackWidth);
            double ratio = Math.Clamp(offset / scrollable, 0.0, 1.0);
            double leftMargin = (trackWidth - thumbWidth) * ratio;

            HourlyScrollThumb.Width = thumbWidth;
            HourlyScrollThumb.Margin = new Thickness(leftMargin, 0, 0, 0);
        }

        private void MainCard_MouseEnter(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(160));
            HoverActionsBar.BeginAnimation(OpacityProperty, anim);
        }

        private void MainCard_MouseLeave(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220));
            HoverActionsBar.BeginAnimation(OpacityProperty, anim);
        }

        private void CardWeatherPill_MouseEnter(object sender, MouseEventArgs e)
        {
            var zoom = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            WeatherPillScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            WeatherPillScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
        }

        private void CardWeatherPill_MouseLeave(object sender, MouseEventArgs e)
        {
            var reset = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            WeatherPillScale.BeginAnimation(ScaleTransform.ScaleXProperty, reset);
            WeatherPillScale.BeginAnimation(ScaleTransform.ScaleYProperty, reset);
        }

        private void CompactWeatherPill_MouseEnter(object sender, MouseEventArgs e)
        {
            CompactWeatherPill.Background = MaterialThemePalette.ToBrush(_currentPalette.SurfaceVariant, 0.38);
        }

        private void CompactWeatherPill_MouseLeave(object sender, MouseEventArgs e)
        {
            CompactWeatherPill.Background = Brushes.Transparent;
        }

        private void WeatherPill_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleForecastDrawer();
        }

        private void ToggleForecastDrawer()
        {
            if (_isDrawerOpen)
            {
                CloseDrawer();
            }
            else
            {
                OpenDrawer(radarTab: false);
            }
        }

        private void OpenDrawer(bool radarTab)
        {
            _isDrawerOpen = true;
            ForecastDrawer.Opacity = 0.0;
            DrawerSlideTransform.Y = -10.0;
            ForecastDrawer.Visibility = Visibility.Visible;

            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slide = new DoubleAnimation(-10.0, 0.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ForecastDrawer.BeginAnimation(OpacityProperty, fade);
            DrawerSlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);

            SelectDrawerTab(radarTab);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampAndPersistCurrentPosition));
        }

        private void CloseDrawer()
        {
            _isDrawerOpen = false;
            ForecastDrawer.Visibility = Visibility.Collapsed;
            RadarView.Deactivate();
        }

        private void SelectDrawerTab(bool radarTab)
        {
            _isRadarTabActive = radarTab;
            ForecastTabPanel.Visibility = radarTab ? Visibility.Collapsed : Visibility.Visible;
            RadarTabPanel.Visibility = radarTab ? Visibility.Visible : Visibility.Collapsed;
            UpdateDrawerTabVisuals();

            if (radarTab)
            {
                _ = ActivateRadarAsync();
            }
            else
            {
                RadarView.Deactivate();
            }
        }

        private bool IsRadarNightMode()
        {
            if (_currentWeather is { SunriseLocal: DateTime sunrise, SunsetLocal: DateTime sunset })
            {
                DateTime now = DateTime.Now;
                return now < sunrise || now >= sunset;
            }

            if (_currentWeather is not null)
            {
                return !_currentWeather.IsDay;
            }

            int hour = DateTime.Now.Hour;
            return hour < 7 || hour >= 18;
        }

        private Task ActivateRadarAsync()
        {
            return RadarView.ActivateAsync(
                _settings.Latitude,
                _settings.Longitude,
                _settings.RadarZoom,
                IsRadarNightMode(),
                _currentPalette,
                _settings.CityName);
        }

        private void TabForecast_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SelectDrawerTab(radarTab: false);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampAndPersistCurrentPosition));
        }

        private void TabRadar_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SelectDrawerTab(radarTab: true);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampAndPersistCurrentPosition));
        }

        private void BtnCloseDrawer_Click(object sender, RoutedEventArgs e)
        {
            CloseDrawer();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, _openSettingsAction);
        }

        private async Task<OfficialAlertFetchResult> SafeFetchOfficialAlertsAsync(
            double lat,
            double lon,
            string lang,
            CancellationToken ct)
        {
            try
            {
                return await _officialAlertService.FetchOfficialAlertAsync(lat, lon, lang, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new OfficialAlertFetchResult(false, null);
            }
        }

        private void CardAlertBadge_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            bool showForecastDetailsFirst = DrawerAlertBanner.Visibility == Visibility.Visible;
            OpenDrawer(radarTab: !showForecastDetailsFirst);
        }

        private void UpdateAlertBadge()
        {
            if (!_settings.EnableWeatherAlerts)
            {
                CardAlertBadge.Visibility = Visibility.Collapsed;
                DrawerAlertBanner.Visibility = Visibility.Collapsed;
                return;
            }

            string lang = _settings.Language;
            WeatherAlertInfo alert;

            if (_officialAlertResult is { ProviderAvailable: true })
            {
                if (_officialAlertResult.Alert is not null &&
                    _officialAlertResult.Alert.Level != WeatherAlertLevel.None)
                {
                    alert = _officialAlertResult.Alert;
                }
                else
                {
                    // Il servizio meteorologico nazionale non segnala allerte: segnala solo emergenze critiche in corso adesso
                    alert = WeatherService.EvaluateCriticalNowcastOnly(_currentWeather, _settings.TempUnit, DateTime.Now, lang);
                }
            }
            else
            {
                // Fallback su stima Open-Meteo (paesi senza feed supportato o feed momentaneamente offline)
                WeatherAlertInfo fallback = WeatherService.EvaluateWeatherAlert(_currentWeather, _settings.TempUnit, DateTime.Now, lang);
                string fallbackSourceKey = _officialAlertResult?.SourceStatus == AlertSourceStatus.Fallback
                    ? "alert_source_fallback"
                    : "alert_source_estimate";
                alert = fallback.Level == WeatherAlertLevel.None
                    ? fallback
                    : fallback with
                    {
                        SourceName = LocalizationService.T(lang, fallbackSourceKey),
                        Description = fallback.Tooltip
                    };
            }

            if (alert.Level == WeatherAlertLevel.None)
            {
                CardAlertBadge.Visibility = Visibility.Collapsed;
                DrawerAlertBanner.Visibility = Visibility.Collapsed;
                return;
            }

            bool isLight = _settings.ThemeMode == WidgetThemeMode.MaterialLight;
            (Color bgColor, Color borderColor, Color accentFg) = alert.Level switch
            {
                WeatherAlertLevel.Red => (
                    isLight ? Color.FromArgb(0x22, 0xDC, 0x26, 0x26) : Color.FromArgb(0x2E, 0xEF, 0x44, 0x44),
                    isLight ? Color.FromRgb(0xDC, 0x26, 0x26) : Color.FromRgb(0xF8, 0x71, 0x71),
                    isLight ? Color.FromRgb(0xB9, 0x1C, 0x1C) : Color.FromRgb(0xFC, 0xA5, 0xA5)),
                WeatherAlertLevel.Orange => (
                    isLight ? Color.FromArgb(0x24, 0xEA, 0x58, 0x0C) : Color.FromArgb(0x2C, 0xF9, 0x73, 0x16),
                    isLight ? Color.FromRgb(0xEA, 0x58, 0x0C) : Color.FromRgb(0xFB, 0x92, 0x3C),
                    isLight ? Color.FromRgb(0xC2, 0x41, 0x0C) : Color.FromRgb(0xFD, 0xBA, 0x74)),
                _ => (
                    isLight ? Color.FromArgb(0x24, 0xD9, 0x77, 0x06) : Color.FromArgb(0x29, 0xF5, 0x9E, 0x0B),
                    isLight ? Color.FromRgb(0xD9, 0x77, 0x06) : Color.FromRgb(0xFB, 0xBF, 0x24),
                    isLight ? Color.FromRgb(0xB4, 0x53, 0x09) : Color.FromRgb(0xFD, 0xE6, 0x8A))
            };

            SolidColorBrush bgBrush = MaterialThemePalette.ToBrush(bgColor);
            SolidColorBrush borderBrush = MaterialThemePalette.ToBrush(borderColor, 0.90);
            SolidColorBrush accentBrush = MaterialThemePalette.ToBrush(accentFg);
            SolidColorBrush onSurfaceBrush = MaterialThemePalette.ToBrush(_currentPalette.OnSurface);
            SolidColorBrush mutedBrush = MaterialThemePalette.ToBrush(_currentPalette.OnSurfaceMuted);

            string certaintySuffix = string.Empty;
            if (string.Equals(alert.Certainty, "Observed", StringComparison.OrdinalIgnoreCase))
            {
                certaintySuffix = $" · {LocalizationService.T(lang, "alert_certainty_observed")}";
            }
            else if (string.Equals(alert.Certainty, "Possible", StringComparison.OrdinalIgnoreCase))
            {
                certaintySuffix = $" · {LocalizationService.T(lang, "alert_certainty_possible")}";
            }

            string tooltipText = string.IsNullOrWhiteSpace(certaintySuffix)
                ? alert.Tooltip
                : $"{alert.Tooltip}{certaintySuffix}";

            CardAlertBadge.Background = bgBrush;
            CardAlertBadge.BorderBrush = borderBrush;
            CardAlertIcon.Foreground = accentBrush;
            CardAlertTitleText.Foreground = accentBrush;
            CardAlertDetailText.Foreground = onSurfaceBrush;

            CardAlertTitleText.Text = alert.Title;
            CardAlertDetailText.Text = alert.Detail;
            CardAlertBadge.ToolTip = tooltipText;
            CardAlertBadge.Visibility = Visibility.Visible;

            // Banner descrittivo nel Drawer (visibile anche su Material Compatto e Minimal)
            string levelKey = alert.Level switch
            {
                WeatherAlertLevel.Red => "alert_lvl_red",
                WeatherAlertLevel.Orange => "alert_lvl_orange",
                _ => "alert_lvl_yellow"
            };
            DrawerAlertBanner.Background = bgBrush;
            DrawerAlertBanner.BorderBrush = borderBrush;
            DrawerAlertIcon.Foreground = accentBrush;
            DrawerAlertHeaderText.Foreground = accentBrush;
            DrawerAlertTimeText.Foreground = accentBrush;
            DrawerAlertBodyText.Foreground = onSurfaceBrush;
            DrawerAlertSourceText.Foreground = mutedBrush;

            DrawerAlertHeaderText.Text = $"{LocalizationService.T(lang, levelKey)} · {alert.Title}";
            DrawerAlertTimeText.Text = alert.Detail;
            DrawerAlertBodyText.Text = string.IsNullOrWhiteSpace(alert.Description) ? alert.Tooltip : alert.Description;
            string sourceLine = alert.IsOfficial && !string.IsNullOrWhiteSpace(alert.SourceName)
                ? LocalizationService.Format(
                    lang,
                    _officialAlertResult?.SourceStatus == AlertSourceStatus.StaleCache
                        ? "alert_source_stale"
                        : "alert_source_official",
                    alert.SourceName)
                : (string.IsNullOrWhiteSpace(alert.SourceName)
                    ? LocalizationService.T(lang, "alert_source_estimate")
                    : alert.SourceName);
            DrawerAlertSourceText.Text = $"{sourceLine}{certaintySuffix}";
            DrawerAlertBanner.Visibility = Visibility.Visible;
        }

        private void UpdateQuickCitySwitchHints()
        {
            SavedLocation? next = _settings.PeekNextFavorite();
            Cursor? cursor = next is not null ? Cursors.Hand : null;
            string? tip = next is not null ? LocalizationService.Format(_settings.Language, "switch_city_hint", next.Name) : null;

            foreach (TextBlock target in new[] { CardSummaryText, MinimalSubText, CompactLocationText, DrawerMetaText })
            {
                target.Cursor = cursor;
                target.ToolTip = tip;
            }
        }

        private void LocationSummary_Click(object sender, MouseButtonEventArgs e)
        {
            SavedLocation? next = _settings.CycleNextFavorite();
            if (next is null)
            {
                return;
            }

            e.Handled = true;
            SelectFavoriteLocation(next);
        }

        private void SelectFavoriteLocation(SavedLocation target)
        {
            _officialAlertResult = null;
            _settings.AutoDetectLocation = false;
            _settings.AddOrSelectLocation(target.Name, target.Country, target.Latitude, target.Longitude);
            _settingsService.Save(_settings);
            UpdateQuickCitySwitchHints();
            if (_isDrawerOpen && _isRadarTabActive)
            {
                _ = ActivateRadarAsync();
            }
            _ = RefreshWeatherAsync();
        }

        private void WidgetContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            string lang = _settings.Language;
            MenuRefreshWeatherItem.Header = LocalizationService.T(lang, "menu_refresh");
            MenuToggleDrawerItem.Header = LocalizationService.T(lang, _isDrawerOpen ? "menu_hide_drawer" : "menu_show_drawer");
            MenuOpenRadarItem.Header = LocalizationService.T(lang, "menu_open_radar");
            MenuFavoriteLocations.Header = LocalizationService.T(lang, "menu_change_city");
            MenuStyleRootItem.Header = LocalizationService.T(lang, "menu_style");
            MenuStylePixelCard.Header = LocalizationService.T(lang, "menu_style_card");
            MenuStyleMinimal.Header = LocalizationService.T(lang, "menu_style_minimal");
            MenuStyleCompact.Header = LocalizationService.T(lang, "menu_style_compact");
            MenuThemeRootItem.Header = LocalizationService.T(lang, "menu_theme");
            MenuThemeDynamic.Header = LocalizationService.T(lang, "menu_theme_dynamic");
            MenuThemePixelDark.Header = LocalizationService.T(lang, "menu_theme_dark");
            MenuThemeGlassDark.Header = LocalizationService.T(lang, "menu_theme_glass");
            MenuThemeMaterialLight.Header = LocalizationService.T(lang, "menu_theme_light");
            MenuPinRootItem.Header = LocalizationService.T(lang, "menu_pin");
            MenuPinDesktop.Header = LocalizationService.T(lang, "menu_pin_desktop");
            MenuPinBottom.Header = LocalizationService.T(lang, "menu_pin_bottom");
            MenuPinTop.Header = LocalizationService.T(lang, "menu_pin_top");
            MenuLockPositionItem.Header = LocalizationService.T(lang, "menu_lock_pos");
            MenuAutostartItem.Header = LocalizationService.T(lang, "menu_autostart");
            MenuCenterWidgetItem.Header = LocalizationService.T(lang, "menu_center");
            MenuOpenSettingsItem.Header = LocalizationService.T(lang, "menu_settings");
            MenuExitItem.Header = LocalizationService.T(lang, "menu_exit");

            MenuFavoriteLocations.Items.Clear();
            if (_settings.FavoriteLocations.Count > 1)
            {
                MenuFavoriteLocations.Visibility = Visibility.Visible;
                foreach (SavedLocation loc in _settings.FavoriteLocations)
                {
                    var item = new MenuItem
                    {
                        Header = loc.DisplayLabel,
                        IsCheckable = true,
                        IsChecked = loc.Matches(_settings.CityName, _settings.Latitude, _settings.Longitude)
                    };
                    item.Click += (_, _) => SelectFavoriteLocation(loc);
                    MenuFavoriteLocations.Items.Add(item);
                }
            }
            else
            {
                MenuFavoriteLocations.Visibility = Visibility.Collapsed;
            }

            MenuStylePixelCard.IsChecked = _settings.LayoutStyle == WidgetLayoutStyle.PixelCard;
            MenuStyleMinimal.IsChecked = _settings.LayoutStyle == WidgetLayoutStyle.PixelMinimalTransparent;
            MenuStyleCompact.IsChecked = _settings.LayoutStyle == WidgetLayoutStyle.MaterialCompact;

            MenuThemeDynamic.IsChecked = _settings.ThemeMode == WidgetThemeMode.DynamicWallpaper;
            MenuThemePixelDark.IsChecked = _settings.ThemeMode == WidgetThemeMode.PixelDark;
            MenuThemeGlassDark.IsChecked = _settings.ThemeMode == WidgetThemeMode.GlassDark;
            MenuThemeMaterialLight.IsChecked = _settings.ThemeMode == WidgetThemeMode.MaterialLight;

            MenuPinDesktop.IsChecked = _settings.PinMode == WindowPinMode.DesktopPinned;
            MenuPinBottom.IsChecked = _settings.PinMode == WindowPinMode.BottomMost;
            MenuPinTop.IsChecked = _settings.PinMode == WindowPinMode.AlwaysOnTop;

            MenuLockPositionItem.IsChecked = _settings.LockPosition;
            MenuAutostartItem.IsChecked = _autostartService.IsEnabled();
        }

        private void MenuRefreshWeather_Click(object sender, RoutedEventArgs e) => _ = RefreshWeatherAsync();
        private void MenuToggleDrawer_Click(object sender, RoutedEventArgs e) => ToggleForecastDrawer();
        private void MenuOpenRadar_Click(object sender, RoutedEventArgs e) => OpenDrawer(radarTab: true);

        private void MenuStylePixelCard_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.LayoutStyle = WidgetLayoutStyle.PixelCard);
        private void MenuStyleMinimal_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.LayoutStyle = WidgetLayoutStyle.PixelMinimalTransparent);
        private void MenuStyleCompact_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.LayoutStyle = WidgetLayoutStyle.MaterialCompact);

        private void MenuThemeDynamic_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.ThemeMode = WidgetThemeMode.DynamicWallpaper);
        private void MenuThemePixelDark_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.ThemeMode = WidgetThemeMode.PixelDark);
        private void MenuThemeGlassDark_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.ThemeMode = WidgetThemeMode.GlassDark);
        private void MenuThemeMaterialLight_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.ThemeMode = WidgetThemeMode.MaterialLight);

        private void MenuPinDesktop_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.PinMode = WindowPinMode.DesktopPinned);
        private void MenuPinBottom_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.PinMode = WindowPinMode.BottomMost);
        private void MenuPinTop_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.PinMode = WindowPinMode.AlwaysOnTop);

        private void MenuLockPosition_Click(object sender, RoutedEventArgs e) => UpdateSetting(s => s.LockPosition = !s.LockPosition);
        private void MenuAutostart_Click(object sender, RoutedEventArgs e) => _autostartService.SetEnabled(!_autostartService.IsEnabled());
        private void MenuCenterWidget_Click(object sender, RoutedEventArgs e) => CenterOnPrimaryScreen();
        private void MenuOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, _openSettingsAction);
        }
        private void MenuExit_Click(object sender, RoutedEventArgs e) => _exitAction();

        private void UpdateSetting(Action<WidgetSettings> mutator)
        {
            WidgetSettings next = _settings.Clone();
            mutator(next);
            ApplySettings(next, refreshWeather: false);
        }

        private static string CapitalizeFirst(string input, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            if (input.Length == 1)
            {
                return input.ToUpper(culture);
            }

            return char.ToUpper(input[0], culture) + input.Substring(1);
        }

        internal enum HourlySlotKind
        {
            Forecast,
            Sunrise,
            Sunset
        }

        internal sealed record HourlyStripSlot(
            HourlySlotKind Kind,
            DateTime LocalTime,
            double TemperatureC,
            HourlyForecastPoint? Forecast,
            int PrecipProbability = 0,
            int RelativeHumidity = 0);

        private sealed class HourlyItemViewModel
        {
            public string HourLabel { get; init; } = string.Empty;
            public string PrecipLabel { get; init; } = string.Empty;
            public Visibility PrecipVisibility { get; init; } = Visibility.Hidden;
            public Visibility WeatherIconVisibility { get; init; } = Visibility.Visible;
            public Visibility SolarIconVisibility { get; init; } = Visibility.Collapsed;
            public string SolarLabel { get; init; } = string.Empty;
            public Brush SolarLabelBrush { get; init; } = Brushes.Gold;
            public string SolarArrowData { get; init; } = string.Empty;
            public string CompactTempLabel { get; init; } = string.Empty;
            public Visibility CompactTempVisibility { get; init; } = Visibility.Collapsed;
            public Brush CompactTempBrush { get; init; } = Brushes.White;
            public string TooltipText { get; init; } = string.Empty;
            public int WeatherCode { get; init; }
            public bool IsDay { get; init; }
            public Brush HourBrush { get; init; } = Brushes.LightGray;
            public Brush PrecipBrush { get; init; } = Brushes.LightSkyBlue;
        }

        private sealed class DailyItemViewModel
        {
            public string DayLabel { get; init; } = string.Empty;
            public int WeatherCode { get; init; }
            public string ConditionLabel { get; init; } = string.Empty;
            public string PrecipLabel { get; init; } = string.Empty;
            public Visibility PrecipVisibility { get; init; } = Visibility.Hidden;
            public string WindLabel { get; init; } = string.Empty;
            public double WindArrowAngle { get; init; }
            public string WindTooltip { get; init; } = string.Empty;
            public string MinLabel { get; init; } = string.Empty;
            public string MaxLabel { get; init; } = string.Empty;
            public Brush PrimaryTextBrush { get; init; } = Brushes.White;
            public Brush MutedBrush { get; init; } = Brushes.LightGray;
            public Brush AccentBrush { get; init; } = Brushes.LightSkyBlue;
        }
    }
}
