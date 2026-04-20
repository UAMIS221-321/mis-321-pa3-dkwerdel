using System.Text.Json;
using MySqlConnector;
using TuneFinder.Api.Services.Interfaces;

namespace TuneFinder.Api.Services;

public class SeedDataService : ISeedDataService
{
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _environment;
    private readonly ILLMService _llmService;

    public SeedDataService(string connectionString, IWebHostEnvironment environment, ILLMService llmService)
    {
        _connectionString = connectionString;
        _environment = environment;
        _llmService = llmService;
    }

    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await CreateTablesAsync(connection, cancellationToken);
        await SeedArtistsAsync(connection, cancellationToken);
        await SeedSongsAsync(connection, cancellationToken);
        await SeedAlbumsAsync(connection, cancellationToken);
        await SeedDocumentsAndChunksAsync(connection, cancellationToken);
    }

    private static async Task CreateTablesAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS artists (
    id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    genres TEXT NOT NULL,
    description TEXT NOT NULL,
    similar_artists TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS songs (
    id INT AUTO_INCREMENT PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    artist VARCHAR(255) NOT NULL,
    genre VARCHAR(100) NOT NULL,
    mood VARCHAR(100) NOT NULL,
    energy_level INT NOT NULL
);

CREATE TABLE IF NOT EXISTS albums (
    id INT AUTO_INCREMENT PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    artist VARCHAR(255) NOT NULL,
    summary TEXT NOT NULL,
    genre VARCHAR(100) NOT NULL
);

CREATE TABLE IF NOT EXISTS documents (
    id INT AUTO_INCREMENT PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    content TEXT NOT NULL,
    category VARCHAR(100) NOT NULL
);

CREATE TABLE IF NOT EXISTS document_chunks (
    id INT AUTO_INCREMENT PRIMARY KEY,
    document_id INT NOT NULL,
    chunk_text TEXT NOT NULL,
    embedding LONGTEXT NULL,
    FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS chat_messages (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    session_id VARCHAR(100) NOT NULL,
    role VARCHAR(20) NOT NULL,
    message_text TEXT NOT NULL,
    created_at DATETIME NOT NULL
);";

        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureIndexAsync(connection, "chat_messages", "idx_chat_messages_session_created", "session_id, created_at", cancellationToken);
        await EnsureIndexAsync(connection, "songs", "idx_songs_mood", "mood", cancellationToken);
        await EnsureIndexAsync(connection, "artists", "idx_artists_name", "name", cancellationToken);
        await EnsureIndexAsync(connection, "documents", "idx_documents_category", "category", cancellationToken);
    }

    private static async Task EnsureIndexAsync(
        MySqlConnection connection,
        string tableName,
        string indexName,
        string columnListSql,
        CancellationToken cancellationToken
    )
    {
        const string indexExistsSql = @"
SELECT COUNT(*)
FROM information_schema.statistics
WHERE table_schema = DATABASE()
  AND table_name = @tableName
  AND index_name = @indexName;";

        await using var existsCommand = new MySqlCommand(indexExistsSql, connection);
        existsCommand.Parameters.AddWithValue("@tableName", tableName);
        existsCommand.Parameters.AddWithValue("@indexName", indexName);

        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (exists)
        {
            return;
        }

        var createIndexSql = $"CREATE INDEX {indexName} ON {tableName} ({columnListSql});";
        await using var createCommand = new MySqlCommand(createIndexSql, connection);
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedArtistsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await DeleteInvalidRowsAsync(connection, "artists", "TRIM(name) = ''", cancellationToken);

        var baseArtists = await LoadJsonAsync<List<ArtistSeed>>("artists.json", cancellationToken) ?? [];
        var decadeArtists = await LoadJsonAsync<List<ArtistSeed>>("artists_decades.json", cancellationToken) ?? [];
        var artists = baseArtists.Concat(decadeArtists).ToList();

        const string insertSql = @"
INSERT INTO artists (name, genres, description, similar_artists)
VALUES (@name, @genres, @description, @similarArtists);";

        foreach (var artist in artists)
        {
            if (await ArtistExistsAsync(connection, artist.Name, cancellationToken))
            {
                continue;
            }

            await using var command = new MySqlCommand(insertSql, connection);
            command.Parameters.AddWithValue("@name", artist.Name);
            command.Parameters.AddWithValue("@genres", artist.Genres);
            command.Parameters.AddWithValue("@description", artist.Description);
            command.Parameters.AddWithValue("@similarArtists", artist.SimilarArtists);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> ArtistExistsAsync(
        MySqlConnection connection,
        string artistName,
        CancellationToken cancellationToken
    )
    {
        const string sql = @"
SELECT EXISTS(
    SELECT 1
    FROM artists
    WHERE LOWER(name) = LOWER(@artistName)
    LIMIT 1
);";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@artistName", artistName);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return result == 1;
    }

    private async Task SeedSongsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await DeleteInvalidRowsAsync(connection, "songs", "TRIM(title) = '' OR TRIM(artist) = ''", cancellationToken);

        if (await HasRowsAsync(connection, "songs", cancellationToken))
        {
            return;
        }

        var songs = await LoadJsonAsync<List<SongSeed>>("songs.json", cancellationToken) ?? [];
        const string insertSql = @"
INSERT INTO songs (title, artist, genre, mood, energy_level)
VALUES (@title, @artist, @genre, @mood, @energyLevel);";

        foreach (var song in songs)
        {
            await using var command = new MySqlCommand(insertSql, connection);
            command.Parameters.AddWithValue("@title", song.Title);
            command.Parameters.AddWithValue("@artist", song.Artist);
            command.Parameters.AddWithValue("@genre", song.Genre);
            command.Parameters.AddWithValue("@mood", song.Mood);
            command.Parameters.AddWithValue("@energyLevel", song.EnergyLevel);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SeedAlbumsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await DeleteInvalidRowsAsync(connection, "albums", "TRIM(title) = '' OR TRIM(artist) = ''", cancellationToken);

        if (await HasRowsAsync(connection, "albums", cancellationToken))
        {
            return;
        }

        var albums = await LoadJsonAsync<List<AlbumSeed>>("albums.json", cancellationToken) ?? [];
        const string insertSql = @"
INSERT INTO albums (title, artist, summary, genre)
VALUES (@title, @artist, @summary, @genre);";

        foreach (var album in albums)
        {
            await using var command = new MySqlCommand(insertSql, connection);
            command.Parameters.AddWithValue("@title", album.Title);
            command.Parameters.AddWithValue("@artist", album.Artist);
            command.Parameters.AddWithValue("@summary", album.Summary);
            command.Parameters.AddWithValue("@genre", album.Genre);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SeedDocumentsAndChunksAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await DeleteInvalidRowsAsync(connection, "documents", "TRIM(title) = '' OR TRIM(content) = ''", cancellationToken);
        await DeleteInvalidRowsAsync(connection, "document_chunks", "TRIM(chunk_text) = ''", cancellationToken);

        var curatedDocuments = await LoadJsonAsync<List<DocumentSeed>>("documents.json", cancellationToken) ?? [];
        var generatedDocuments = await BuildGeneratedKnowledgeDocumentsAsync(connection, cancellationToken);
        var allDocuments = curatedDocuments
            .Concat(generatedDocuments)
            .GroupBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        foreach (var document in allDocuments)
        {
            var documentId = await GetOrCreateDocumentAsync(connection, document, cancellationToken);
            if (await DocumentHasChunksAsync(connection, documentId, cancellationToken))
            {
                continue;
            }

            await InsertChunksForDocumentAsync(connection, documentId, document.Content, cancellationToken);
        }

        await BackfillChunkEmbeddingsAsync(connection, cancellationToken);
    }

    private static async Task<int> GetOrCreateDocumentAsync(
        MySqlConnection connection,
        DocumentSeed document,
        CancellationToken cancellationToken
    )
    {
        const string findSql = @"
SELECT id
FROM documents
WHERE LOWER(title) = LOWER(@title)
LIMIT 1;";

        await using (var findCommand = new MySqlCommand(findSql, connection))
        {
            findCommand.Parameters.AddWithValue("@title", document.Title);
            var existingId = await findCommand.ExecuteScalarAsync(cancellationToken);
            if (existingId is not null && existingId != DBNull.Value)
            {
                return Convert.ToInt32(existingId);
            }
        }

        const string insertSql = @"
INSERT INTO documents (title, content, category)
VALUES (@title, @content, @category);
SELECT LAST_INSERT_ID();";

        await using var insertCommand = new MySqlCommand(insertSql, connection);
        insertCommand.Parameters.AddWithValue("@title", document.Title);
        insertCommand.Parameters.AddWithValue("@content", document.Content);
        insertCommand.Parameters.AddWithValue("@category", document.Category);
        return Convert.ToInt32(await insertCommand.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> DocumentHasChunksAsync(
        MySqlConnection connection,
        int documentId,
        CancellationToken cancellationToken
    )
    {
        const string sql = @"
SELECT EXISTS(
    SELECT 1
    FROM document_chunks
    WHERE document_id = @documentId
    LIMIT 1
);";

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@documentId", documentId);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return result == 1;
    }

    private static async Task InsertChunksForDocumentAsync(
        MySqlConnection connection,
        int documentId,
        string content,
        CancellationToken cancellationToken
    )
    {
        const string insertChunkSql = @"
INSERT INTO document_chunks (document_id, chunk_text, embedding)
VALUES (@documentId, @chunkText, NULL);";

        foreach (var chunk in ChunkText(content, 320))
        {
            await using var insertChunk = new MySqlCommand(insertChunkSql, connection);
            insertChunk.Parameters.AddWithValue("@documentId", documentId);
            insertChunk.Parameters.AddWithValue("@chunkText", chunk);
            await insertChunk.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<List<DocumentSeed>> BuildGeneratedKnowledgeDocumentsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        var docs = new List<DocumentSeed>();
        docs.AddRange(await BuildArtistDocumentsAsync(connection, cancellationToken));
        docs.AddRange(await BuildAlbumDocumentsAsync(connection, cancellationToken));
        docs.AddRange(await BuildSongDocumentsAsync(connection, cancellationToken));
        docs.AddRange(await BuildGenreDocumentsAsync(connection, cancellationToken));
        return docs;
    }

    private static async Task<List<DocumentSeed>> BuildArtistDocumentsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string artistSql = @"
SELECT name, genres, description, similar_artists
FROM artists;";

        var artists = new List<(string Name, string Genres, string Description, string SimilarArtists)>();
        await using (var command = new MySqlCommand(artistSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                artists.Add(
                    (
                        reader.GetString("name"),
                        reader.GetString("genres"),
                        reader.GetString("description"),
                        reader.GetString("similar_artists")
                    )
                );
            }
        }

        const string albumsByArtistSql = @"
SELECT artist, GROUP_CONCAT(title ORDER BY title SEPARATOR ', ') AS album_titles
FROM albums
GROUP BY artist;";

        var albumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = new MySqlCommand(albumsByArtistSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var artist = reader.GetString("artist");
                var albumTitles = reader.IsDBNull(reader.GetOrdinal("album_titles"))
                    ? "No notable albums listed."
                    : reader.GetString("album_titles");
                albumMap[artist] = albumTitles;
            }
        }

        return artists.Select(artist =>
        {
            var albums = albumMap.TryGetValue(artist.Name, out var albumTitles)
                ? albumTitles
                : "No notable albums listed.";

            return new DocumentSeed
            {
                Title = $"Artist Profile: {artist.Name}",
                Category = "artist_profile_generated",
                Content =
                    $"{artist.Name} is associated with {artist.Genres}. " +
                    $"{artist.Description} " +
                    $"Similar artists include {artist.SimilarArtists}. " +
                    $"Notable albums: {albums}. " +
                    $"Recommendation note: if a listener likes {artist.Name}, suggest one of the similar artists with overlapping genres."
            };
        }).ToList();
    }

    private static async Task<List<DocumentSeed>> BuildAlbumDocumentsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string albumSql = @"
SELECT title, artist, summary, genre
FROM albums;";

        var docs = new List<DocumentSeed>();
        await using var command = new MySqlCommand(albumSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var title = reader.GetString("title");
            var artist = reader.GetString("artist");
            var summary = reader.GetString("summary");
            var genre = reader.GetString("genre");

            docs.Add(
                new DocumentSeed
                {
                    Title = $"Album Snapshot: {title} - {artist}",
                    Category = "album_generated",
                    Content =
                        $"{title} by {artist} is categorized as {genre}. " +
                        $"Album summary: {summary} " +
                        $"Listener guidance: recommend this album to users asking for {genre} records or artists similar to {artist}."
                }
            );
        }

        return docs;
    }

    private static async Task<List<DocumentSeed>> BuildSongDocumentsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string songSql = @"
SELECT title, artist, genre, mood, energy_level
FROM songs;";

        var docs = new List<DocumentSeed>();
        await using var command = new MySqlCommand(songSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var title = reader.GetString("title");
            var artist = reader.GetString("artist");
            var genre = reader.GetString("genre");
            var mood = reader.GetString("mood");
            var energy = reader.GetInt32("energy_level");

            docs.Add(
                new DocumentSeed
                {
                    Title = $"Song Snapshot: {title} - {artist}",
                    Category = "song_generated",
                    Content =
                        $"{title} by {artist} fits the {genre} genre with a {mood} mood and energy level {energy} out of 10. " +
                        $"Use this song for playlists matching {mood} or users requesting {genre} recommendations with similar energy."
                }
            );
        }

        return docs;
    }

    private static async Task<List<DocumentSeed>> BuildGenreDocumentsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string genreSql = @"
SELECT genre, COUNT(*) AS song_count, GROUP_CONCAT(DISTINCT mood ORDER BY mood SEPARATOR ', ') AS moods
FROM songs
GROUP BY genre;";

        var docs = new List<DocumentSeed>();
        await using var command = new MySqlCommand(genreSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var genre = reader.GetString("genre");
            var songCount = reader.GetInt32("song_count");
            var moods = reader.IsDBNull(reader.GetOrdinal("moods"))
                ? "varied moods"
                : reader.GetString("moods");

            docs.Add(
                new DocumentSeed
                {
                    Title = $"Genre Guide Generated: {genre}",
                    Category = "genre_generated",
                    Content =
                        $"{genre} currently includes {songCount} seeded songs in the TuneFinder catalog. " +
                        $"Common moods in this genre are {moods}. " +
                        $"Recommendation note: when users ask for {genre}, prioritize songs whose mood and energy align with the requested vibe."
                }
            );
        }

        return docs;
    }

    private async Task BackfillChunkEmbeddingsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        const string selectSql = @"
SELECT id, chunk_text
FROM document_chunks
WHERE embedding IS NULL OR TRIM(embedding) = '';";

        var chunks = new List<(int Id, string ChunkText)>();
        await using (var selectCommand = new MySqlCommand(selectSql, connection))
        await using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                chunks.Add((reader.GetInt32("id"), reader.GetString("chunk_text")));
            }
        }

        if (chunks.Count == 0)
        {
            return;
        }

        const string updateSql = @"
