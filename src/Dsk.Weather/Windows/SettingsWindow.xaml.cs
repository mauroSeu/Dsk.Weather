using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dsk.Weather.Models;
using Dsk.Weather.Services;

namespace Dsk.Weather.Windows
{
    public partial class SettingsWindow : Window
    {
        private readonly WidgetSettings _draft;
        private readonly WeatherService _weatherService;
        private readonly AutostartService _autostartService;
        private readonly Action<WidgetSettings> _onSettingsChanged;
        private readonly Action _onResetPosition;
        private readonly Action<WeatherIconKind?> _onPreviewChanged;
        private readonly Func<Task> _onRefreshWeatherAsync;

        private bool _isInitializing = true;
        private MaterialThemePalette _currentPalette;

        public SettingsWindow(
            WidgetSettings initialSettings,
            WeatherService weatherService,
            AutostartService autostartService,
            Action<WidgetSettings> onSettingsChanged,
            Action onResetPosition,
            Action<WeatherIconKind?>? onPreviewChanged = null,
            Func<Task>? onRefreshWeatherAsync = null,
            MaterialThemePalette? initialPalette = null)
        {
            _draft = initialSettings.Clone();
            _draft.Normalize();
            _weatherService = weatherService;
            _autostartService = autostartService;
            _onSettingsChanged = onSettingsChanged;
            _onResetPosition = onResetPosition;
            _onPreviewChanged = onPreviewChanged ?? (_ => { });
            _onRefreshWeatherAsync = onRefreshWeatherAsync ?? (() => Task.CompletedTask);
            _currentPalette = initialPalette ?? new MaterialColorService().BuildPalette(_draft.ThemeMode);

            InitializeComponent();

            ApplyPalette(_currentPalette, _draft.ThemeMode == WidgetThemeMode.MaterialLight);
            Closed += (_, _) => _onPreviewChanged(null);
            PopulateUiFromDraft();
        }

        public void ApplyPalette(MaterialThemePalette palette, bool isLightTheme)
        {
            if (palette is null)
            {
                return;
            }

            _currentPalette = palette;

            double primaryLum = ((0.299 * palette.Primary.R) + (0.587 * palette.Primary.G) + (0.114 * palette.Primary.B)) / 255.0;
            Color onAccentColor = primaryLum > 0.56 ? Color.FromRgb(0x11, 0x16, 0x20) : Color.FromRgb(0xFF, 0xFF, 0xFF);

            Resources["DynShellBg"] = MaterialThemePalette.ToBrush(palette.Surface, isLightTheme ? 0.96 : 0.95);
            Resources["DynPopupBg"] = MaterialThemePalette.ToBrush(palette.SurfaceVariant, 0.98);
            Resources["DynCardBg"] = MaterialThemePalette.ToBrush(palette.SurfaceVariant, isLightTheme ? 0.72 : 0.55);
            Resources["DynInputBg"] = MaterialThemePalette.ToBrush(palette.Surface, isLightTheme ? 0.92 : 0.78);
            Resources["DynBorder"] = MaterialThemePalette.ToBrush(palette.SurfaceBorder, 0.68);
            Resources["DynAccent"] = MaterialThemePalette.ToBrush(palette.Primary);
            Resources["DynAccentContainer"] = MaterialThemePalette.ToBrush(palette.PrimaryContainer, 0.88);
            Resources["DynOnAccent"] = MaterialThemePalette.ToBrush(onAccentColor);
            Resources["DynText"] = MaterialThemePalette.ToBrush(palette.OnSurface);
            Resources["DynMuted"] = MaterialThemePalette.ToBrush(palette.OnSurfaceMuted);
            Resources["DynHoverBg"] = MaterialThemePalette.ToBrush(palette.SurfaceVariant, isLightTheme ? 0.88 : 0.62);
            Resources["DynButtonBg"] = MaterialThemePalette.ToBrush(palette.SurfaceVariant, isLightTheme ? 0.82 : 0.76);
            Resources["DynSwitchTrackOff"] = MaterialThemePalette.ToBrush(palette.SurfaceBorder, isLightTheme ? 0.55 : 0.70);
            Resources["DynSwitchThumbOff"] = MaterialThemePalette.ToBrush(palette.OnSurfaceMuted, 0.92);

            RefreshFavoritesUi();
        }

