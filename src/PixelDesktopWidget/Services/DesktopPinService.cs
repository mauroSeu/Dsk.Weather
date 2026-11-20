using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using PixelDesktopWidget.Models;

namespace PixelDesktopWidget.Services
{
    public sealed class DesktopPinService
    {
        private const int GwlExStyle = -20;
        private const int GwlpHwndParent = -8;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExAppWindow = 0x00040000L;

        private const int WmWindowPosChanging = 0x0046;
        private const int WmSysCommand = 0x0112;
        private const int WmSettingChange = 0x001A;
        private const int WmDwmColorizationColorChanged = 0x0320;
        private const int ScMinimize = 0xF020;
        private const int SpiSetDesktopWallpaper = 0x0014;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private static readonly IntPtr HwndBottom = new IntPtr(1);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindowExW(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private Window? _window;
        private HwndSource? _hwndSource;
        private IntPtr _hwnd;
        private WindowPinMode _mode = WindowPinMode.DesktopPinned;
        private Action? _onWallpaperOrThemeChanged;
        private uint _taskbarCreatedMsg;
        private bool _isShowDesktopActive;

        public void Attach(Window window, WindowPinMode mode, Action onWallpaperOrThemeChanged)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _mode = mode;
            _onWallpaperOrThemeChanged = onWallpaperOrThemeChanged;
            _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");

            var helper = new WindowInteropHelper(window);
            _hwnd = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);

            ApplyToolWindowStyle(_hwnd);
            ApplyMode(mode);
        }

        public void ApplyMode(WindowPinMode mode)
        {
            _mode = mode;
            if (_window is null || _hwnd == IntPtr.Zero)
            {
                return;
            }

            if (mode == WindowPinMode.AlwaysOnTop)
            {
                SetWindowLongPtr(_hwnd, GwlpHwndParent, IntPtr.Zero);
                _window.Topmost = true;
                return;
            }

            _window.Topmost = false;
            // Evita GWLP_HWNDPARENT cross-process verso explorer.exe (Progman/WorkerW),
            // che collega le code di input e può causare freeze all'apertura di finestre figlie o menu.
            SetWindowLongPtr(_hwnd, GwlpHwndParent, IntPtr.Zero);
            SetWindowPos(_hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        public void Detach()
        {
            if (_hwndSource is not null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            if (_hwnd != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GwlpHwndParent, IntPtr.Zero);
                _hwnd = IntPtr.Zero;
            }

            _window = null;
        }

        public static IntPtr FindDesktopOwner()
        {
            try
            {
                IntPtr progman = FindWindowW("Progman", null);
                if (progman != IntPtr.Zero)
                {
                    IntPtr defViewInProgman = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defViewInProgman != IntPtr.Zero)
                    {
                        return progman;
                    }
                }

                IntPtr foundWorker = IntPtr.Zero;
                EnumWindows((hWnd, _) =>
                {
                    var className = new StringBuilder(64);
                    if (GetClassNameW(hWnd, className, className.Capacity) > 0 &&
                        string.Equals(className.ToString(), "WorkerW", StringComparison.Ordinal))
                    {
                        IntPtr defView = FindWindowExW(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (defView != IntPtr.Zero)
                        {
                            foundWorker = hWnd;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                return foundWorker;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
            {
                ApplyMode(_mode);
                return IntPtr.Zero;
            }

            switch (msg)
            {
                case WmWindowPosChanging:
                    if ((_mode == WindowPinMode.DesktopPinned || _mode == WindowPinMode.BottomMost) && !_isShowDesktopActive)
                    {
                        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                        if ((pos.flags & SwpNoZOrder) == 0)
                        {
                            pos.hwndInsertAfter = HwndBottom;
                            Marshal.StructureToPtr(pos, lParam, false);
                        }
                    }
                    break;

                case WmSysCommand:
                    if ((wParam.ToInt64() & 0xFFF0L) == ScMinimize)
                    {
                        handled = true;
                    }
                    break;

                case WmSettingChange:
                    if (wParam.ToInt64() == SpiSetDesktopWallpaper)
                    {
                        _onWallpaperOrThemeChanged?.Invoke();
                    }
                    break;

                case WmDwmColorizationColorChanged:
                    _onWallpaperOrThemeChanged?.Invoke();
                    break;
            }

            return IntPtr.Zero;
        }

        public void CheckDesktopForegroundState()
        {
            if (_hwnd == IntPtr.Zero || _mode != WindowPinMode.DesktopPinned)
            {
                _isShowDesktopActive = false;
                return;
            }

            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == _hwnd)
                {
                    return;
                }

                var cls = new StringBuilder(64);
                GetClassNameW(fg, cls, cls.Capacity);
                string clsName = cls.ToString();
                bool isDesktopShell = string.Equals(clsName, "WorkerW", StringComparison.Ordinal) ||
                                      string.Equals(clsName, "Progman", StringComparison.Ordinal);

                if (isDesktopShell && !_isShowDesktopActive)
                {
                    _isShowDesktopActive = true;
                    SetWindowPos(_hwnd, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                }
                else if (!isDesktopShell && _isShowDesktopActive)
                {
                    _isShowDesktopActive = false;
                    SetWindowPos(_hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                }
            }
            catch
            {
                // Ignora errori Win32 non critici
            }
        }

        private static void ApplyToolWindowStyle(IntPtr hwnd)
        {
            long exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            exStyle |= WsExToolWindow;
            exStyle &= ~WsExAppWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }
}
