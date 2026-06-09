namespace ChatApp.Models;

public class Conversation
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ConversationParticipant> Participants { get; set; } = [];
    public List<DirectMessage> Messages { get; set; } = [];
}
