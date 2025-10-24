using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Models.Chat;
using NorthOaks.Shared.Model.Chat;
using Microsoft.EntityFrameworkCore;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Data.Services.Generation;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Sessions;

using System.Text;
using System.Text.RegularExpressions;
using System.Linq;


namespace CMPS4110_NorthOaksProj.Data.Services.Chat.Messages
{
    public class ChatMessagesService : EntityBaseRepository<ChatMessage>,IChatMessagesService
    {
        // Summary intent detection 
        private static readonly Regex SummaryRegex = new(
            @"\b(summarize|summary|summarise|summarzie|summery|tl;dr|tldr|short\s+version|condense|brief\s+me|give\s+me\s+a\s+summary)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // optimization constants for 100 second LLM timeout
        private const int MAX_EXCERPTS_TOTAL = 40;     // limits total chunks across all docs
        private const int MAX_EXCERPTS_PER_DOC = 30;     // per-doc cap
        private const int MAX_CONTEXT_CHARS = 9000;   // ~6–7k tokens per model
        private const int MIN_CHUNK_LEN = 20;     // skip crumbs

        private readonly DataContext _context;
        private readonly MessageEmbeddingService _messageEmbeddings;
        private readonly IQdrantService _qdrantService;
        private readonly IOllamaGenerationClient _generationClient;
        private readonly ILogger<ChatMessagesService> _logger;

        public ChatMessagesService(
            DataContext context,
            MessageEmbeddingService messageEmbeddings,
            IQdrantService qdrantService,
            IOllamaGenerationClient generationClient,
            ILogger<ChatMessagesService> logger
        ) : base(context)
        {
            _context = context;
            _messageEmbeddings = messageEmbeddings;
            _qdrantService = qdrantService;
            _generationClient = generationClient;
            _logger = logger;
        }

        public async Task<IEnumerable<ChatMessageDto>> GetBySession(int sessionId)
        {
            var exists = await _context.ChatSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId);
            if (!exists) return Enumerable.Empty<ChatMessageDto>();

            return await _context.ChatMessages
                .AsNoTracking()
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
            var exists = await _context.ChatSessions.AsNoTracking().AnyAsync(s => s.Id == dto.SessionId);
            if (!exists) return null;

            var entity = new ChatMessage
            {
                SessionId = dto.SessionId,
                Message = dto.Message
            };

            try
            {
                // Generate summary response
                if (IsSummaryIntent(dto.Message))
                {
                    _logger.LogInformation("Summary headsup detected for session {SessionId}", dto.SessionId);
                    entity.Response = await HandleSummaryAsync(dto.SessionId, dto.Message);
                }
                else
                {
                    //Rag response
                    var vector = await _messageEmbeddings.EmbedMessageAsync(dto.Message);
                    var results = await _qdrantService.SearchSimilarAsync(vector, limit: 12, scoreThreshold: 0.2f);


                    _logger.LogInformation("Search returned {Count} results for message: {Message}",
                            results.Count, dto.Message);

                    if (results.Count == 0)
                    {
                        entity.Response = "I'm sorry, I couldn't find any relevant information to answer your question.";
                    }
                    else
                    {
                        var contextText = string.Join("\n\n", results.Select((r, i) =>
                           $"[Context {i + 1}]:\n{r.ChunkText}"));

                        var systemPrompt = "Answer ONLY from the provided context. Be clear and concise. If the context is insufficient, say so plainly. Do not invent facts.";
                        var userPrompt = $@"{contextText} User question: {dto.Message} Answer concisely using ONLY the context above. If you can't answer from the context, say so.";

                        var generatedResponse = await _generationClient.GenerateAsync(userPrompt, systemPrompt);

                        entity.Response = generatedResponse;
                        _logger.LogInformation("Successfully generated response for message {MessageId}", entity.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                entity.Response = "⚠️ Exception: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: {Message}", dto.Message);
                entity.Response = "I encountered an error while processing your question. Please try again.";
            }
            _context.ChatMessages.Add(entity);
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

        private static ChatMessageDto MapToDto(ChatMessage entity) => new ChatMessageDto
        {
            Id = entity.Id,
            SessionId = entity.SessionId,
            Message = entity.Message,
            Response = entity.Response,
            Timestamp = entity.Timestamp
        };

        // intent detection
        private static bool IsSummaryIntent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return SummaryRegex.IsMatch(text);
        }

        // summary handler
        private async Task<string> HandleSummaryAsync(int sessionId, string userMessage)
        {
            //  contracts linked to session
            var contractIds = await _context.ChatSessionContracts
                .AsNoTracking()
                .Where(x => x.ChatSessionId == sessionId)
                .Select(x => x.ContractId)
                .Distinct()
                .ToListAsync();

            if (contractIds.Count == 0)
                return "I don’t see any document linked to this chat to summarize.";

            //  pulls text chunks with limits
            var rawChunks = await _context.ContractEmbeddings
                .AsNoTracking()
                .Where(e => contractIds.Contains(e.ContractId))
                .OrderBy(e => e.ContractId)
                .ThenBy(e => e.ChunkIndex)
                .Take(MAX_EXCERPTS_TOTAL)
                .Select(e => new { e.ContractId, e.ChunkIndex, e.ChunkText })
                .ToListAsync();

            if (rawChunks.Count == 0)
                return "No text was found to summarize for the linked document(s).";

            //  Build context with limits
            var sb = new StringBuilder(MAX_CONTEXT_CHARS + 512);
            var totalChars = 0;
            var totalTaken = 0;

            sb.AppendLine("=== DOCUMENT EXCERPTS START ===");

            foreach (var g in rawChunks.GroupBy(c => c.ContractId).OrderBy(g => g.Key))
            {
                int takenForThisDoc = 0;
                foreach (var c in g)
                {
                    if (totalTaken >= MAX_EXCERPTS_TOTAL) break;
                    if (takenForThisDoc >= MAX_EXCERPTS_PER_DOC) break;

                    var text = (c.ChunkText ?? string.Empty).Trim();
                    if (text.Length < MIN_CHUNK_LEN) continue;

                    if (totalChars + text.Length + 64 > MAX_CONTEXT_CHARS) break;

                    sb.Append("--- Document #").Append(g.Key)
                      .Append(" | Chunk ")
                      .Append(c.ChunkIndex)
                      .AppendLine(" ---")
                      .AppendLine(text)
                      .AppendLine();

                    takenForThisDoc++;
                    totalTaken++;
                    totalChars += text.Length;

                }
                if (totalTaken >= MAX_EXCERPTS_TOTAL) break;
            }

            sb.AppendLine("=== DOCUMENT EXCERPTS END ===");

            if (totalTaken == 0)
                return "I couldn’t extract enough readable text to summarize.";

            // System + user prompt
            var systemPrompt = "Summarize briefly and faithfully. Use ONLY the provided text. Keep it under 200 words. Do not invent facts.";
            var userPrompt = new StringBuilder(2048);
            userPrompt.AppendLine("Summarize the following text clearly and briefly.");
            userPrompt.AppendLine("Focus on the main points, important facts, names, and numbers.");
            userPrompt.AppendLine("Use concise paragraphs.");
            userPrompt.AppendLine();
            userPrompt.AppendLine("=== DOCUMENT START ===");
            userPrompt.AppendLine(sb.ToString());
            userPrompt.AppendLine("=== DOCUMENT END ===");
            userPrompt.AppendLine();
            userPrompt.AppendLine("User request (if any):");
            userPrompt.AppendLine(userMessage ?? string.Empty);
            userPrompt.AppendLine();
            userPrompt.AppendLine("Return only the final summary text — no introductions or headings.");

            //   LLM call 
            try
            {
                return await _generationClient.GenerateAsync(
                    prompt: userPrompt.ToString(),
                    systemPrompt: systemPrompt
                );
            }
            catch (OperationCanceledException)
            {
                return "Exception: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fast summary generation failed.");
                return "I couldn’t generate the summary due to an internal issue. Please try again.";
            }
        }
    }
}


