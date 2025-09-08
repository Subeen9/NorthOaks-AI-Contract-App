using System;

namespace CMPS4110_NorthOaksProj.Models.Chat
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Response { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public ChatSession Session { get; set; } = null!;
    }
}
