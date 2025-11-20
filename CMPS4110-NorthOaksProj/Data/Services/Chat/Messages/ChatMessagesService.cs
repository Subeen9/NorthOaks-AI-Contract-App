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
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CMPS4110_NorthOaksProj.Data.Services.Chat.Messages
{
    public class ChatMessagesService : EntityBaseRepository<ChatMessage>, IChatMessagesService
    {
        
        private static readonly Regex SummaryRegex = new(
            @"\b(summarize|summary|summarise|summarzie|summery|tl;dr|tldr|short\s+version|condense|brief\s+me|give\s+me\s+a\s+summary)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        
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

        private static string CleanFileName(string fileName)
        {
            return Path.GetFileNameWithoutExtension(fileName) ?? "Document";
        }


        public async Task<IEnumerable<ChatMessageDto>> GetBySession(int sessionId)
        {
            var exists = await _context.ChatSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId);
            if (!exists) return Enumerable.Empty<ChatMessageDto>();

            var messages = await _context.ChatMessages
                .AsNoTracking()
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new ChatMessageDto
                {
                    Id = m.Id,
                    SessionId = m.SessionId,
                    Message = m.Message,
                    Response = m.Response,
                    Timestamp = m.Timestamp,
                    Sources = DeserializeSources(m.SourcesJson)
                })
                .ToListAsync();

            return messages;
        }

        private static List<ChatMessageSourceDto> DeserializeSources(string? sourcesJson)
        {
            if (string.IsNullOrWhiteSpace(sourcesJson))
                return new List<ChatMessageSourceDto>();

            try
            {
                var sources = JsonSerializer.Deserialize<List<ChatMessageSourceDto>>(sourcesJson);
                return sources ?? new List<ChatMessageSourceDto>();
            }
            catch
            {
                return new List<ChatMessageSourceDto>();
            }
        }

        private static string? SerializeSources(List<ChatMessageSourceDto>? sources)
        {
            if (sources == null || sources.Count == 0)
                return null;

            try
            {
                return JsonSerializer.Serialize(sources);
            }
            catch
            {
                return null;
            }
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
                
                if (IsSummaryIntent(dto.Message))
                {
                    _logger.LogInformation("Summary intent detected for session {SessionId}", dto.SessionId);
                    var summaryResponse = await HandleSummaryAsync(dto.SessionId, dto.Message);
                    _logger.LogInformation("Summary took {Ms}ms", sw.ElapsedMilliseconds);
                    entity.Response = summaryResponse;
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                
                _logger.LogInformation("Starting RAG pipeline for: {Message}", dto.Message);

                var vector = await _messageEmbeddings.EmbedMessageAsync(dto.Message);
                _logger.LogInformation("Message embedding took {Ms}ms", sw.ElapsedMilliseconds);
                sw.Restart();

                
                var contractIds = await _context.ChatSessionContracts
                    .AsNoTracking()
                    .Where(x => x.ChatSessionId == dto.SessionId)
                    .Select(x => x.ContractId)
                    .Distinct()
                    .ToListAsync();

                if (contractIds.Count == 0)
                {
                    entity.Response = "I don't see any document linked to this chat to search.";
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                
                var results = await _qdrantService.SearchSimilarAsync(
                    vector,
                    limit: 20,
                    scoreThreshold: 0.15f,
                    contractIds: contractIds
                );

                _logger.LogInformation("Vector search took {Ms}ms, found {Count} results",
                    sw.ElapsedMilliseconds, results.Count);
                sw.Restart();

                if (results.Count == 0)
                {
                    entity.Response = "I'm sorry, I couldn't find any relevant information to answer your question.";
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                
                var dedupedTexts = ContextBuilder.DeduplicateChunks(results.Select(r => r.ChunkText).ToList());
                var filteredResults = results
                    .Where(r => dedupedTexts.Contains(r.ChunkText))
                    .Take(12)
                    .ToList();

                _logger.LogInformation("Deduplication: {Original} → {Filtered} chunks",
                    results.Count, filteredResults.Count);


                // Load session (for stable ordering)
                var session = await _context.ChatSessions
                    .AsNoTracking()
                    .Include(s => s.SessionContracts)
                    .FirstOrDefaultAsync(s => s.Id == dto.SessionId);

                // Determine stable ordering of Contract IDs
                var orderedContractIds = session?.SessionContracts
                    .Select(sc => sc.ContractId)
                    .Distinct()
                    .ToList() ?? contractIds;

                // Fetch contract names for nicer labels
                var contractNames = await _context.Contracts
                    .AsNoTracking()
                    .Where(c => orderedContractIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.FileName);

                // Group chunks by contract (in stable order)
                var docGroups = filteredResults
                    .GroupBy(r => r.ContractId)
                    .OrderBy(g => orderedContractIds.IndexOf(g.Key))
                    .ToList();

                // BUILD CONTEXT
                var contextSb = new StringBuilder(MAX_CONTEXT_CHARS + 512);
                int docIndex = 1;

                foreach (var g in docGroups)
                {
                    var displayName = contractNames.TryGetValue(g.Key, out var fn)
                        ? CleanFileName(fn)
                        : $"Document {docIndex}";

                    foreach (var r in g.OrderByDescending(x => x.Score))
                    {
                        var text = (r.ChunkText ?? string.Empty).Trim();
                        if (text.Length < MIN_CHUNK_LEN) continue;

                        contextSb.AppendLine($"Document {docIndex}: {text}");
                    }

                    docIndex++;
                }


                var contextText = contextSb.ToString().Trim();
                _logger.LogInformation("Structured context built: {Length} chars; documents: {Docs}",
                    contextText.Length, docGroups.Count);

                // DYNAMIC comparison mode
                bool isComparison = docGroups.Count > 1;

                // DYNAMIC SYSTEM PROMPT
                string systemPrompt;

                if (!isComparison)
                {
                    // SINGLE DOCUMENT
                    systemPrompt = @"
You are a professional contract analysis assistant.
Use ONLY the provided text.
Do NOT invent facts.
If the answer cannot be found, reply exactly: 'Not found in contract.'";
                }
                else
                {
                    // MULTI-DOCUMENT COMPARISON
                    systemPrompt = @"
You are a professional contract analyst comparing documents.
Use ONLY the provided text.

CRITICAL RULES:
1. Only refer to documents as 'Document 1', 'Document 2', etc.
2. NEVER mix up content between documents.
3. NEVER invent missing sections.

If the answer cannot be found in the provided text,
'If unsure, try your best using the text provided.'";
                }

                // DYNAMIC USER PROMPT
                string evidenceInstruction = docGroups.Count > 1
                    ? "When giving evidence, prefix it with the document number (e.g., 'Document 1:')."
                    : "When giving evidence, refer to text as 'Document 1:'.";

                var userPrompt = $@"
Context:
{contextText}

User question:
{dto.Message}

Answer using ONLY the context above. {evidenceInstruction}
";

                _logger.LogInformation("Calling LLM generation");
                var rawresponse = await _generationClient.GenerateAsync(userPrompt, systemPrompt);
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

                var sources = results.Select(r => new ChatMessageSourceDto
                {
                    ContractId = r.ContractId,
                    ChunkText = r.ChunkText,
                    ChunkIndex = r.ChunkIndex,
                    PageNumber = r.PageNumber,
                    SimilarityScore = (double)r.Score
                }).ToList();

                entity.SourcesJson = SerializeSources(sources);

                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully generated response for message {MessageId} with {Count} sources",
                    entity.Id, sources.Count);

                var responseDto = MapToDto(entity);
                responseDto.Sources = sources;

                _logger.LogInformation("Returning {Count} sources with response", responseDto.Sources.Count);
                return responseDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Create()");
                entity.Response = "An unexpected error occurred while processing your message.";
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
            Sources = DeserializeSources(entity.SourcesJson)
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