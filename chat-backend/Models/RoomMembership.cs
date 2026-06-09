namespace ChatApp.Models;

public class RoomMembership
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public MemberRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}
