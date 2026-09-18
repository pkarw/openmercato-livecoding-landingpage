using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace Landing.Data;

public sealed record MigrationState(string Name, bool Applied, DateTimeOffset? AppliedAt, bool Modified);

/// <summary>
/// Applies db/migrations/*.sql in filename order, once each, inside a transaction,
/// tracking what ran in schema_migrations. Each file's checksum is recorded so an
/// edit to an already-applied migration is reported instead of silently ignored.
/// </summary>
public sealed class Migrator(NpgsqlDataSource dataSource, string migrationsDirectory, ILogger<Migrator> logger)
{
    private const string EnsureTableSql = """
        create table if not exists schema_migrations (
          name text primary key,
          applied_at timestamptz not null default now()
        );
        alter table schema_migrations add column if not exists checksum text;
        """;

    public string Directory => migrationsDirectory;

    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(EnsureTableSql);

        var applied = (await connection.QueryAsync<string>("select name from schema_migrations")).ToHashSet();
        var pending = ReadMigrationFiles().Where(file => !applied.Contains(file.Name)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database is up to date ({Count} migrations applied).", applied.Count);
            return 0;
        }

        foreach (var file in pending)
        {
            var sql = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await connection.ExecuteAsync(sql, transaction: transaction);
                await connection.ExecuteAsync(
                    "insert into schema_migrations (name, checksum) values (@name, @checksum)",
                    new { name = file.Name, checksum = Checksum(sql) },
                    transaction);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Applied {Migration}", file.Name);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                logger.LogError("Failed on {Migration} — rolled back", file.Name);
                throw;
            }
        }

        return pending.Count;
    }

    public async Task<IReadOnlyList<MigrationState>> StatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(EnsureTableSql);

        var applied = (await connection.QueryAsync<(string Name, DateTimeOffset AppliedAt, string? Checksum)>(
                "select name, applied_at, checksum from schema_migrations"))
            .ToDictionary(row => row.Name);

        var states = new List<MigrationState>();
        foreach (var file in ReadMigrationFiles())
        {
            if (!applied.TryGetValue(file.Name, out var row))
            {
                states.Add(new MigrationState(file.Name, Applied: false, AppliedAt: null, Modified: false));
                continue;
            }

            var checksum = Checksum(await File.ReadAllTextAsync(file.FullName, cancellationToken));
            var modified = row.Checksum is not null && row.Checksum != checksum;
            states.Add(new MigrationState(file.Name, Applied: true, row.AppliedAt, modified));
        }

        return states;
    }

    /// <summary>Scaffolds a timestamped migration file and returns its path.</summary>
    public string CreateMigration(string name)
    {
        var slug = string.Concat(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_'))
            .Trim('_');
        if (slug.Length == 0) throw new ArgumentException("Migration name must contain letters or digits.", nameof(name));

        System.IO.Directory.CreateDirectory(migrationsDirectory);
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{slug}.sql";
        var path = Path.Combine(migrationsDirectory, fileName);
        File.WriteAllText(path, $"-- {fileName}\n-- Runs once, inside a transaction.\n\n");
        return path;
    }

    private IEnumerable<FileInfo> ReadMigrationFiles()
    {
        if (!System.IO.Directory.Exists(migrationsDirectory))
        {
            throw new DirectoryNotFoundException($"No migrations directory at {migrationsDirectory}");
        }

        return new DirectoryInfo(migrationsDirectory)
            .GetFiles("*.sql")
            .OrderBy(file => file.Name, StringComparer.Ordinal);
    }

    private static string Checksum(string sql) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.ReplaceLineEndings("\n")))).ToLowerInvariant();
}
