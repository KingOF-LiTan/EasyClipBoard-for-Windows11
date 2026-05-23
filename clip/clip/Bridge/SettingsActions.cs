using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace clip.Bridge;

internal sealed class SettingsActions
{
    private static readonly Dictionary<string, uint> CodeToVkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        {"KeyA", 0x41}, {"KeyB", 0x42}, {"KeyC", 0x43}, {"KeyD", 0x44}, {"KeyE", 0x45},
        {"KeyF", 0x46}, {"KeyG", 0x47}, {"KeyH", 0x48}, {"KeyI", 0x49}, {"KeyJ", 0x4A},
        {"KeyK", 0x4B}, {"KeyL", 0x4C}, {"KeyM", 0x4D}, {"KeyN", 0x4E}, {"KeyO", 0x4F},
        {"KeyP", 0x50}, {"KeyQ", 0x51}, {"KeyR", 0x52}, {"KeyS", 0x53}, {"KeyT", 0x54},
        {"KeyU", 0x55}, {"KeyV", 0x56}, {"KeyW", 0x57}, {"KeyX", 0x58}, {"KeyY", 0x59},
        {"KeyZ", 0x5A},
        {"Digit0", 0x30}, {"Digit1", 0x31}, {"Digit2", 0x32}, {"Digit3", 0x33}, {"Digit4", 0x34},
        {"Digit5", 0x35}, {"Digit6", 0x36}, {"Digit7", 0x37}, {"Digit8", 0x38}, {"Digit9", 0x39},
        {"Escape", 0x1B}, {"Space", 0x20}, {"Backspace", 0x08}, {"Tab", 0x09}, {"Enter", 0x0D},
        {"F1", 0x70}, {"F2", 0x71}, {"F3", 0x72}, {"F4", 0x73}, {"F5", 0x74}, {"F6", 0x75},
        {"F7", 0x76}, {"F8", 0x77}, {"F9", 0x78}, {"F10", 0x79}, {"F11", 0x7A}, {"F12", 0x7B},
        {"Backquote", 0xC0}, {"Minus", 0xBD}, {"Equal", 0xBB}, {"BracketLeft", 0xDB},
        {"BracketRight", 0xDD}, {"Backslash", 0xDC}, {"Semicolon", 0xBA}, {"Quote", 0xDE},
        {"Comma", 0xBC}, {"Period", 0xBE}, {"Slash", 0xBF}
    };

    private readonly Func<string, Task> _sendToJs;

    public SettingsActions(Func<string, Task> sendToJs)
    {
        _sendToJs = sendToJs;
    }

    public object GetSettings()
    {
        string? bgPath = Core.SettingsManager.Get<string>("ui_background_path");
        string? bgBase64 = null;

        if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(bgPath);
                string ext = Path.GetExtension(bgPath).TrimStart('.').ToLower();
                if (ext == "jpg") ext = "jpeg";
                bgBase64 = $"data:image/{ext};base64,{Convert.ToBase64String(bytes)}";
            }
            catch { }
        }

        return new
        {
            theme = Core.SettingsManager.Get("theme", 0),
            bgBase64,
            maskOpacity = 0.3,
            blurAmount = 30.0,
            autostart = GetAutostartEnabled(),
            language = Core.SettingsManager.Get<string>("language") ?? "zh",
            modifiers = Core.SettingsManager.Get("hotkey_modifiers", Native.Win32Helper.MOD_CONTROL),
            vk = Core.SettingsManager.Get("hotkey_vk", Native.Win32Helper.VK_TAB),
            code = Core.SettingsManager.Get<string>("hotkey_code") ?? "Tab",
        };
    }

    public object GetAutostart()
    {
        return new { enabled = GetAutostartEnabled() };
    }

    public object SetAutostart(JsonElement root)
    {
        bool enable = root.TryGetProperty("enabled", out var en) && en.GetBoolean();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return BridgeResponse.Fail("registryUnavailable");
            if (enable)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue("WinClipboard", exePath);
            }
            else
            {
                key.DeleteValue("WinClipboard", false);
            }
            return new { success = true, enabled = enable };
        }
        catch (Exception ex)
        {
            return BridgeResponse.Fail(ex.Message);
        }
    }

    public object SetMaskOpacity(JsonElement root)
    {
        double opacity = root.TryGetProperty("opacity", out var op) ? op.GetDouble() : 0.7;
        Core.SettingsManager.Set("ui_background_mask_opacity", opacity);
        return new { success = true };
    }

    public object SetBackground(JsonElement root)
    {
        string? path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
        Core.SettingsManager.Set("ui_background_path", path);
        return new { success = true };
    }

    public object ClearBackground()
    {
        Core.SettingsManager.Set<string?>("ui_background_path", null);
        return new { success = true };
    }

    public object SetTheme(JsonElement root)
    {
        int theme = root.TryGetProperty("theme", out var t) ? t.GetInt32() : 0;
        Core.SettingsManager.Set("theme", theme);

        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            MainWindow.Current?.ApplyTheme(theme);
        });

        return new { success = true, theme };
    }

    public object SetShortcut(JsonElement root)
    {
        uint modifiers = root.TryGetProperty("modifiers", out var mods)
            ? mods.GetUInt32()
            : Native.Win32Helper.MOD_CONTROL;
        string code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        uint vk = CodeToVkMap.TryGetValue(code, out uint mappedVk) ? mappedVk : Native.Win32Helper.VK_TAB;

        bool success = App.Current.Host?.RebindHotKey(modifiers, vk) ?? false;
        if (!success)
        {
            return BridgeResponse.Fail("shortcutError");
        }

        Core.SettingsManager.Set("hotkey_modifiers", modifiers);
        Core.SettingsManager.Set("hotkey_vk", vk);
        Core.SettingsManager.Set("hotkey_code", code);
        return new { success = true, modifiers, vk, code };
    }

    public async Task<object> SetLanguage(JsonElement root)
    {
        string lang = root.TryGetProperty("language", out var l) ? l.GetString() ?? "zh" : "zh";
        Core.SettingsManager.Set("language", lang);

        string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        string localeFile = Path.Combine(wwwroot, "locales", $"{lang}.json");
        string localeJson = File.Exists(localeFile) ? File.ReadAllText(localeFile) : "{}";
        string script = $"window.INITIAL_LOCALE = {localeJson}; window.USER_LANG = '{lang}'; if(typeof applyTranslations==='function') applyTranslations();";
        await _sendToJs(script);

        return new { success = true };
    }

    private static bool GetAutostartEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("WinClipboard") != null;
        }
        catch { return false; }
    }
}
