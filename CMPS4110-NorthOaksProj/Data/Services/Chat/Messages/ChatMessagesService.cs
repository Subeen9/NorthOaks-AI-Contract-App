using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CMPS4110_NorthOaksProj.Data.Services.Chat.Messages
{
    public class ChatMessagesService : EntityBaseRepository<ChatMessage>, IChatMessagesService
    {
        private readonly DataContext _context;
        public ChatMessagesService(DataContext _context) : base(_context)
        {
            _context = _context;
        }
        public async Task<IEnumerable<ChatMessageDto>> GetBySession(int sessionId)
        {
            var exists = await _context.ChatSessions.AnyAsync(s => s.Id == sessionId);
            if (!exists) return Enumerable.Empty<ChatMessageDto>();

            return await _context.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SessionId = m.SessionId,
                    Message = m.Message,
                    Response = m.Response,
                    Timestamp = m.Timestamp
                })
                .ToListAsync();
        }

        public async Task<ChatMessageDto?> Create(CreateChatMessageDto dto)
        {
            var exists = await _context.ChatSessions.AnyAsync(s => s.Id == dto.SessionId);
            if (!exists) return null;

            var entity = new ChatMessage
            {
                SessionId = dto.SessionId,
                Message = dto.Message,
                Response = dto.Response
            };

            _context.ChatMessages.Add(entity);
            await _context.SaveChangesAsync();

            return new ChatMessageDto
            {
                Id = entity.Id,
                SessionId = entity.SessionId,
                Message = entity.Message,
                Response = entity.Response,
                Timestamp = entity.Timestamp
            };
        }

        public async Task<bool> Delete(int id)
        {
            var msg = await GetByIdAsync(id);
            if (msg == null) return false;
            await DeleteAsync(id);
            return true;
        }


    }
}