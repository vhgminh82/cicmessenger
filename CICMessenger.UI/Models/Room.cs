using System.Collections.Generic;

namespace CICMessenger.UI.Models;

/// <summary>
/// A named group conversation with a fixed member list, persisted locally so it survives
/// app restarts and keeps the same chat history every time it's reopened.
/// </summary>
public class Room
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public List<string> MemberIds { get; set; } = new();

    /// <summary>
    /// Id of the buddy who created the room. Empty for rooms saved before this field existed;
    /// callers should treat an empty value as "owned by the current user" for back-compat.
    /// </summary>
    public string CreatorId { get; set; } = "";
}
