namespace Landing.Data;

/// <summary>Command-line face of the migration runner: `dotnet run --project server -- db &lt;verb&gt;`.</summary>
public static class DbCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var migrator = services.GetRequiredService<Migrator>();

        switch (args.FirstOrDefault())
        {
            case "migrate":
                await migrator.MigrateAsync();
                return 0;

            case "status":
                foreach (var state in await migrator.StatusAsync())
                {
                    var mark = state switch
                    {
                        { Modified: true } => "!",
                        { Applied: true } => "✓",
                        _ => "·",
                    };
                    var detail = state switch
                    {
                        { Modified: true } => "applied, but the file changed since",
                        { Applied: true } => $"applied {state.AppliedAt:u}",
                        _ => "pending",
                    };
                    Console.WriteLine($"{mark} {state.Name} — {detail}");
                }
                return 0;

            case "new":
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("usage: db new <name>");
                    return 1;
                }
                Console.WriteLine($"Created {migrator.CreateMigration(string.Join(' ', args[1..]))}");
                return 0;

            default:
                Console.Error.WriteLine("usage: db <migrate|status|new <name>>");
                return 1;
        }
    }
}
