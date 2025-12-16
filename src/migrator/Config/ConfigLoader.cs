
using migrator.Engine;
using System.Text.Json;

namespace migrator.Config;

public static class ConfigLoader
{
    public static MigratorConfig Load(string[] args)
    {
        var config = new MigratorConfig();

        // 1. .env
        ApplyEnvFile(config);

        // 2. migrator.json
        ApplyJsonFile(config);

        // 3. Environment variables
        ApplyEnvironment(config);

        // 4. CLI args (highest priority)
        ApplyCliArgs(config, args);

        Validate(config);

        return config;
    }







    private static void ApplyEnvFile(MigratorConfig config)
    {
        if( IsComplete(config)) return;
        var path = Utils.FindUpwards(".env");
        if (!File.Exists(path))
        {
            Utils.SendWarningMessage("No .env file found.");
            return;
        }

        Utils.SendInfoMessage("Found .env file.");

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("MIGRATOR_CONN="))
                config.ConnectionString ??= line["MIGRATOR_CONN=".Length..].Trim();

            else if (line.StartsWith("MIGRATOR_PROVIDER="))
                config.Provider ??= line["MIGRATOR_PROVIDER=".Length..].Trim();
        }
    }



    private static void ApplyJsonFile(MigratorConfig config)
    {
        if (IsComplete(config)) return;
        var path = Utils.FindUpwards("migrator.json");
        if (!File.Exists(path))
        {
            Utils.SendWarningMessage("No migrator.json file found.");
            return;
        }

        Utils.SendInfoMessage("Found migrator.json file.");

        var json = File.ReadAllText(path);
        var fileConfig = JsonSerializer.Deserialize<MigratorConfig>(json);
        if (fileConfig == null) return;

        config.Provider ??= fileConfig.Provider;
        config.ConnectionString ??= fileConfig.ConnectionString;
    }



    private static void ApplyEnvironment(MigratorConfig config)
    {
        config.Provider ??= Environment.GetEnvironmentVariable("MIGRATOR_PROVIDER");
        config.ConnectionString ??= Environment.GetEnvironmentVariable("MIGRATOR_CONN");
    }


    private static void ApplyCliArgs(MigratorConfig config, string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--conn="))
                config.ConnectionString = arg["--conn=".Length..];

            else if (arg.StartsWith("--provider="))
                config.Provider = arg["--provider=".Length..];
        }
    }

    private static bool IsComplete(MigratorConfig config) =>
      !string.IsNullOrWhiteSpace(config.Provider) && !string.IsNullOrWhiteSpace(config.ConnectionString);
    

    private static void Validate(MigratorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            Utils.SendErrorMessage("ERROR: No connection string configured.");
            throw new InvalidOperationException("No configuration found. Provide connection string via .env, migrator.json, environment variables, or CLI." );
        }

        if (string.IsNullOrWhiteSpace(config.Provider))
        {
            Utils.SendErrorMessage("ERROR: No provider configured.");
            throw new InvalidOperationException("Database provider is required.");
        }
    }







}
