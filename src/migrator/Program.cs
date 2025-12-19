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
            Utils.SendHelpMessage(
                "Usage: migrator create \"Description\" " +
                "[--author \"Full Name\"] [--branch \"branch-name\"] [--transaction on|off]");
            return 1;
        }

        var description = args[1];
        string author = null;
        string branch = null;
        bool useTransaction = true;

        // Parse optional flags
        for (int i = 2; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--author", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                author = args[++i];
            }
            else if (arg.Equals("--branch", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                branch = args[++i];
            }
            else if (arg.Equals("--transaction", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var val = args[++i];

                if (val.Equals("on", StringComparison.OrdinalIgnoreCase))
                    useTransaction = true;
                else if (val.Equals("off", StringComparison.OrdinalIgnoreCase))
                    useTransaction = false;
                else
                {
                    Utils.SendErrorMessage("Invalid value for --transaction. Use 'on' or 'off'.");
                    return 1;
                }
            }
            else
            {
                Utils.SendErrorMessage($"Unknown option: {arg}");
                return 1;
            }
        }

        // If author is missing, warn user
        if (string.IsNullOrWhiteSpace(author))
        {
            Utils.SendWarningMessage("No author provided.");
            Utils.SendWarningMessage("This migration will be created but WILL NOT APPLY until an Author is added.");
            author = string.Empty;
        }

        // If branch missing, set blank but no warning needed
        branch ??= string.Empty;

        var fileEngine = new MigrationTemplateEngine(author, branch, migrationPath);
        var created = fileEngine.CreateMigrationFile(description, useTransaction);

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
                    {
                        bool force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
                        string targetVersion = "";
                        //apply specific version
                        if (args.Length >= 3 && args[1].Equals("-v", StringComparison.OrdinalIgnoreCase))
                        {
                            targetVersion = args[2];
                            await engine.ApplySpecificAsync(targetVersion);
                            return 0;
                        }
                        //apply up to version
                        else if ((args.Length >= 3 && args[1].Equals("to", StringComparison.OrdinalIgnoreCase)))
                        {
                            targetVersion = args[2];
                            await engine.ApplyMigrationsAsync(targetVersion, force);
                            return 0;
                        }
                        //apply all unapplied versions
                        else if (args.Length == 1 || (args.Length == 2 && args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase))))
                        {
                            bool confirm = Utils.ConfirmDangerousOperation("Are you sure you want to apply all pending migrations?");
                            if (!confirm)
                            {
                                Utils.SendWarningMessage("Operation aborted by user.");
                                return 0;
                            }
                            await engine.ApplyMigrationsAsync("", force);
                            return 0;
                        }

                        Utils.SendHelpMessage("Usage:");
                        Utils.SendHelpMessage("  migrator apply");
                        Utils.SendHelpMessage("  migrator apply to <version>");
                        Utils.SendHelpMessage("  migrator apply -v <version>");
                        Utils.SendHelpMessage("  migrator apply --force");
                        return 1;
                    }
                case "rollback":
                    {
                        // migrator rollback
                        if (args.Length == 1)
                        {
                            await engine.RollbackLastAsync();
                            return 0;
                        }

                        // migrator rollback all
                        if (args.Length == 2 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                        {
                            bool confirm = Utils.ConfirmDangerousOperation("Are you sure you want to rollback ALL applied migrations?");
                            if (!confirm)
                            {
                                Utils.SendWarningMessage("Operation aborted by user.");
                                return 0;
                            }
                            await engine.RollbackAllAppliedAsync();
                            return 0;
                        }

                        // migrator rollback to <version>
                        if (args.Length == 3 && args[1].Equals("to", StringComparison.OrdinalIgnoreCase))
                        {
                            await engine.RollbackToAsync(args[2]);
                            return 0;
                        }
                        // migrator rollback to <version>
                        if (args.Length == 3 && args[1].Equals("-v", StringComparison.OrdinalIgnoreCase))
                        {
                            await engine.RollbackVersionAsync(args[2]);
                            return 0;
                        }

                        Utils.SendHelpMessage("Usage:");
                        Utils.SendHelpMessage("  migrator rollback");
                        Utils.SendHelpMessage("  migrator rollback to <version>");
                        Utils.SendHelpMessage("  migrator rollback all");
                        Utils.SendHelpMessage("  migrator rollback -v <version>");

                        return 1;
                    }

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
                case "--help":
                case "-h":
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









