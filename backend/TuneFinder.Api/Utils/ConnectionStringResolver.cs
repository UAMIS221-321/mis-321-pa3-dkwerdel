using MySqlConnector;

namespace TuneFinder.Api.Utils;

public static class ConnectionStringResolver
{
    public static string ResolveMySqlConnectionString(IConfiguration configuration)
    {
        var jawsDbUrl = configuration["JAWSDB_URL"] ?? Environment.GetEnvironmentVariable("JAWSDB_URL");
        if (!string.IsNullOrWhiteSpace(jawsDbUrl))
        {
            return BuildConnectionStringFromUrl(jawsDbUrl);
        }

        var databaseUrl = configuration["DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return BuildConnectionStringFromUrl(databaseUrl);
        }

        var explicitConnectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return EnsureSslMode(explicitConnectionString);
        }

        throw new InvalidOperationException(
            "No database connection info found. Set ConnectionStrings__Default or JAWSDB_URL/DATABASE_URL."
        );
    }

    private static string BuildConnectionStringFromUrl(string url)
    {
        var uri = new Uri(url);
        var userInfoParts = uri.UserInfo.Split(':', 2);
        if (userInfoParts.Length != 2)
        {
            throw new InvalidOperationException("Invalid database URL format.");
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = (uint)(uri.Port > 0 ? uri.Port : 3306),
            UserID = Uri.UnescapeDataString(userInfoParts[0]),
            Password = Uri.UnescapeDataString(userInfoParts[1]),
            Database = uri.AbsolutePath.Trim('/'),
            SslMode = MySqlSslMode.Required
        };

        return builder.ConnectionString;
    }

    private static string EnsureSslMode(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (builder.SslMode == MySqlSslMode.None)
        {
            builder.SslMode = MySqlSslMode.Required;
        }

        return builder.ConnectionString;
    }
}
