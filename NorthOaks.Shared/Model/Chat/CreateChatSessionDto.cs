using System.Collections.Generic;

namespace NorthOaks.Shared.Model.Chat
{
    public class CreateChatSessionDto
    {
        public int UserId { get; set; }
        public List<int>? ContractIds { get; set; }
    }
}
