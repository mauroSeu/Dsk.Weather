using System;
using System.IO;
using Microsoft.Win32;

namespace PixelDesktopWidget.Services
{
    public sealed class AutostartService
    {
        public const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RegistryValueName = "PixelDesktopWidget";

        public bool IsEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: false);
                string? value = key?.GetValue(RegistryValueName) as string;
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        public bool SetEnabled(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true);
                if (key is null)
                {
                    return false;
                }

                if (!enabled)
                {
                    key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
                    return true;
                }

                string baseDir = AppContext.BaseDirectory;
                string vbsLauncher = Path.Combine(baseDir, "Avvia-PixelWidget.vbs");
                if (File.Exists(vbsLauncher))
                {
                    key.SetValue(RegistryValueName, $"wscript.exe //B //Nologo \"{vbsLauncher}\"", RegistryValueKind.String);
                    return true;
                }

                string exeCandidate = Path.Combine(baseDir, "PixelDesktopWidget.exe");
                if (File.Exists(exeCandidate))
                {
                    key.SetValue(RegistryValueName, $"\"{exeCandidate}\"", RegistryValueKind.String);
                    return true;
                }

                string? processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
                {
                    return false;
                }

                key.SetValue(RegistryValueName, $"\"{processPath}\"", RegistryValueKind.String);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
