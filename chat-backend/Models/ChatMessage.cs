namespace ChatApp.Models;

public class ChatMessage
{
    public int Id { get; set; }
    public string User { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}
