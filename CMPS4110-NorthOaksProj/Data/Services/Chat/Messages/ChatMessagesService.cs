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
        private const int MAX_CONTEXT_CHARS = 80000;
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

        private static bool IsComparisonIntent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var comparisonPatterns = new[]
            {
        @"\b(difference|differ|compare|comparison|contrast|versus|vs\.?)\b",
        @"\bbetween\s+(these|the|both|two|multiple)\s+(document|contract|file)",
        @"\bhow\s+do\s+(these|they|the\s+\w+)\s+(differ|compare)",
        @"\bwhat\s+(distinguishes|sets\s+apart)",
        @"\bsimilar(ities)?\s+(and|or)\s+difference"
    };

            return comparisonPatterns.Any(pattern =>
                Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
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
        private async Task<string> HandleComparisonAsync(
    int sessionId,
    string userMessage,
    List<int> contractIds)
        {
            if (contractIds.Count > 4)
            {
                return $"You selected {contractIds.Count} documents. Please select up to 4 documents for comparison.";
            }

            _logger.LogInformation("Starting comparison for {Count} documents", contractIds.Count);

            var documentChunks = new Dictionary<int, List<(int, string)>>();

            foreach (var contractId in contractIds)
            {
                var totalChunks = await _context.ContractEmbeddings
                    .Where(e => e.ContractId == contractId)
                    .CountAsync();

                if (totalChunks == 0)
                {
                    documentChunks[contractId] = new List<(int, string)>();
                    continue;
                }

                // sample evenly across document  
                int samplesNeeded = Math.Min(12, totalChunks);
                int interval = Math.Max(1, totalChunks / samplesNeeded);

                var chunkIndices = Enumerable.Range(0, samplesNeeded)
                    .Select(i => i * interval)
                    .Where(x => x < totalChunks)
                    .ToList();

                var chunks = await _context.ContractEmbeddings
                    .AsNoTracking()
                    .Where(e => e.ContractId == contractId && chunkIndices.Contains(e.ChunkIndex))
                    .OrderBy(e => e.ChunkIndex)
                    .Select(e => new { e.ChunkIndex, e.ChunkText })
                    .ToListAsync();

                documentChunks[contractId] = chunks
                    .Select(c => (c.ChunkIndex, c.ChunkText ?? string.Empty))
                    .ToList();

                _logger.LogInformation(
                    "Sampled {Count} chunks from {Total} total for Doc {DocId}",
                    chunks.Count, totalChunks, contractId
                );
            }

            // build context
            var contextSb = new StringBuilder(MAX_CONTEXT_CHARS);

            var contractNames = await _context.Contracts
                .AsNoTracking()
                .Where(c => contractIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FileName);

            int docNum = 1;
            int charCount = 0;

            foreach (var contractId in contractIds)
            {
                var displayName = contractNames.TryGetValue(contractId, out var fn)
                    ? Path.GetFileNameWithoutExtension(fn)
                    : $"Document {docNum}";

                contextSb.AppendLine($"\n=== Document {docNum}: {displayName} ===");

                if (documentChunks.TryGetValue(contractId, out var chunks))
                {
                    foreach (var (_, text) in chunks)
                    {
                        var clean = text.Trim();
                        if (clean.Length < MIN_CHUNK_LEN) continue;

                        if (charCount + clean.Length > MAX_CONTEXT_CHARS)
                        {
                            _logger.LogWarning("Stopped due to context size limit");
                            break;
                        }

                        contextSb.AppendLine(clean);
                        contextSb.AppendLine();
                        charCount += clean.Length;
                    }
                }

                docNum++;
            }

            if (charCount < 150)
                return "I couldn't extract enough useful text to compare the documents.";

            var systemPrompt = @"
You are a professional document analyst. 
Compare documents strictly based on the provided text.

RULES:
1. Refer to each document by 'Document 1', 'Document 2', etc.
2. Identify differences in content, purpose, structure, tone.
3. Identify similarities if they exist.
4. DO NOT make up content.
5. If documents are different genres (contract vs letter), explain clearly.
6. Keep analysis concise but thorough.";

            var userPrompt = $@"
User request: {userMessage}

Below are excerpts from {contractIds.Count} documents. 
Compare them and list differences and similarities.

{contextSb}
";

            try
            {
                var result = await _generationClient.GenerateAsync(
                    prompt: userPrompt,
                    systemPrompt: systemPrompt
                );

                return CleanGeneratedResponse(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HandleComparisonAsync");
                return "I encountered an error while comparing the documents.";
            }
        }


        public async Task<ChatMessageDto?> Create(CreateChatMessageDto dto)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Validate session
            var exists = await _context.ChatSessions.AsNoTracking()
                .AnyAsync(s => s.Id == dto.SessionId);
            if (!exists) return null;

            // 2. Save user message
            var entity = new ChatMessage
            {
                SessionId = dto.SessionId,
                Message = dto.Message
            };

            _context.ChatMessages.Add(entity);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved message in {Ms}ms", sw.ElapsedMilliseconds);

            try
            {
                // 3. Check summary intent first
                if (IsSummaryIntent(dto.Message))
                {
                    sw.Restart();
                    _logger.LogInformation("Summary intent detected");
                    var summary = await HandleSummaryAsync(dto.SessionId, dto.Message);
                    _logger.LogInformation("Summary completed in {Ms}ms", sw.ElapsedMilliseconds);

                    entity.Response = summary;
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                // 4. Load documents linked to this chat
                sw.Restart();
                var contractIds = await _context.ChatSessionContracts
                    .AsNoTracking()
                    .Where(x => x.ChatSessionId == dto.SessionId)
                    .Select(x => x.ContractId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("Found {Count} documents in {Ms}ms",
                    contractIds.Count, sw.ElapsedMilliseconds);

                if (contractIds.Count == 0)
                {
                    entity.Response = "I don't see any document linked to this chat to search.";
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                // 5. Check comparison intent (only for multi-document)
                if (IsComparisonIntent(dto.Message) && contractIds.Count > 1)
                {
                    sw.Restart();
                    _logger.LogInformation("Comparison intent detected for {Count} documents",
                        contractIds.Count);

                    var comparison = await HandleComparisonAsync(
                        dto.SessionId,
                        dto.Message,
                        contractIds
                    );

                    _logger.LogInformation("Comparison completed in {Ms}ms", sw.ElapsedMilliseconds);
                    entity.Response = comparison;
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                // 6. RAG PIPELINE with balanced multi-document support
                sw.Restart();
                _logger.LogInformation("Starting RAG pipeline for {Count} document(s)",
                    contractIds.Count);

                var vector = await _messageEmbeddings.EmbedMessageAsync(dto.Message);
                _logger.LogInformation("Embedding created in {Ms}ms", sw.ElapsedMilliseconds);
                sw.Restart();
                List<VectorSearchResult> results;

                if (contractIds.Count == 1)
                {
                    // Single document: standard search
                    results = await _qdrantService.SearchSimilarAsync(
                        vector,
                        limit: 20,
                        scoreThreshold: 0.15f,
                        contractIds: contractIds
                    );
                    _logger.LogInformation("Single-doc search: {Count} results in {Ms}ms",
                        results.Count, sw.ElapsedMilliseconds);
                }
                else
                {
                    // Multiple documents: search each separately for fairness
                    results = new List<VectorSearchResult>();
                    int chunksPerDoc = Math.Max(8, 20 / contractIds.Count);

                    _logger.LogInformation("Multi-doc balanced search: {Chunks} per document",
                        chunksPerDoc);

                    foreach (var contractId in contractIds)
                    {
                        var docResults = await _qdrantService.SearchSimilarAsync(
                            vector,
                            limit: chunksPerDoc,
                            scoreThreshold: 0.12f,  // Lower threshold for better coverage
                            contractIds: new List<int> { contractId }
                        );
                        results.AddRange(docResults);
                    }

                    _logger.LogInformation("Multi-doc search: {Total} results from {Docs} documents in {Ms}ms",
                        results.Count, contractIds.Count, sw.ElapsedMilliseconds);
                }

                if (results.Count == 0)
                {
                    entity.Response = "I couldn't find any relevant information to answer your question.";
                    await _context.SaveChangesAsync();
                    return MapToDto(entity);
                }

                // Deduplicate
                var deduped = ContextBuilder.DeduplicateChunks(
                    results.Select(r => r.ChunkText).ToList()
                );

                var filtered = results
                    .Where(r => deduped.Contains(r.ChunkText))
                    .OrderByDescending(r => r.Score)
                    .Take(12)
                    .ToList();

                _logger.LogInformation("After dedup: {Count} chunks", filtered.Count);

                //  Build context with document labels for multi-doc scenarios
                var contractNames = await _context.Contracts
                    .AsNoTracking()
                    .Where(c => contractIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id, c => c.FileName);

                var docGroups = filtered
                    .GroupBy(r => r.ContractId)
                    .OrderBy(g => contractIds.IndexOf(g.Key))
                    .ToList();

                var contextSb = new StringBuilder(MAX_CONTEXT_CHARS);
                int docIndex = 1;

                foreach (var group in docGroups)
                {
                    if (contractIds.Count > 1)
                    {
                        // Multi-doc: add document labels
                        var displayName = contractNames.TryGetValue(group.Key, out var fn)
                            ? CleanFileName(fn)
                            : $"Document {docIndex}";

                        contextSb.AppendLine($"\n=== Document {docIndex}: {displayName} ===");
                    }

                    foreach (var r in group.OrderByDescending(r => r.Score))
                    {
                        var text = (r.ChunkText ?? "").Trim();
                        if (text.Length < MIN_CHUNK_LEN) continue;

                        if (contractIds.Count > 1)
                        {
                            contextSb.AppendLine($"Document {docIndex}: {text}");
                        }
                        else
                        {
                            contextSb.AppendLine(text);
                        }
                    }

                    docIndex++;
                }

                var contextText = contextSb.ToString().Trim();
                _logger.LogInformation("Context built: {Length} chars, {Docs} document(s)",
                    contextText.Length, docGroups.Count);

                string systemPrompt;
                string evidenceInstruction;

                if (contractIds.Count == 1)
                {
                    systemPrompt = @"
You are a professional document analyst.

RULES:
- Use ONLY the provided text
- Write naturally - no metadata, chunk numbers, or technical labels
- Be clear, concise, and professional
- If information is not present, say so directly

When answering, provide specific details and quote relevant terms where helpful.";

                    evidenceInstruction = "Answer based on the text above. Be specific.";
                }
                else
                {
                    systemPrompt = @"
You are a professional contract analyst working with multiple documents.
Use ONLY the provided text.

CRITICAL RULES:
1. Refer to documents as 'Document 1', 'Document 2', etc.
2. NEVER mix up content between documents.
3. NEVER invent content not present in the text.
4. If information is in one document but not another, state this clearly.";

                    evidenceInstruction = "When citing information, specify which document it came from (e.g., 'Document 1:', 'Document 2:').";
                }

                string userPrompt = $@"
Context:
{contextText}

User question:
{dto.Message}

{evidenceInstruction}";

                // Generate response
                sw.Restart();
                var raw = await _generationClient.GenerateAsync(userPrompt, systemPrompt);
                var cleaned = CleanGeneratedResponse(raw);

                _logger.LogInformation("LLM generation completed in {Ms}ms, length: {Len}",
                    sw.ElapsedMilliseconds, cleaned?.Length ?? 0);

                entity.Response = string.IsNullOrWhiteSpace(cleaned)
                    ? "The model returned an empty response. Please try rephrasing your question."
                    : cleaned;

                // Save sources
                entity.SourcesJson = SerializeSources(
                    filtered.Select(r => new ChatMessageSourceDto
                    {
                        ContractId = r.ContractId,
                        ChunkText = r.ChunkText,
                        ChunkIndex = r.ChunkIndex,
                        PageNumber = r.PageNumber,
                        SimilarityScore = r.Score
                    }).ToList()
                );

                await _context.SaveChangesAsync();

                var responseDto = MapToDto(entity);
                responseDto.Sources = DeserializeSources(entity.SourcesJson);

                return responseDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message for session {SessionId}", dto.SessionId);
                entity.Response = "An unexpected error occurred. Please try again or rephrase your question.";
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