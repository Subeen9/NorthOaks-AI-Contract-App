using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using Microsoft.EntityFrameworkCore;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Data.Services.Generation;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Sessions;

namespace CMPS4110_NorthOaksProj.Data.Services.Chat.Messages
{
    public class ChatMessagesService : EntityBaseRepository<ChatMessage>, IChatMessagesService
    {
        private readonly DataContext _context;
        private readonly MessageEmbeddingService _messageEmbeddings;
        private readonly IQdrantService _qdrantService;
        private readonly IOllamaGenerationClient _generationClient;
        private readonly ILogger<ChatMessagesService> _logger;

        public ChatMessagesService(
            DataContext context,
            MessageEmbeddingService messageEmbeddings,
            IQdrantService qdrantService, IOllamaGenerationClient generationClient, ILogger<ChatMessagesService> logger) : base(context)
        {
            _context = context;
            _messageEmbeddings = messageEmbeddings;
            _qdrantService = qdrantService;
            _generationClient = generationClient;
            _logger = logger;
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
                Message = dto.Message
            };

            _context.ChatMessages.Add(entity);
            await _context.SaveChangesAsync();

            // 1. Generate embedding for the message
            var vector = await _messageEmbeddings.EmbedMessageAsync(dto.Message);

            // 2. Search Qdrant for relevant contract chunks
            var results = await _qdrantService.SearchSimilarAsync(vector, limit: 5);

            _logger.LogInformation("Search returned {Count} results for message: {Message}",
                    results.Count, dto.Message);

            foreach (var r in results)
            {
                Console.WriteLine($"[Qdrant] score={r.Score:F2} | contract={r.ContractId} | chunk={r.ChunkText}");
            }

            // 3. For now, store top chunks as the "Response"
            entity.Response = string.Join("\n", results.Select(r => $"{r.Score:F2} | {r.ChunkText}"));

            await _context.SaveChangesAsync();

            return MapToDto(entity);
        }

        public async Task<bool> Delete(int id)
        {
            var msg = await GetByIdAsync(id);
            if (msg == null) return false;
            await DeleteAsync(id);
            return true;
        }
        private static ChatMessageDto MapToDto(ChatMessage entity)
        {
            return new ChatMessageDto
            {
                Id = entity.Id,
                SessionId = entity.SessionId,
                Message = entity.Message,
                Response = entity.Response,
                Timestamp = entity.Timestamp
            };
        }
    }
}
