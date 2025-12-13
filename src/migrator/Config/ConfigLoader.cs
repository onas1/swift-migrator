
using migrator.Engine;
using System.Text.Json;

namespace migrator.Config;






public static class ConfigLoader
{
    public static MigratorConfig Load(string[] args)
    {
        var config = new MigratorConfig();

        // --- 1. Load from migrator.json ---
        //var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "migrator.json");
        var jsonPath = Utils.FindUpwards( "migrator.json");

        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath);
            var fileConfig = JsonSerializer.Deserialize<MigratorConfig>(json);
            if (fileConfig != null)
            {
                config.Provider = fileConfig.Provider;
                config.ConnectionString = fileConfig.ConnectionString;
            }
        }
        else
        Utils.SendWarningMessage("No migrator.json file found.");

        // --- 2. Load from .env ---
        var envPath = Utils.FindUpwards(".env");
        if (File.Exists(envPath))
        {
            Utils.SendInfoMessage($"Found .env file.");

            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("MIGRATOR_CONN="))
                    config.ConnectionString = line["MIGRATOR_CONN=".Length..].Trim();

                if (line.StartsWith("MIGRATOR_PROVIDER="))
                    config.Provider = line["MIGRATOR_PROVIDER=".Length..].Trim();
            }
        }
        else
            Utils.SendWarningMessage("No .env file found.");

        // --- 3. Environment Variables ---
        config.Provider ??= Environment.GetEnvironmentVariable("MIGRATOR_PROVIDER");
        config.ConnectionString ??= Environment.GetEnvironmentVariable("MIGRATOR_CONN");

        // --- 4. CLI Flags ---
        foreach (var arg in args)
        {
            if (arg.StartsWith("--conn="))
                config.ConnectionString = arg["--conn=".Length..];

            if (arg.StartsWith("--provider="))
                config.Provider = arg["--provider=".Length..];
        }

        // --- 5. Fallback defaults (development only) ---
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            Utils.SendErrorMessage(" ERROR: Configuration not found, using default development configuration for migrator. Set up database provider. ");
            config.Provider ??= "";
            config.ConnectionString ??= "";
        }

        return config;
    }
}
