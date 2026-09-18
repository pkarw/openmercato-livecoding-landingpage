using Npgsql;
using StackExchange.Redis;

namespace Landing.Configuration;

/// <summary>
/// The sandbox hands out URL-style connection strings (postgres://…, redis://…);
/// the .NET clients want their own formats.
/// </summary>
public static class ConnectionUrls
{
    public static string ToNpgsqlConnectionString(string url)
    {
        if (!url.Contains("://", StringComparison.Ordinal)) return url; // already key=value

        var uri = new Uri(url);
        var userInfo = Uri.UnescapeDataString(uri.UserInfo).Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = userInfo.ElementAtOrDefault(0) ?? string.Empty,
            Password = userInfo.ElementAtOrDefault(1) ?? string.Empty,
            MaxPoolSize = 5,
        };

        foreach (var (key, value) in ParseQuery(uri.Query))
        {
            if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }

        return builder.ConnectionString;
    }

    public static ConfigurationOptions ToRedisOptions(string url)
    {
        if (!url.Contains("://", StringComparison.Ordinal)) return ConfigurationOptions.Parse(url);

        var uri = new Uri(url);
        var userInfo = Uri.UnescapeDataString(uri.UserInfo).Split(':', 2);

        var options = new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.IsDefaultPort ? 6379 : uri.Port } },
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase),
            AbortOnConnectFail = false, // the cache is best-effort; Postgres keeps serving
            ConnectTimeout = 2_000,
            ConnectRetry = 1,
        };

        if (userInfo.Length == 2)
        {
            if (userInfo[0].Length > 0) options.User = userInfo[0];
            options.Password = userInfo[1];
        }
        else if (userInfo.Length == 1 && userInfo[0].Length > 0)
        {
            options.Password = userInfo[0];
        }

        var database = uri.AbsolutePath.TrimStart('/');
        if (int.TryParse(database, out var index)) options.DefaultDatabase = index;

        return options;
    }

    private static IEnumerable<(string Key, string Value)> ParseQuery(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2) yield return (parts[0], Uri.UnescapeDataString(parts[1]));
        }
    }
}
