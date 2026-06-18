using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeTaskManagement.Services.Ai
{
    /// <summary>
    /// Generates mock 768-dimension embeddings locally and deterministically.
    /// Used for offline testing and development when no external APIs or Ollama instances are available.
    /// </summary>
    public class LocalMockEmbeddingService : IGeminiEmbeddingService
    {
        /// <inheritdoc/>
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var seed = text.GetHashCode();
            var random = new Random(seed);
            var vector = new float[768];
            for (int i = 0; i < 768; i++)
            {
                vector[i] = (float)random.NextDouble() * 2.0f - 1.0f;
            }
            return Task.FromResult(vector);
        }

        /// <inheritdoc/>
        public Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken ct = default)
        {
            if (texts == null || texts.Length == 0)
            {
                return Task.FromResult(Array.Empty<float[]>());
            }

            var results = new float[texts.Length][];
            for (int j = 0; j < texts.Length; j++)
            {
                var seed = texts[j].GetHashCode();
                var random = new Random(seed);
                var vector = new float[768];
                for (int i = 0; i < 768; i++)
                {
                    vector[i] = (float)random.NextDouble() * 2.0f - 1.0f;
                }
                results[j] = vector;
            }
            return Task.FromResult(results);
        }
    }
}
