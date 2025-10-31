using CMPS4110_NorthOaksProj.Data.Base;
using CMPS4110_NorthOaksProj.Data.Services.Chat.Sessions;
using CMPS4110_NorthOaksProj.Data.Services.DocumentProcessing;
using CMPS4110_NorthOaksProj.Data.Services.Embeddings;
using CMPS4110_NorthOaksProj.Data.Services.Generation;
using CMPS4110_NorthOaksProj.Data.Services.QDrant;
using CMPS4110_NorthOaksProj.Models.Chat;
using Microsoft.EntityFrameworkCore;
using NorthOaks.Shared.Model.Chat;
using System.Text;
using System.Text.RegularExpressions;

namespace CMPS4110_NorthOaksProj.Data.Services.Chat.Messages
{
    public class ChatMessagesService : EntityBaseRepository<ChatMessage>, IChatMessagesService
    {
        // Summary intent detection 
        private static readonly Regex SummaryRegex = new(
            @"\b(summarize|summary|summarise|summarzie|summery|tl;dr|tldr|short\s+version|condense|brief\s+me|give\s+me\s+a\s+summary)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // optimization constants for 100 second LLM timeout
        private const int MAX_EXCERPTS_TOTAL = 40;
        private const int MAX_EXCERPTS_PER_DOC = 30;
        private const int MAX_CONTEXT_CHARS = 9000;
        private const int MIN_CHUNK_LEN = 20;

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

        private string CleanGeneratedResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = Regex.Replace(text, @"Clause\d+: Chunk\d+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^[\*\+\-]\s*", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();

            return text;
        }

        public async Task<ChatMessageDto?> Create(CreateChatMessageDto dto)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var exists = await _context.ChatSessions.AsNoTracking().AnyAsync(s => s.Id == dto.SessionId);
            if (!exists) return null;

            var entity = new ChatMessage
            {
                SessionId = dto.SessionId,
                Message = dto.Message
            };

            _context.ChatMessages.Add(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("DB save took {Ms}ms", sw.ElapsedMilliseconds);
            sw.Restart();

            try
            {
                // ===== FAST SUMMARY path =====
                if (IsSummaryIntent(dto.Message))
                {
                    _logger.LogInformation("Summary intent detected for session {SessionId}", dto.SessionId);
                    var summaryResponse = await HandleSummaryAsync(dto.SessionId, dto.Message);
                    _logger.LogInformation("Summary took {Ms}ms", sw.ElapsedMilliseconds);
                    entity.Response = summaryResponse;
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }
                else
                {
                    // ===== RAG path =====
                    _logger.LogInformation("Starting RAG pipeline for: {Message}", dto.Message);

                    var vector = await _messageEmbeddings.EmbedMessageAsync(dto.Message);
                    _logger.LogInformation("Message embedding took {Ms}ms", sw.ElapsedMilliseconds);
                    sw.Restart();

                    var results = await _qdrantService.SearchSimilarAsync(vector, limit: 20, scoreThreshold: 0.15f);
                    _logger.LogInformation("Vector search took {Ms}ms, found {Count} results",
                        sw.ElapsedMilliseconds, results.Count);
                    sw.Restart();

                    if (results.Count == 0)
                    {
                        entity.Response = "I'm sorry, I couldn't find any relevant information to answer your question.";
                        await _context.SaveChangesAsync();
                        return MapToDto(entity);
                    }

                    // Deduplicate overlapping chunks
                    var dedupedTexts = ContextBuilder.DeduplicateChunks(results.Select(r => r.ChunkText).ToList());
                    var filteredResults = results
                        .Where(r => dedupedTexts.Contains(r.ChunkText))
                        .Take(12)
                        .ToList();

                    _logger.LogInformation("Deduplication: {Original} → {Filtered} chunks",
                        results.Count, filteredResults.Count);

                    // Build structured clause context
                    var contextText = ContextBuilder.BuildStructuredContext(filteredResults);
                    _logger.LogInformation("Context built: {Length} characters", contextText.Length);

                    var systemPrompt = @"
You are a professional contract analysis assistant.
Use only the clauses provided in the context to answer the question.
Do NOT include internal labels, clause numbers, chunk identifiers, or any metadata in the output.
Provide a clean, concise, professional, human-readable response.
If the answer cannot be found, reply exactly: 'Not found in contract.'
Avoid using jargon or technical references unrelated to the user question.
";

                    var userPrompt = $@"
Context:
{contextText}

User question:
{dto.Message}

Answer:
";

                    _logger.LogInformation("Calling LLM generation (model: {Model})", "llama3.2");
                    _logger.LogInformation("Prompt size: system={SystemLen}chars, user={UserLen}chars",
                        systemPrompt.Length, userPrompt.Length);

                    sw.Restart();
                    var rawresponse = await _generationClient.GenerateAsync(
                        userPrompt,
                        systemPrompt
                    );
                    var generatedResponse = CleanGeneratedResponse(rawresponse);
                    _logger.LogInformation("LLM generation took {Ms}ms, response length: {Len}",
                        sw.ElapsedMilliseconds, generatedResponse?.Length ?? 0);

                    if (string.IsNullOrWhiteSpace(generatedResponse))
                    {
                        _logger.LogWarning("Empty response from LLM!");
                        entity.Response = "The model returned an empty response. Please try rephrasing your question.";
                    }
                    else
                    {
                        entity.Response = generatedResponse;
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully generated response for message {MessageId}", entity.Id);

                    var responseDto = MapToDto(entity);
                    responseDto.Sources = results.Select(r => new ChatMessageSourceDto
                    {
                        ContractId = r.ContractId,
                        ChunkText = r.ChunkText,
                        ChunkIndex = r.ChunkIndex,
                        PageNumber = r.PageNumber,
                        SimilarityScore = (double)r.Score
                    }).ToList();

                    _logger.LogInformation("Returning {Count} sources with response", responseDto.Sources.Count);
                    return responseDto;
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogError(ex, "OPERATION CANCELED after {Ms}ms at: {StackTrace}",
                    sw.ElapsedMilliseconds, ex.StackTrace);
                entity.Response = "The request was canceled due to timeout (100 seconds).";
                await _context.SaveChangesAsync();
                return MapToDto(entity);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP ERROR: {Message}, StatusCode: {StatusCode}",
                    ex.Message, ex.StatusCode);
                entity.Response = $"Connection error: {ex.Message}. Is Ollama running?";
                await _context.SaveChangesAsync();
                return MapToDto(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UNEXPECTED ERROR after {Ms}ms: {ExceptionType} - {Message}",
                    sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
                entity.Response = $"Error: {ex.Message}";
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

        private static ChatMessageDto MapToDto(ChatMessage entity) => new ChatMessageDto
        {
            Id = entity.Id,
            SessionId = entity.SessionId,
            Message = entity.Message,
            Response = entity.Response,
            Timestamp = entity.Timestamp,
            Sources = new List<ChatMessageSourceDto>()
        };

        private static bool IsSummaryIntent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return SummaryRegex.IsMatch(text);
        }

        private async Task<string> HandleSummaryAsync(int sessionId, string userMessage)
        {
            var contractIds = await _context.ChatSessionContracts
                .AsNoTracking()
                .Where(x => x.ChatSessionId == sessionId)
                .Select(x => x.ContractId)
                .Distinct()
                .ToListAsync();

            _logger.LogInformation("Found {Count} contracts for session {SessionId}",
                contractIds.Count, sessionId);

            if (contractIds.Count == 0)
                return "I don't see any document linked to this chat to summarize.";

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

            _logger.LogInformation("Context built: {Chunks} chunks, {Chars} chars",
                totalTaken, totalChars);

            if (totalTaken == 0)
                return "I couldn't extract enough readable text to summarize.";

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

            try
            {
                _logger.LogInformation("Calling LLM for summary generation");

                var result = await _generationClient.GenerateAsync(
                    prompt: userPrompt.ToString(),
                    systemPrompt: systemPrompt
                );

                return result;
            }
            catch (OperationCanceledException)
            {
                return "Exception: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.";
            }
            catch (Exception ex)
            {
                return "I couldn't generate the summary due to an internal issue. Please try again.";
            }
        }
    }
}