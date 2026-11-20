using System;
using System.Threading;
using System.Windows;
using PixelDesktopWidget.Icons;
using PixelDesktopWidget.Models;
using PixelDesktopWidget.Services;
using PixelDesktopWidget.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PixelDesktopWidget
{
    public partial class App : Application
    {
        private Mutex? _singleInstanceMutex;
        private SettingsService _settingsService = null!;
        private WeatherService _weatherService = null!;
        private MaterialColorService _colorService = null!;
        private DesktopPinService _pinService = null!;
        private AutostartService _autostartService = null!;
        private WidgetSettings _settings = null!;

        private WidgetWindow? _widgetWindow;
        private SettingsWindow? _settingsWindow;
        private Forms.NotifyIcon? _notifyIcon;
        private Drawing.Icon? _trayIconHandle;
        private Forms.ToolStripMenuItem? _autostartTrayItem;
        private System.Windows.Threading.DispatcherTimer? _saveDebounceTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                _settingsService?.LogError("DispatcherUnhandledException", args.Exception);
                args.Handled = true;
            };

            _singleInstanceMutex = new Mutex(true, @"Local\PixelDesktopWidget_SingleInstance_v1", out bool createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }

            _settingsService = new SettingsService();
            _weatherService = new WeatherService(_settingsService);
            _colorService = new MaterialColorService();
            _pinService = new DesktopPinService();
            _autostartService = new AutostartService();
            _settings = _settingsService.Load();

            _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _saveDebounceTimer.Tick += (_, _) =>
            {
                _saveDebounceTimer.Stop();
                _settingsService.Save(_settings);
            };

            SetupTrayIcon();

            _widgetWindow = new WidgetWindow(
                _settings,
                _settingsService,
                _weatherService,
                _colorService,
                _pinService,
                _autostartService,
                OpenSettingsWindow,
                RequestExit);

            MainWindow = _widgetWindow;
            _widgetWindow.Show();
        }

        private void SetupTrayIcon()
        {
            _trayIconHandle = TrayIconFactory.CreateTrayIcon();

            var menu = new Forms.ContextMenuStrip
            {
                Renderer = new DarkTrayMenuRenderer(),
                ShowImageMargin = false
            };

            var centerItem = new Forms.ToolStripMenuItem("Centra widget sullo schermo");
            centerItem.Click += (_, _) => Dispatcher.Invoke(() => _widgetWindow?.CenterOnPrimaryScreen());

            var refreshItem = new Forms.ToolStripMenuItem("Aggiorna meteo");
            refreshItem.Click += (_, _) => Dispatcher.Invoke(() => _ = _widgetWindow?.RefreshWeatherAsync());

            var settingsItem = new Forms.ToolStripMenuItem("Impostazioni...");
            settingsItem.Click += (_, _) => Dispatcher.Invoke(OpenSettingsWindow);

            _autostartTrayItem = new Forms.ToolStripMenuItem("Avvio automatico con Windows")
            {
                CheckOnClick = true,
                Checked = _autostartService.IsEnabled()
            };
            _autostartTrayItem.Click += (_, _) =>
            {
                bool target = _autostartTrayItem.Checked;
                bool ok = _autostartService.SetEnabled(target);
                if (!ok)
                {
                    _autostartTrayItem.Checked = _autostartService.IsEnabled();
                }
            };

            var exitItem = new Forms.ToolStripMenuItem("Esci");
            exitItem.Click += (_, _) => Dispatcher.Invoke(RequestExit);

            menu.Opening += (_, _) =>
            {
                if (_autostartTrayItem is not null)
                {
                    _autostartTrayItem.Checked = _autostartService.IsEnabled();
                }
            };

            menu.Items.Add(centerItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(_autostartTrayItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _trayIconHandle,
                Text = "Pixel Desktop Widget",
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettingsWindow);
        }

        public void OpenSettingsWindow()
        {
            if (_settingsWindow is not null)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Show();
                _settingsWindow.Activate();
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;
                _settingsWindow.Focus();
                return;
            }

            if (_widgetWindow is not null)
            {
                _settings = _widgetWindow.CurrentSettings.Clone();
            }

            _settingsWindow = new SettingsWindow(
                _settings.Clone(),
                _weatherService,
                _autostartService,
                updatedSettings =>
                {
                    bool shouldRefreshWeather =
                        updatedSettings.WeatherModel != _settings.WeatherModel ||
                        Math.Abs(updatedSettings.Latitude - _settings.Latitude) > 0.0001 ||
                        Math.Abs(updatedSettings.Longitude - _settings.Longitude) > 0.0001 ||
                        !string.Equals(updatedSettings.CityName, _settings.CityName, StringComparison.Ordinal);

                    _settings = updatedSettings.Clone();
                    _widgetWindow?.ApplySettings(_settings, refreshWeather: shouldRefreshWeather);
                    if (_widgetWindow is not null && _settingsWindow is not null)
                    {
                        _settingsWindow.ApplyPalette(_widgetWindow.CurrentPalette, _settings.ThemeMode == WidgetThemeMode.MaterialLight);
                    }
                    _saveDebounceTimer?.Stop();
                    _saveDebounceTimer?.Start();
                },
                () => _widgetWindow?.ResetToDefaultPosition(),
                previewKind => _widgetWindow?.SetAnimationPreview(previewKind),
                () => _widgetWindow is not null ? _widgetWindow.RefreshWeatherAsync() : System.Threading.Tasks.Task.CompletedTask,
                _widgetWindow?.CurrentPalette);

            _settingsWindow.Closed += (_, _) =>
            {
                if (_saveDebounceTimer is { IsEnabled: true })
                {
                    _saveDebounceTimer.Stop();
                    _settingsService.Save(_settings);
                }
                _settingsWindow = null;
            };
            _settingsWindow.Show();
            _settingsWindow.Activate();
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
            _settingsWindow.Focus();
        }

        private void RequestExit()
        {
            Shutdown(0);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_widgetWindow is not null)
                {
                    _settingsService.Save(_widgetWindow.CurrentSettings);
                }
            }
            catch
            {
                // Ignora errori in uscita
            }

            if (_notifyIcon is not null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_trayIconHandle is not null)
            {
                _trayIconHandle.Dispose();
                _trayIconHandle = null;
            }

            if (_singleInstanceMutex is not null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch
                {
                    // Ignora se non posseduto
                }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }

        private sealed class DarkTrayMenuRenderer : Forms.ToolStripProfessionalRenderer
        {
            public DarkTrayMenuRenderer() : base(new DarkColorTable())
            {
            }

            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = Drawing.Color.FromArgb(0xF1, 0xF5, 0xF9);
                base.OnRenderItemText(e);
            }
        }

        private sealed class DarkColorTable : Forms.ProfessionalColorTable
        {
            public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(0x17, 0x1F, 0x2E);
            public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(0x2C, 0x38, 0x4E);
            public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(0x3B, 0x4C, 0x6B);
            public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(0x24, 0x31, 0x47);
            public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(0x2C, 0x38, 0x4E);
            public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(0x2C, 0x38, 0x4E);
            public override Drawing.Color CheckBackground => Drawing.Color.FromArgb(0x25, 0x3B, 0x63);
            public override Drawing.Color CheckSelectedBackground => Drawing.Color.FromArgb(0x30, 0x4C, 0x7D);
        }
    }
}
