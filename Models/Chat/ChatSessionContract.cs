namespace CMPS4110_NorthOaksProj.Models.Chat
{
    public class ChatSessionContract
    {
        public int Id { get; set; }
        public int ChatSessionId { get; set; }
        public int ContractId { get; set; }

        public ChatSession ChatSession { get; set; } = null!;

    }
}
