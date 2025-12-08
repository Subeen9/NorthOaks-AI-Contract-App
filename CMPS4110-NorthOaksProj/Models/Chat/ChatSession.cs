using System;
using System.Collections.Generic;

namespace CMPS4110_NorthOaksProj.Models.Chat
{
    public enum ChatSessionType
    {
        Single = 0,
        Comparison = 1
    }

    public class ChatSession
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public ChatSessionType SessionType { get; set; } = ChatSessionType.Single;
        public ICollection<ChatSessionContract> SessionContracts { get; set; } = new List<ChatSessionContract>();
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
        public bool IsPublic { get; set; } = false;
    }
}
