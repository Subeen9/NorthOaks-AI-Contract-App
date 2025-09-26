using System.Threading;
using System.Threading.Tasks;

namespace CMPS4110_NorthOaksProj.Data.Services.Embeddings
{
    public class MessageEmbeddingService
    {
        private readonly IEmbeddingClient _embeddingClient;

        public MessageEmbeddingService(IEmbeddingClient embeddingClient)
        {
            _embeddingClient = embeddingClient;
        }

        
        /// Generate an embedding vector for a single message.
        
        public async Task<float[]> EmbedMessageAsync(string message, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message cannot be empty", nameof(message));

            return await _embeddingClient.EmbedAsync(message, ct);
        }

       
        /// Generate embeddings for a batch of messages.
       
        public async Task<float[][]> EmbedMessagesBatchAsync(IEnumerable<string> messages, CancellationToken ct = default)
        {
            var arr = messages?.ToArray() ?? throw new ArgumentNullException(nameof(messages));
            if (arr.Length == 0)
                throw new ArgumentException("Messages batch cannot be empty", nameof(messages));

            return await _embeddingClient.EmbedBatchAsync(arr, ct);
        }
    }
}
