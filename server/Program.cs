using Npgsql;
using StackExchange.Redis;
using Landing.Caching;
using Landing.Configuration;
using Landing.Data;
using Landing.Endpoints;
using Landing.Notifications;

var repositoryRoot = DotEnv.FindRepositoryRoot(AppContext.BaseDirectory);
DotEnv.Load(repositoryRoot);

// Pin the content root to the binaries so `dotnet .publish/<app>.dll` serves wwwroot
// no matter which directory it was started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// PostgreSQL — the only durable store.
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL is not set (defaults live in .env, overrides in .env.local)");

builder.Services.AddSingleton(_ =>
    new NpgsqlDataSourceBuilder(ConnectionUrls.ToNpgsqlConnectionString(databaseUrl)).Build());
builder.Services.AddSingleton<LeadRepository>();
builder.Services.AddSingleton(OfferSettings.FromEnvironment());

// Resend — the discount code goes to the lead, a copy of the lead goes to the inbox.
builder.Services.AddSingleton(EmailSettings.FromEnvironment());
builder.Services.AddHttpClient<ResendEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddTransient<LeadNotifier>();

builder.Services.AddSingleton(provider => new Migrator(
    provider.GetRequiredService<NpgsqlDataSource>(),
    MigrationsDirectory(repositoryRoot),
    provider.GetRequiredService<ILogger<Migrator>>()));

// Redis — optional, best-effort cache. A cache that never connects is not fatal.
IConnectionMultiplexer? redis = null;
var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");
if (!string.IsNullOrWhiteSpace(redisUrl))
{
    try
    {
        redis = ConnectionMultiplexer.Connect(ConnectionUrls.ToRedisOptions(redisUrl));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[redis] {ex.Message} — serving straight from PostgreSQL");
    }
}

var cacheTtl = TimeSpan.FromSeconds(
    double.TryParse(Environment.GetEnvironmentVariable("CACHE_TTL_SECONDS"), out var ttl) ? ttl : 30);

builder.Services.AddSingleton(provider =>
    new CacheStore(redis, cacheTtl, provider.GetRequiredService<ILogger<CacheStore>>()));

var app = builder.Build();

// `dotnet run --project server -- db <migrate|status|new>` runs the command and exits.
if (args.Length > 0 && args[0] == "db")
{
    return await DbCommand.RunAsync(app.Services, args.Skip(1).ToArray());
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapLeadEndpoints();
app.MapHealthEndpoints();

// Everything else is the React app; client-side routes resolve to index.html.
app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

// Prefer the repository copy so a freshly added .sql file is picked up without a rebuild,
// and fall back to the copy next to the binaries in a published build.
static string MigrationsDirectory(string root)
{
    var source = Path.Combine(root, "db", "migrations");
    return Directory.Exists(source) ? source : Path.Combine(AppContext.BaseDirectory, "db", "migrations");
}

// WebApplicationFactory uses this public marker to host the real HTTP pipeline in integration tests.
public partial class Program { }
