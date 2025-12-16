using migrator.Config;
using migrator.Engine;


namespace migrator;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        return await RunMigrationToolAsync(args);

    }

    public static int CreateMigrationFile(string[] args, string migrationPath)
    {
        if (args.Length < 2)
        {
            Utils.SendHelpMessage("Usage: migrator create \"Description\" [--author \"Name\"] [--branch \"BranchName\"]");
            return 1;
        }

        var description = args[1];
        string author = null;
        string branch = null;

        // Parse optional flags
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--author" && i + 1 < args.Length)
            {
                author = args[i + 1];
                i++;
            }
            else if (args[i] == "--branch" && i + 1 < args.Length)
            {
                branch = args[i + 1];
                i++;
            }
        }

        // If author is missing, warn user
        if (string.IsNullOrWhiteSpace(author))
        {
            Utils.SendWarningMessage("⚠️ Warning: No author provided.");
            Utils.SendWarningMessage("   This migration will be created, but WILL NOT APPLY until you add an Author to the file.");
            author = ""; // Create empty field in the file
        }

        // If branch missing, set blank but no warning needed
        if (string.IsNullOrWhiteSpace(branch))
            branch = "";
        MigrationTemplateEngine fileEngine = new(author, branch, migrationPath);
        var created = fileEngine.CreateMigrationFile(description);
        Utils.SendInfoMessage($"Created migration: {created}");
        return 0;
    }




    private static async Task<int> RunMigrationToolAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Utils.SendTitleMessage("Migration Tool");
                Utils.SendTitleMessage("--------------");
                Utils.SendHelpMessage("Usage: migrator <command> [options]");
                Utils.SendHelpMessage("");
                Utils.SendHelpMessage("Run 'migrator help' for a list of available commands.");
                return 0;
            }


            var command = args[0].ToLowerInvariant();

            var migrationsPath = Path.Combine(Environment.CurrentDirectory, "migrations");
            Directory.CreateDirectory(migrationsPath);

            MigratorConfig config = ConfigLoader.Load(args);
            var engine = new MigrationEngine(migrationsPath, config.Provider, config.ConnectionString);
            engine.SetupSupportedProviders();

            switch (command)
            {
                case "create":
                    int r = CreateMigrationFile(args, migrationsPath);
                    return r;

                case "status":
                    var status = await engine.GetStatusAsync();
                    Utils.SendInfoMessage(status);
                    return 0;

                case "apply":
                    await engine.ApplyMigrationsAsync();
                    return 0;
                case "rollback":
                    await engine.RollbackLastAsync();
                    return 0;
                case "redo":
                    if (args.Length < 2)
                    {
                        Utils.SendHelpMessage("Usage: migrator redo \"20251211001611_c4128f\"");
                        return 1;
                    }
                    var version = args[1];
                    await engine.RedoAsync(version);
                    return 0;
                case "help":
                    Utils.PrintHelp();
                    return 0;

                default:
                    Utils.SendWarningMessage("Unknown command. Run 'migrator help' for usage.");
                    return 1;
            }
        }
        catch (Exception ex)
        {
          Utils.SendErrorMessage($"Error: {ex.Message.Replace(Environment.NewLine, " ")}");
            return 1;
        }
    }
}









