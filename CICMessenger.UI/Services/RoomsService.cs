using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CICMessenger.UI.Models;

namespace CICMessenger.UI.Services;

/// <summary>Loads/saves the local list of group rooms (name + member ids) as JSON.</summary>
public class RoomsService
{
    private static readonly string RoomsFile = Path.Combine(SettingsService.Folder, "rooms.json");

    public List<Room> Load()
    {
        try
        {
            if (File.Exists(RoomsFile))
            {
                var json = File.ReadAllText(RoomsFile);
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListRoom) ?? new List<Room>();
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
            var json = JsonSerializer.Serialize(rooms, AppJsonContext.Default.ListRoom);
            File.WriteAllText(RoomsFile, json);
        }
        catch
        {
            // Best effort — don't crash if we can't save
        }
    }
}
