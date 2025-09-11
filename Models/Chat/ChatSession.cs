using System;
using System.Collections.Generic;

namespace CMPS4110_NorthOaksProj.Models.Chat
{
    public class ChatSession
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        public ICollection<ChatSessionContract> SessionContracts { get; set; } = new List<ChatSessionContract>();
    }
}
