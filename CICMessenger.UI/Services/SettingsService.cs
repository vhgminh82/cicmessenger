using System;
using System.IO;
using System.Text.Json;
using CICMessenger.UI.ViewModel;

namespace CICMessenger.UI.Services;

public class SettingsService
{
    // CICMESSENGER_INSTANCE isolates settings/identity per instance so several copies
    // can run side by side on one machine (e.g. for local P2P testing).
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CICMESSENGER_INSTANCE"))
            ? "CICMessenger"
            : $"CICMessenger_{Environment.GetEnvironmentVariable("CICMESSENGER_INSTANCE")}");

    /// <summary>The per-instance app data folder, exposed so other local stores (rooms, etc.) live alongside settings.</summary>
    public static string Folder => SettingsFolder;

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    private static readonly string ClientIdFile = Path.Combine(SettingsFolder, "clientid.txt");

    public SettingsViewModel Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.SettingsViewModel) ?? new SettingsViewModel();
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
            var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.SettingsViewModel);
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
