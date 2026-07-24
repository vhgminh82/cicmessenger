using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CICMessenger.UI.Models;

namespace CICMessenger.UI.Services;

/// <summary>Loads/saves the local list of group rooms (name + member ids) as JSON.</summary>
public class RoomsService
{
    private static readonly string RoomsFile = Path.Combine(SettingsService.Folder, "rooms.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<Room> Load()
    {
        try
        {
            if (File.Exists(RoomsFile))
            {
                var json = File.ReadAllText(RoomsFile);
                return JsonSerializer.Deserialize<List<Room>>(json, JsonOptions) ?? new List<Room>();
            }
        }
        catch
        {
            // Corrupt file — start fresh rather than block the app
        }

        return new List<Room>();
    }

    public void Save(List<Room> rooms)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.Folder);
            var json = JsonSerializer.Serialize(rooms, JsonOptions);
            File.WriteAllText(RoomsFile, json);
        }
        catch
        {
            // Best effort — don't crash if we can't save
        }
    }
}