        private void PopulateUiFromDraft()
        {
            _isInitializing = true;
            try
            {
                UpdateActiveLocationLabel();
                RefreshFavoritesUi();

                SelectComboByTag(LayoutCombo, _draft.LayoutStyle.ToString());
                SelectComboByTag(ThemeCombo, _draft.ThemeMode.ToString());
                SelectComboByTag(UnitCombo, _draft.TempUnit.ToString());
                SelectComboByTag(WeatherModelCombo, _draft.WeatherModel.ToString());
                SelectComboByTag(PinModeCombo, _draft.PinMode.ToString());
                SelectComboByTag(PreviewSceneCombo, "Real");

                ScaleSlider.Value = _draft.Scale;
                ScaleValueText.Text = $"{Math.Round(_draft.Scale * 100)}%";

                OpacitySlider.Value = _draft.BackgroundOpacity;
                OpacityValueText.Text = $"{Math.Round(_draft.BackgroundOpacity * 100)}%";

                DynamicAtmosphereCheck.IsChecked = _draft.EnableDynamicAtmosphere;
                WeatherAnimationsCheck.IsChecked = _draft.EnableWeatherAnimations;
                Use24HourCheck.IsChecked = _draft.Use24HourFormat;
                ShowSecondsCheck.IsChecked = _draft.ShowSeconds;
                ShowHourlyCheck.IsChecked = _draft.ShowHourlyStrip;
                QuickNoteBox.Text = _draft.QuickNote;
                LockPositionCheck.IsChecked = _draft.LockPosition;
                AutostartCheck.IsChecked = _autostartService.IsEnabled();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void UpdateActiveLocationLabel()
        {
            string city = string.IsNullOrWhiteSpace(_draft.CityName) ? "Roma" : _draft.CityName;
            string country = string.IsNullOrWhiteSpace(_draft.CountryName) ? string.Empty : $", {_draft.CountryName}";
            string coords = string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", _draft.Latitude, _draft.Longitude);
            ActiveLocationText.Text = $"{city}{country} {coords}";
        }

        private void RefreshFavoritesUi()
        {
            if (FavoritesList is null || _draft is null)
            {
                return;
            }

            var items = new List<FavoriteItemViewModel>(_draft.FavoriteLocations.Count);
            SolidColorBrush activeBg = MaterialThemePalette.ToBrush(_currentPalette.PrimaryContainer, 0.90);
            SolidColorBrush activeBorder = MaterialThemePalette.ToBrush(_currentPalette.Primary, 0.95);
            SolidColorBrush inactiveBg = MaterialThemePalette.ToBrush(_currentPalette.Surface, 0.75);
            SolidColorBrush inactiveBorder = MaterialThemePalette.ToBrush(_currentPalette.SurfaceBorder, 0.70);
            SolidColorBrush activeText = MaterialThemePalette.ToBrush(_currentPalette.OnSurface);
            SolidColorBrush inactiveText = MaterialThemePalette.ToBrush(_currentPalette.OnSurfaceMuted);

            foreach (SavedLocation loc in _draft.FavoriteLocations)
            {
                bool isActive = loc.Matches(_draft.CityName, _draft.Latitude, _draft.Longitude);
                items.Add(new FavoriteItemViewModel
                {
                    Location = loc,
                    Title = loc.Name,
                    TooltipText = $"{loc.DisplayLabel} ({loc.Latitude:0.##}, {loc.Longitude:0.##})",
                    PillBg = isActive ? activeBg : inactiveBg,
                    PillBorder = isActive ? activeBorder : inactiveBorder,
                    TextBrush = isActive ? activeText : inactiveText,
                    TitleWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    ActiveDotVisibility = isActive ? Visibility.Visible : Visibility.Collapsed
                });
            }

            FavoritesList.ItemsSource = items;
        }

        private void FavoritePill_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is SavedLocation loc)
            {
                _draft.AddOrSelectLocation(loc.Name, loc.Country, loc.Latitude, loc.Longitude);
                UpdateActiveLocationLabel();
                RefreshFavoritesUi();
                _onSettingsChanged(_draft);
            }
        }

