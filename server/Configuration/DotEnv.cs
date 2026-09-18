namespace Landing.Configuration;

/// <summary>
/// Loads .env files into the process environment. Real environment variables always win,
/// and .env.local wins over .env, which mirrors how the sandbox ships defaults.
/// </summary>
public static class DotEnv
{
    public static void Load(string root)
    {
        foreach (var file in new[] { ".env.local", ".env" })
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path)) continue;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

                var separator = trimmed.IndexOf('=');
                if (separator <= 0) continue;

                var key = trimmed[..separator].Trim();
                var value = trimmed[(separator + 1)..].Trim().Trim('"', '\'');
                if (Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
    }

    /// <summary>
    /// Walks up from the binaries to the repository root so `dotnet run` and a published
    /// build both find the same .env files.
    /// </summary>
    public static string FindRepositoryRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "openmercato.toml"))) return dir.FullName;
            dir = dir.Parent;
        }
        return start;
    }
}
