using System;
using System.IO;
using System.Text.Json;
using Squiggle.UI.ViewModel;

namespace Squiggle.UI.Services;

public class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Squiggle");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsViewModel Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<SettingsViewModel>(json, JsonOptions) ?? new SettingsViewModel();
            }
        }
        catch
        {
            // If settings file is corrupt, return defaults
        }

        return new SettingsViewModel();
    }

    public void Save(SettingsViewModel settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Best effort — don't crash if we can't save
        }
    }
}
