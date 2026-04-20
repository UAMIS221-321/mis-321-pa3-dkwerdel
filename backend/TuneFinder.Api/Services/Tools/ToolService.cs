using System.Text;
using System.Text.Json;
using MySqlConnector;
using TuneFinder.Api.Models.Llm;
using TuneFinder.Api.Services.Interfaces;

namespace TuneFinder.Api.Services.Tools;

public class ToolService : IToolService
{
    private readonly string _connectionString;

    public ToolService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Name = "recommendSongsByMood",
                Description = "Return songs and artists that fit a mood.",
                Parameters = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["mood"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = "Mood like chill, energetic, happy, romantic, focused"
                        }
                    },
                    ["required"] = new[] { "mood" }
                }
            },
            new ToolDefinition
            {
                Name = "findSimilarArtists",
                Description = "Return similar artists from local data.",
                Parameters = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["artistName"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = "Artist name"
                        }
                    },
                    ["required"] = new[] { "artistName" }
                }
            },
            new ToolDefinition
            {
                Name = "createPlaylist",
                Description = "Return a curated playlist of 8-10 songs by theme.",
                Parameters = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["theme"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = "Theme like workout, study, late night, road trip"
                        }
                    },
                    ["required"] = new[] { "theme" }
                }
            },
            new ToolDefinition
            {
                Name = "searchArtistInfo",
                Description = "Return artist summary, genres, style, notable albums, and similar artists.",
                Parameters = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["artistName"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = "Artist name"
                        }
                    },
                    ["required"] = new[] { "artistName" }
                }
            }
        ];
    }

    public async Task<string> ExecuteToolAsync(string toolName, string argumentsJson)
    {
        return toolName switch
        {
            "recommendSongsByMood" => await RecommendSongsByMoodAsync(GetArgument(argumentsJson, "mood")),
            "findSimilarArtists" => await FindSimilarArtistsAsync(GetArgument(argumentsJson, "artistName")),
            "createPlaylist" => await CreatePlaylistAsync(GetArgument(argumentsJson, "theme")),
            "searchArtistInfo" => await SearchArtistInfoAsync(GetArgument(argumentsJson, "artistName")),
            _ => $"Unknown tool: {toolName}"
        };
    }

    private static string GetArgument(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> RecommendSongsByMoodAsync(string mood)
    {
        if (string.IsNullOrWhiteSpace(mood))
        {
            return "Missing mood argument.";
        }

        const string sql = @"
SELECT title, artist, genre, energy_level
FROM songs
WHERE LOWER(mood) LIKE @mood
ORDER BY energy_level DESC
LIMIT 8;";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@mood", $"%{mood.ToLowerInvariant()}%");

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add($"- {reader.GetString("title")} - {reader.GetString("artist")} ({reader.GetString("genre")}, energy {reader.GetInt32("energy_level")})");
        }

        if (rows.Count == 0)
        {
            return $"No songs found for mood '{mood}'. Try moods like chill, energetic, focused, happy, or romantic.";
        }

        return $"Songs for mood '{mood}':\n{string.Join('\n', rows)}";
    }

    private async Task<string> FindSimilarArtistsAsync(string artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return "Missing artistName argument.";
        }

        const string sql = @"
SELECT name, genres, similar_artists
FROM artists
WHERE LOWER(name) LIKE @artist
LIMIT 1;";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@artist", $"%{artistName.ToLowerInvariant()}%");

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return $"No artist match found for '{artistName}'.";
        }

        var name = reader.GetString("name");
        var genres = reader.GetString("genres");
        var similar = reader.GetString("similar_artists");

        return $"Similar artists for {name}:\n- Genres: {genres}\n- Similar: {similar}";
    }

    private async Task<string> CreatePlaylistAsync(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return "Missing theme argument.";
        }

        var themeToMood = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workout"] = "energetic",
            ["study"] = "focused",
            ["late night"] = "chill",
            ["road trip"] = "uplifting",
            ["heartbreak"] = "sad",
            ["party"] = "dance",
            ["morning"] = "happy",
            ["rainy day"] = "melancholic"
        };

        var mood = themeToMood.TryGetValue(theme.Trim(), out var mappedMood)
            ? mappedMood
            : "chill";

        const string sql = @"
SELECT title, artist, genre
FROM songs
WHERE LOWER(mood) LIKE @mood
ORDER BY RAND()
LIMIT 10;";

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@mood", $"%{mood.ToLowerInvariant()}%");

        await using var reader = await command.ExecuteReaderAsync();
        var playlistRows = new List<string>();
        while (await reader.ReadAsync())
        {
            playlistRows.Add($"- {reader.GetString("title")} - {reader.GetString("artist")} ({reader.GetString("genre")})");
        }

        if (playlistRows.Count < 8)
        {
            reader.Close();
            command.CommandText = "SELECT title, artist, genre FROM songs ORDER BY RAND() LIMIT 10;";
            command.Parameters.Clear();
            await using var fallbackReader = await command.ExecuteReaderAsync();
            playlistRows.Clear();
            while (await fallbackReader.ReadAsync())
            {
                playlistRows.Add($"- {fallbackReader.GetString("title")} - {fallbackReader.GetString("artist")} ({fallbackReader.GetString("genre")})");
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Playlist for theme '{theme}' (mood focus: {mood}):");
        foreach (var row in playlistRows)
        {
            builder.AppendLine(row);
        }

        return builder.ToString().Trim();
    }

    private async Task<string> SearchArtistInfoAsync(string artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return "Missing artistName argument.";
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        const string artistSql = @"
SELECT name, genres, description, similar_artists
FROM artists
WHERE LOWER(name) LIKE @artist
LIMIT 1;";

        await using var artistCommand = new MySqlCommand(artistSql, connection);
        artistCommand.Parameters.AddWithValue("@artist", $"%{artistName.ToLowerInvariant()}%");

        await using var artistReader = await artistCommand.ExecuteReaderAsync();
        if (!await artistReader.ReadAsync())
        {
            return $"No artist info found for '{artistName}'.";
        }

        var canonicalName = artistReader.GetString("name");
        var genres = artistReader.GetString("genres");
        var description = artistReader.GetString("description");
        var similarArtists = artistReader.GetString("similar_artists");

        await artistReader.CloseAsync();

        const string albumsSql = @"
SELECT title, summary
FROM albums
WHERE LOWER(artist) = @artist
LIMIT 4;";

        await using var albumsCommand = new MySqlCommand(albumsSql, connection);
        albumsCommand.Parameters.AddWithValue("@artist", canonicalName.ToLowerInvariant());

        await using var albumsReader = await albumsCommand.ExecuteReaderAsync();
        var albums = new List<string>();
        while (await albumsReader.ReadAsync())
        {
            albums.Add($"- {albumsReader.GetString("title")}: {albumsReader.GetString("summary")}");
        }

        var result = new StringBuilder();
        result.AppendLine($"Artist: {canonicalName}");
        result.AppendLine($"Genres/Style: {genres}");
        result.AppendLine($"Summary: {description}");
        result.AppendLine($"Similar Artists: {similarArtists}");
        if (albums.Count > 0)
        {
            result.AppendLine("Notable Albums:");
            albums.ForEach(x => result.AppendLine(x));
        }

        return result.ToString().Trim();
    }
}
