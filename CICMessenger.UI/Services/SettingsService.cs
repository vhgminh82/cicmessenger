using System;
using System.IO;
using System.Text.Json;
using CICMessenger.UI.ViewModel;

namespace CICMessenger.UI.Services;

public class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CICMessenger");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    private static readonly string ClientIdFile = Path.Combine(SettingsFolder, "clientid.txt");

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

    /// <summary>
    /// Returns a client identity that stays stable across app restarts (persisted to disk),
    /// so the same user isn't seen by peers as a brand-new buddy every time they relaunch.
    /// </summary>
    public static string GetOrCreateClientId()
    {
        try
        {
            if (File.Exists(ClientIdFile))
            {
                var existing = File.ReadAllText(ClientIdFile).Trim();
                if (!string.IsNullOrEmpty(existing))
                    return existing;
            }

            var id = Guid.NewGuid().ToString();
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllText(ClientIdFile, id);
            return id;
        }
        catch
        {
            // Best effort — fall back to a session-only id if disk isn't writable
            return Guid.NewGuid().ToString();
        }
    }
}
