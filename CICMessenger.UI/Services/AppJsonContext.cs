using System.Collections.Generic;
using System.Text.Json.Serialization;
using CICMessenger.UI.Models;
using CICMessenger.UI.ViewModel;

namespace CICMessenger.UI.Services;

/// <summary>
/// Source-generated JSON contract for local settings/rooms persistence, so serialization
/// doesn't rely on reflection that breaks under trimming/AOT.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsViewModel))]
[JsonSerializable(typeof(List<Room>))]
partial class AppJsonContext : JsonSerializerContext
{
}
