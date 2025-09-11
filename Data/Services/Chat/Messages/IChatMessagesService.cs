using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CMPS4110_NorthOaksProj.Data.Services.Chat.Messages
{
    public interface IChatMessagesService : IEntityBaseRepository<ChatMessage>
    {
        Task<IEnumerable<ChatMessageDto>> GetBySession(int sessionId);
        Task<ChatMessageDto?> Create(CreateChatMessageDto dto);
        Task<bool> Delete(int id);
    }
}
