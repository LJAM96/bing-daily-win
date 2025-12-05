using System.IO;
using System.Text.Json;
using BingDaily.Models;

namespace BingDaily.Services;

public class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BingDaily");
    
    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null)
                {
                    // Ensure resolution is properly deserialized
                    if (settings.Resolution == null || string.IsNullOrEmpty(settings.Resolution.Id))
                    {
                        settings.Resolution = ResolutionOption.Uhd4K;
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // Return default settings on error
        }
        
        return new Settings();
    }

    public void Save(Settings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }
            
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail on save errors
        }
    }
}
