namespace CMPS4110_NorthOaksProj.Models.Chat.Dtos
{
    public class CreateChatMessageDto
    {
        public int SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Response { get; set; }
    }
}
