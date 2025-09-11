using System;

namespace CMPS4110_NorthOaksProj.Models.Chat.Dtos
{
    public class ChatMessageDto
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Response { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
