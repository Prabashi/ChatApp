namespace ChatApp.Models;

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<RoomMembership> Memberships { get; set; } = [];
    public List<RoomMessage> Messages { get; set; } = [];
}
