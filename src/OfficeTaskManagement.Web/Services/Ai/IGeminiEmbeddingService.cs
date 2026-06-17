namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Service for generating text embeddings using the Gemini gemini-embedding-001 model.
    /// Used by CodebaseIndexingService (Phase 3) to embed code chunks for vector similarity search.
    /// </summary>
    public interface IGeminiEmbeddingService
    {
        /// <summary>
        /// Generates a 768-dimensional float vector embedding for a single text string.
        /// </summary>
        Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

        /// <summary>
        /// Generates embeddings for a batch of texts (up to 100 per call).
        /// More efficient than calling EmbedAsync in a loop during bulk indexing.
        /// </summary>
        Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default);
    }
}
