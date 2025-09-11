using System;
using System.Collections.Generic;

namespace CMPS4110_NorthOaksProj.Models.Chat.Dtos
{
    public class ChatSessionDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedDate { get; set; }
        public int MessageCount { get; set; }
        public List<int> ContractIds { get; set; } = new();
    }
}