        private void BtnRemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Button btn && btn.Tag is SavedLocation loc)
            {
                _draft.FavoriteLocations.Remove(loc);
                RefreshFavoritesUi();
                _onSettingsChanged(_draft);
            }
        }

        private void BtnAddLocation_Click(object sender, RoutedEventArgs e)
        {
            bool willOpen = AddLocationPanel.Visibility != Visibility.Visible;
            AddLocationPanel.Visibility = willOpen ? Visibility.Visible : Visibility.Collapsed;
            if (willOpen)
            {
                CitySearchBox.Text = string.Empty;
                CityResultsList.Visibility = Visibility.Collapsed;
                SearchStatusText.Visibility = Visibility.Collapsed;
                CitySearchBox.Focus();
            }
        }

        private static void SelectComboByTag(ComboBox combo, string tagValue)
        {
            foreach (object item in combo.Items)
            {
                if (item is ComboBoxItem cbi && string.Equals(cbi.Tag as string, tagValue, StringComparison.Ordinal))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static T ParseSelectedTag<T>(ComboBox combo, T fallback) where T : struct, Enum
        {
            if (combo.SelectedItem is ComboBoxItem cbi &&
                cbi.Tag is string tag &&
                Enum.TryParse<T>(tag, out T parsed))
            {
                return parsed;
            }
            return fallback;
        }

        private void SettingCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _draft is null)
            {
                return;
            }

            _draft.LayoutStyle = ParseSelectedTag(LayoutCombo, _draft.LayoutStyle);
            _draft.ThemeMode = ParseSelectedTag(ThemeCombo, _draft.ThemeMode);
            _draft.TempUnit = ParseSelectedTag(UnitCombo, _draft.TempUnit);
            _draft.WeatherModel = ParseSelectedTag(WeatherModelCombo, _draft.WeatherModel);
            _draft.PinMode = ParseSelectedTag(PinModeCombo, _draft.PinMode);

            _onSettingsChanged(_draft);
        }

        private void SettingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _draft is null)
            {
                return;
            }

            if (ScaleSlider is null || ScaleValueText is null || OpacitySlider is null || OpacityValueText is null)
            {
                return;
            }

            _draft.Scale = Math.Round(ScaleSlider.Value, 2);
            ScaleValueText.Text = $"{Math.Round(_draft.Scale * 100)}%";

            _draft.BackgroundOpacity = Math.Round(OpacitySlider.Value, 2);
            OpacityValueText.Text = $"{Math.Round(_draft.BackgroundOpacity * 100)}%";

            _onSettingsChanged(_draft);
        }

        private void SettingCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _draft is null)
            {
                return;
            }

            _draft.EnableDynamicAtmosphere = DynamicAtmosphereCheck.IsChecked == true;
            _draft.EnableWeatherAnimations = WeatherAnimationsCheck.IsChecked == true;
            _draft.Use24HourFormat = Use24HourCheck.IsChecked == true;
            _draft.ShowSeconds = ShowSecondsCheck.IsChecked == true;
            _draft.ShowHourlyStrip = ShowHourlyCheck.IsChecked == true;
            _draft.LockPosition = LockPositionCheck.IsChecked == true;

            _onSettingsChanged(_draft);
        }

        private void QuickNoteBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing || _draft is null)
            {
                return;
            }

            _draft.QuickNote = QuickNoteBox.Text.Trim();
            _onSettingsChanged(_draft);
        }

        private void BtnClearNote_Click(object sender, RoutedEventArgs e)
        {
            QuickNoteBox.Text = string.Empty;
        }

        private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _draft is null)
            {
                return;
            }

            bool target = AutostartCheck.IsChecked == true;
            bool ok = _autostartService.SetEnabled(target);
            if (!ok)
            {
                _isInitializing = true;
                AutostartCheck.IsChecked = _autostartService.IsEnabled();
                _isInitializing = false;
            }
        }

        private async void BtnSearchCity_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteCitySearchAsync();
        }

        private async void CitySearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ExecuteCitySearchAsync();
            }
        }

        private async System.Threading.Tasks.Task ExecuteCitySearchAsync()
        {
            string query = CitySearchBox.Text.Trim();
            if (query.Length < 2)
            {
                SearchStatusText.Text = "Inserisci almeno 2 caratteri per cercare una città.";
                SearchStatusText.Visibility = Visibility.Visible;
                CityResultsList.Visibility = Visibility.Collapsed;
                return;
            }

            BtnSearchCity.IsEnabled = false;
            SearchStatusText.Text = "Ricerca località in corso...";
            SearchStatusText.Visibility = Visibility.Visible;
            CityResultsList.Visibility = Visibility.Collapsed;

            try
            {
                List<GeoLocationResult> results = await _weatherService.SearchCitiesAsync(query);
                if (results.Count == 0)
                {
                    SearchStatusText.Text = "Nessuna città trovata. Prova con un altro nome.";
                    CityResultsList.ItemsSource = null;
                    CityResultsList.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SearchStatusText.Text = "Seleziona una località dalla lista:";
                    CityResultsList.ItemsSource = results;
                    CityResultsList.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                SearchStatusText.Text = "Errore di connessione durante la ricerca.";
            }
            finally
            {
                BtnSearchCity.IsEnabled = true;
            }
        }

        private void CityResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CityResultsList.SelectedItem is not GeoLocationResult selected)
            {
                return;
            }

            _draft.AddOrSelectLocation(selected.Name, selected.Country, selected.Latitude, selected.Longitude);

            UpdateActiveLocationLabel();
            RefreshFavoritesUi();
            SearchStatusText.Text = $"Aggiunta e impostata: {selected.DisplayName}.";
            CityResultsList.Visibility = Visibility.Collapsed;
            AddLocationPanel.Visibility = Visibility.Collapsed;

            _onSettingsChanged(_draft);
        }

        private async void BtnDetectIp_Click(object sender, RoutedEventArgs e)
        {
            BtnDetectIp.IsEnabled = false;
            AddLocationPanel.Visibility = Visibility.Visible;
            SearchStatusText.Text = "Rilevamento posizione da IP in corso...";
            SearchStatusText.Visibility = Visibility.Visible;

            try
            {
                GeoLocationResult detected = await _weatherService.DetectLocationByIpAsync();
                _draft.AddOrSelectLocation(detected.Name, detected.Country, detected.Latitude, detected.Longitude);

                UpdateActiveLocationLabel();
                RefreshFavoritesUi();
                SearchStatusText.Text = $"Posizione rilevata: {detected.DisplayName}.";
                CityResultsList.Visibility = Visibility.Collapsed;

                _onSettingsChanged(_draft);
            }
            finally
            {
                BtnDetectIp.IsEnabled = true;
            }
        }

        private void BtnResetPosition_Click(object sender, RoutedEventArgs e)
        {
            _onResetPosition();
        }

        private async void BtnRefreshWeather_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshWeatherTop.IsEnabled = false;
            BtnRefreshWeatherBottom.IsEnabled = false;
            BtnRefreshWeatherTop.Content = "↻ Aggiornamento...";
            BtnRefreshWeatherBottom.Content = "↻ Aggiornamento...";
            try
            {
                await _onRefreshWeatherAsync();
                SearchStatusText.Visibility = Visibility.Visible;
                SearchStatusText.Text = $"Meteo aggiornato alle {DateTime.Now:HH:mm:ss}.";
            }
            finally
            {
                BtnRefreshWeatherTop.Content = "↻ Ricarica";
                BtnRefreshWeatherBottom.Content = "↻ Ricarica meteo";
                BtnRefreshWeatherTop.IsEnabled = true;
                BtnRefreshWeatherBottom.IsEnabled = true;
            }
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current is not null)
            {
                if (current is Button)
                {
                    return;
                }
                if (current is not Visual && current is not System.Windows.Media.Media3D.Visual3D)
                {
                    current = LogicalTreeHelper.GetParent(current);
                    continue;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            try
            {
                DragMove();
            }
            catch
            {
                // Ignora DragMove interrotto
            }
        }

        private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            if (Keyboard.FocusedElement is DependencyObject focused)
            {
                DependencyObject? current = focused;
                while (current is not null)
                {
                    if (current is ComboBox cb && cb.IsDropDownOpen)
                    {
                        return;
                    }
                    if (current is not Visual && current is not System.Windows.Media.Media3D.Visual3D)
                    {
                        current = LogicalTreeHelper.GetParent(current);
                        continue;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
            }

            e.Handled = true;
            Close();
        }

        private void PreviewSceneCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _draft is null)
            {
                return;
            }

            if (PreviewSceneCombo.SelectedItem is ComboBoxItem cbi &&
                cbi.Tag is string tag &&
                Enum.TryParse<WeatherIconKind>(tag, out WeatherIconKind kind))
            {
                _onPreviewChanged(kind);
            }
            else
            {
                _onPreviewChanged(null);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private sealed class FavoriteItemViewModel
        {
            public SavedLocation Location { get; init; } = new SavedLocation();
            public string Title { get; init; } = string.Empty;
            public string TooltipText { get; init; } = string.Empty;
            public Brush PillBg { get; init; } = Brushes.Transparent;
            public Brush PillBorder { get; init; } = Brushes.Transparent;
            public Brush TextBrush { get; init; } = Brushes.White;
            public FontWeight TitleWeight { get; init; } = FontWeights.Normal;
            public Visibility ActiveDotVisibility { get; init; } = Visibility.Collapsed;
        }
    }
}
