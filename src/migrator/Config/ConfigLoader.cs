
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

        // --- 2. Load from .env ---
        //var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        var envPath = Utils.FindUpwards(".env");
        //if (!File.Exists(envPath)) {

        //    envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        //    Console.WriteLine($"Finding env in {envPath}");
        //    if (File.Exists(envPath))
        //    {
        //        Console.WriteLine($"Found env path in {envPath}");
        //    }

        //}

        if (File.Exists(envPath))
        {
            Console.WriteLine($"Found env path in {envPath}");

            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("MIGRATOR_CONN="))
                    config.ConnectionString = line["MIGRATOR_CONN=".Length..].Trim();

                if (line.StartsWith("MIGRATOR_PROVIDER="))
                    config.Provider = line["MIGRATOR_PROVIDER=".Length..].Trim();
            }
        }

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
            Console.WriteLine("Warning: Configuration not found, using default development configuration for migrator.");
            config.Provider ??= "SqlClient";
            config.ConnectionString ??= "Server=localhost;Database=SwiftScale.SampleApplication; User ID=sa;Password=Admin123;;Trusted_Connection=True;";
        }

        return config;
    }
}
