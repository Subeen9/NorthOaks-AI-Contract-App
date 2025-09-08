using System.Collections.Generic;

namespace CMPS4110_NorthOaksProj.Models.Chat.Dtos
{
    public class CreateChatSessionDto
    {
        public int UserId { get; set; }
        public List<int>? ContractIds { get; set; }
    }
}
