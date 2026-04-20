using System.Text.Json;
using MySqlConnector;
using TuneFinder.Api.Services.Interfaces;

namespace TuneFinder.Api.Services.Rag;

public class RagService : IRagService
{
    private readonly string _connectionString;
    private readonly ILLMService _llmService;

    public RagService(string connectionString, ILLMService llmService)
    {
        _connectionString = connectionString;
        _llmService = llmService;
    }

    public bool ShouldUseRag(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var ragHintWords = new[]
        {
            "artist", "genre", "album", "style", "history", "similar", "playlist", "recommend", "music"
        };

        var lower = userMessage.ToLowerInvariant();
        return ragHintWords.Any(lower.Contains);
    }

    public async Task<List<string>> RetrieveRelevantChunksAsync(string userMessage, int topN = 4)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return [];
        }

        try
        {
            var queryEmbedding = await _llmService.CreateEmbeddingAsync(userMessage);
            if (queryEmbedding.Count > 0)
            {
                var semanticResults = await RetrieveByEmbeddingSimilarityAsync(queryEmbedding, topN);
                if (semanticResults.Count > 0)
                {
                    return semanticResults;
                }
            }
        }
        catch
        {
            // If embedding endpoint/rate-limits fail, fallback to keyword retrieval.
        }

        return await RetrieveByKeywordFallbackAsync(userMessage, topN);
    }

    private async Task<List<string>> RetrieveByEmbeddingSimilarityAsync(List<float> queryEmbedding, int topN)
    {
        const string sql = @"
SELECT chunk_text, embedding
FROM document_chunks
WHERE embedding IS NOT NULL AND TRIM(embedding) <> '';";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var scored = new List<(string ChunkText, double Score)>();
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var chunkText = reader.GetString("chunk_text");
            var embeddingJson = reader.GetString("embedding");
            var chunkEmbedding = ParseEmbedding(embeddingJson);
            if (chunkEmbedding.Count == 0)
            {
                continue;
            }

            var score = CosineSimilarity(queryEmbedding, chunkEmbedding);
            scored.Add((chunkText, score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .Select(x => x.ChunkText)
            .ToList();
    }

    private async Task<List<string>> RetrieveByKeywordFallbackAsync(string userMessage, int topN)
    {
        var keywords = userMessage
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 2)
            .Distinct()
            .Take(8)
            .ToList();

        if (keywords.Count == 0)
        {
            return [];
        }

        var whereParts = new List<string>();
        var parameters = new List<MySqlParameter>();

        for (var i = 0; i < keywords.Count; i++)
        {
            var parameterName = $"@k{i}";
            whereParts.Add($"LOWER(dc.chunk_text) LIKE {parameterName}");
            parameters.Add(new MySqlParameter(parameterName, $"%{keywords[i]}%"));
        }

        var whereClause = string.Join(" OR ", whereParts);
        var sql = $@"
SELECT dc.chunk_text
FROM document_chunks dc
WHERE {whereClause}
ORDER BY LENGTH(dc.chunk_text) ASC
LIMIT @limit;";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddRange(parameters.ToArray());
        command.Parameters.AddWithValue("@limit", topN);

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString("chunk_text"));
        }

        return results;
    }

    private static List<float> ParseEmbedding(string embeddingJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<float>>(embeddingJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static double CosineSimilarity(List<float> left, List<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return -1;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var i = 0; i < length; i++)
        {
            var l = left[i];
            var r = right[i];
            dot += l * r;
            leftNorm += l * l;
            rightNorm += r * r;
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return -1;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}
