namespace NorthOaks.Shared.Model.Chat
{
    public class CreateChatMessageDto
    {
        public int SessionId { get; set; }
        public string Message { get; set; } = string.Empty;
        //public string? Response { get; set; }
    }
}
