using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Chat;
//using CMPS4110_NorthOaksProj.Models.Chat.Dtos;
using NorthOaks.Shared.Model.Chat;
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

            try
            {
                // 1. Generate embedding for the message
                var vector = await _messageEmbeddings.EmbedMessageAsync(dto.Message);

                // 2. Search Qdrant for relevant contract chunks
                var results = await _qdrantService.SearchSimilarAsync(vector, limit: 5, scoreThreshold: 0.3f);

                _logger.LogInformation("Search returned {Count} results for message: {Message}",
                        results.Count, dto.Message);

                // 3. checks if we found relevant chunks
                if (results.Count == 0)
                {
                    entity.Response = "I'm sorry, I couldn't find any relevant information to answer your question."; 

                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                // 4. Build context from retrieved chunks
                var context = string.Join("\n\n", results.Select((r, i) =>
                    $"[Context {i + 1}]:\n{r.ChunkText}"));

                // 5. building the rag prompt
                var SystemPrompt = @"You are an AI Assitant which only have Knowledge based ON Provided context. You need to answer based on the following rules. 
                                       Rules:
                                       - Answer based ONLY on the information in the context provided
                                       - If the context doesn't contain enough information to answer the question, say so clearly
                                       - Do not use any external knowledge or make assumptions
                                       - Give clear, concise, professional and Human Like Answer
                                       - Do not make up information";

                var userPrompt = $@"   {context}
                                       {dto.Message}
                                       Answer in a clear and concise manner based on the context above. If you can't answer from the context, say so.";

                // 6. generate response using LLM
                var generatedResponse = await _generationClient.GenerateAsync(prompt: userPrompt, systemPrompt: SystemPrompt);

                // 7. save the generated response
                entity.Response = generatedResponse;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully generated response for message {MessageId}", entity.Id);

                return MapToDto(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: {Message}", dto.Message);
                entity.Response = "I encountered an error while processing your question. Please try again.";
                await _context.SaveChangesAsync();
                return MapToDto(entity);

            }
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