UPDATE document_chunks
SET embedding = @embedding
WHERE id = @id;";

        foreach (var chunk in chunks)
        {
            try
            {
                var embedding = await _llmService.CreateEmbeddingAsync(chunk.ChunkText, cancellationToken);
                if (embedding.Count == 0)
                {
                    continue;
                }

                var serialized = JsonSerializer.Serialize(embedding);
                await using var updateCommand = new MySqlCommand(updateSql, connection);
                updateCommand.Parameters.AddWithValue("@embedding", serialized);
                updateCommand.Parameters.AddWithValue("@id", chunk.Id);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Do not fail app startup if embedding generation is unavailable.
            }
        }
    }

    private async Task<T?> LoadJsonAsync<T>(string fileName, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_environment.ContentRootPath, "SeedData", fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Seed file not found: {filePath}");
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<T>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );
    }

    private static async Task<bool> HasRowsAsync(
        MySqlConnection connection,
        string tableName,
        CancellationToken cancellationToken
    )
    {
        await using var command = new MySqlCommand($"SELECT EXISTS(SELECT 1 FROM {tableName} LIMIT 1);", connection);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return result == 1;
    }

    private static async Task DeleteInvalidRowsAsync(
        MySqlConnection connection,
        string tableName,
        string whereClause,
        CancellationToken cancellationToken
    )
    {
        var sql = $"DELETE FROM {tableName} WHERE {whereClause};";
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static List<string> ChunkText(string text, int chunkSize)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= chunkSize)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        var current = 0;
        while (current < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - current);
            var end = current + length;

            if (end < normalized.Length)
            {
                var nearestSentenceBreak = normalized.LastIndexOf('.', end - 1, length);
                if (nearestSentenceBreak > current + 30)
                {
                    end = nearestSentenceBreak + 1;
                }
            }

            chunks.Add(normalized[current..end].Trim());
            current = end;
        }

        return chunks.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }

    private class ArtistSeed
    {
        public string Name { get; set; } = string.Empty;
        public string Genres { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SimilarArtists { get; set; } = string.Empty;
    }

    private class SongSeed
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Mood { get; set; } = string.Empty;
        public int EnergyLevel { get; set; }
    }

    private class AlbumSeed
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
    }

    private class DocumentSeed
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }
}
