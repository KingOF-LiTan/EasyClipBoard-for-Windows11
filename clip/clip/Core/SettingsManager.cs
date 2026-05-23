using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace clip.Core;

public static class SettingsManager
{
    // Use a 'data' folder next to the exe — ApplicationData.Current throws for unpackaged apps
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
    private static Dictionary<string, JsonElement> _settings = new();
    private static readonly object _lock = new();

    static SettingsManager()
    {
        Directory.CreateDirectory(DataDir); // Ensure the directory exists
        Load();
    }

    public static void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (dict != null)
                    {
                        _settings = dict;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }
    }

    public static void Save()
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }

    public static T? Get<T>(string key, T? defaultValue = default)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var jsonElement))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }
    }

    public static void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(value);
                _settings[key] = JsonSerializer.Deserialize<JsonElement>(json);
                Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set setting {key}: {ex.Message}");
            }
        }
    }
}
