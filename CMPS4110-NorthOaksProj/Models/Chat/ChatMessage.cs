using CMPS4110_NorthOaksProj.Data.Base;
using System;
namespace CMPS4110_NorthOaksProj.Models.Chat
{
    public class ChatMessage : IEntityBase
    {
        public int Id { get; set; }
        public int SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Response { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? SourcesJson { get; set; }
        public ChatSession Session { get; set; } = null!;
    }
}