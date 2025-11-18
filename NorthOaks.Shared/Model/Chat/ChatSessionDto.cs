using System;
using System.Collections.Generic;

namespace NorthOaks.Shared.Model.Chat
{
    public class ChatSessionDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedDate { get; set; }
        public int MessageCount { get; set; }
        public List<int> ContractIds { get; set; } = new();
        public List<ContractInfoDto> Contracts { get; set; } = new();
    }
}
