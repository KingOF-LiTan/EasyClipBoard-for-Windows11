using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace clip.Core.Support;

public static class StartupService
{
    private const string AppName = "WinClipboard";
    private static readonly string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string ExecutablePath
    {
        get
        {
            // For unpackaged apps, use MainModule.FileName
            // process.MainModule can be null in some contexts, but usually fine for desktop apps.
            // If packaged (MSIX), this might point to a virtual path or system shim.
            // But we assume unpackaged for Registry method.
            using var process = Process.GetCurrentProcess();
            return process.MainModule?.FileName ?? Environment.ProcessPath ?? "";
        }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
            var path = key?.GetValue(AppName) as string;
            return !string.IsNullOrEmpty(path) && string.Equals(path, ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (enable)
            {
                key?.SetValue(AppName, ExecutablePath);
            }
            else
            {
                key?.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartupService] Failed to set auto start: {ex.Message}");
        }
    }
}
